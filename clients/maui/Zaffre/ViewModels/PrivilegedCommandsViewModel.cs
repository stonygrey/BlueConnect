// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

using Microsoft.Extensions.Logging;

using ReactiveUI.Reactive;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;

using Zaffre.Services.Abstractions;

namespace Zaffre.ViewModels;

/// <summary>
/// Commands issued under one pairing role. The Low and High pages share this
/// shape; the device is the authority on what each role may actually do —
/// a denied command shows the device's own PERMISSION_DENIED verdict, which
/// is the point of the low-privilege Echo demo.
/// </summary>
public abstract class PrivilegedCommandsViewModel : ViewModelBase
{
    private readonly ICommandService _commands;
    private readonly INoiseCredentialService _pairing;
    private readonly DeviceSelection _selection;
    private readonly ILogger _logger;
    private readonly ObservableAsPropertyHelper<bool> _canRun;
    private readonly ObservableAsPropertyHelper<bool> _isPaired;
    private readonly ObservableAsPropertyHelper<CredentialOrigin> _credentialOrigin;
    private readonly ObservableAsPropertyHelper<bool> _deviceSecured;
    private readonly ObservableAsPropertyHelper<string> _pairingStatus;
    private readonly ObservableAsPropertyHelper<string> _deviceName;

    private string _echoInput = "Hello, Verdigris!";
    private string _provisionInput = string.Empty;
    private readonly IToasts _toasts;
    private readonly IQrScanService _qrScanner;
    private bool? _ledOn;
    private string _serialInput = RandomSerial();
    private string _pairResult = string.Empty;
    private bool _isBusy;

    protected PrivilegedCommandsViewModel(
        PairingRole role,
        ICommandService commands,
        IDiagnosticsService diagnostics,
        INoiseCredentialService pairing,
        IScanner scanner,
        DeviceSelection selection,
        IToasts toasts,
        IQrScanService qrScanner,
        ILogger logger)
    {
        Role = role;
        _commands = commands;
        _toasts = toasts;
        _qrScanner = qrScanner;
        _pairing = pairing;
        _selection = selection;
        _logger = logger;

        // Loads persisted pairing state into the service's stream.
        _ = _pairing.InitializeAsync();

        var loopIdle = diagnostics
            .Select(s => !s.IsRunning)
            .DistinctUntilChanged();

        var currentDevice = _selection.WhenAnyValue(x => x.Current);
        var hasDevice = currentDevice.Select(d => d is not null);

        // One operation at a time: while any command is in flight, every
        // button is disabled so the UI state is never ambiguous.
        var notBusy = this.WhenAnyValue(x => x.IsBusy).Select(busy => !busy);

        var canRun = loopIdle.CombineLatest(hasDevice, notBusy, (idle, has, free) => idle && has && free);

        _canRun = canRun
            .ToProperty(this, x => x.CanRun)
            .DisposeWith(Disposables);

        var deviceName = currentDevice
            .Select(current => current is null
                ? Observable.Return(string.Empty)
                : scanner
                    .Where(s => s.Advertisement.Id == current.Id)
                    .Select(s => s.Advertisement.LocalName)
                    .StartWith(current.LocalName))
            .Switch();

        _deviceName = deviceName
            .ToProperty(this, x => x.DeviceName)
            .DisposeWith(Disposables);

        // The selected device's advertised security state (any slot), kept current.
        var deviceSecured = currentDevice
            .Select(current => current is null
                ? Observable.Return(false)
                : scanner
                    .Where(s => s.Advertisement.Id == current.Id)
                    .Select(s => s.Advertisement.IsSecured)
                    .StartWith(current.IsSecured))
            .Switch()
            .DistinctUntilChanged();

        _deviceSecured = deviceSecured
            .ToProperty(this, x => x.DeviceSecured)
            .DisposeWith(Disposables);

        var roleState = _pairing
            .Select(profiles => profiles.For(role));

        _isPaired = roleState
            .Select(p => p.IsPaired)
            .ToProperty(this, x => x.IsPaired)
            .DisposeWith(Disposables);

        _credentialOrigin = roleState
            .Select(p => p.Origin)
            .ToProperty(this, x => x.CredentialOrigin)
            .DisposeWith(Disposables);

        _pairingStatus = roleState
            .CombineLatest(deviceSecured, (p, secured) => (p.IsPaired, secured) switch
            {
                (true, true) => "Sessions are encrypted.",
                (true, false) => $"Paired ({RoleName(role)}){WithName(p.DeviceName)}, but the device reports no pairings. It may have been reset; unpair to start over.",
                (false, _) => $"Not paired ({RoleName(role)}). Pair to run this page's commands",
            })
            .ToProperty(this, x => x.PairingStatus)
            .DisposeWith(Disposables);

        // The status line is the single source of truth for pairing state; the
        // transient line below it must never contradict it.
        roleState
            .Select(p => p.IsPaired)
            .DistinctUntilChanged()
            .Skip(1)
            .Subscribe(_ => PairResult = string.Empty)
            .DisposeWith(Disposables);

        EchoCommand = ReactiveCommand.CreateFromTask(RunEcho, canRun).DisposeWith(Disposables);
        GetLedCommand = ReactiveCommand.CreateFromTask(RunGetLed, canRun).DisposeWith(Disposables);
        SetLedCommand = ReactiveCommand.CreateFromTask<bool>(RunSetLed, canRun).DisposeWith(Disposables);
        GetSerialNumberCommand = ReactiveCommand.CreateFromTask(RunGetSerialNumber, canRun).DisposeWith(Disposables);
        SetSerialNumberCommand = ReactiveCommand.CreateFromTask(RunSetSerialNumber, canRun).DisposeWith(Disposables);
        PairCommand = ReactiveCommand.CreateFromTask(RunPair, canRun).DisposeWith(Disposables);
        ScanProvisionedCommand = ReactiveCommand.CreateFromTask(RunScanProvisioned, canRun).DisposeWith(Disposables);
        SaveProvisionedCommand = ReactiveCommand.CreateFromTask(RunSaveProvisioned, canRun).DisposeWith(Disposables);
        UnpairCommand = ReactiveCommand.CreateFromTask(RunUnpair, canRun).DisposeWith(Disposables);
    }

    public PairingRole Role { get; }

    /// <summary>True on the high-privilege page: the write commands are shown.</summary>
    public bool ShowWriteCommands => Role == PairingRole.High;

    public string RoleTitle => Role == PairingRole.High ? "High privilege" : "Low privilege";

    /// <summary>Which DK button confirms this page's pairing ceremony.</summary>
    public string ConfirmButton => Role == PairingRole.High ? "Button 3" : "Button 2";

    public bool CanRun => _canRun.Value;

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }

    public bool IsPaired => _isPaired.Value;

    /// <summary>How the role's credential was obtained; meaningful only while <see cref="IsPaired"/>.</summary>
    public CredentialOrigin CredentialOrigin => _credentialOrigin.Value;

    /// <summary>What the selected device advertises: any pairing slot pinned.</summary>
    public bool DeviceSecured => _deviceSecured.Value;

    public string DeviceName => _deviceName.Value;

    public string PairingStatus => _pairingStatus.Value;

    public string PairResult { get => _pairResult; private set => this.RaiseAndSetIfChanged(ref _pairResult, value); }

    public string EchoInput { get => _echoInput; set => this.RaiseAndSetIfChanged(ref _echoInput, value); }

    /// <summary>Pasted SGNK1 payload for the USB-provisioned Noise credential path.</summary>
    public string ProvisionInput { get => _provisionInput; set => this.RaiseAndSetIfChanged(ref _provisionInput, value); }

    public bool CanScan => _qrScanner.IsSupported;

    /// <summary>Transient text every command's result line shows while the command runs.</summary>
    public const string WorkingText = "Working…";

    /// <summary>Last known state of the board's LED 1; null until read or set this session.</summary>
    public bool? LedOn { get => _ledOn; private set => this.RaiseAndSetIfChanged(ref _ledOn, value); }

    /// <summary>
    /// The serial number the next Write sends. Seeded with a random non-zero
    /// value at startup and again after every successful write.
    /// </summary>
    public string SerialInput { get => _serialInput; set => this.RaiseAndSetIfChanged(ref _serialInput, value); }


    public ReactiveCommand<Unit, Unit> EchoCommand { get; }

    public ReactiveCommand<Unit, Unit> GetLedCommand { get; }

    public ReactiveCommand<bool, Unit> SetLedCommand { get; }

    public ReactiveCommand<Unit, Unit> GetSerialNumberCommand { get; }

    public ReactiveCommand<Unit, Unit> SetSerialNumberCommand { get; }

    public ReactiveCommand<Unit, Unit> PairCommand { get; }

    public ReactiveCommand<Unit, Unit> ScanProvisionedCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveProvisionedCommand { get; }

    public ReactiveCommand<Unit, Unit> UnpairCommand { get; }

    private static string RoleName(PairingRole role) => role == PairingRole.High ? "high" : "low";

    private static string WithName(string? name) => name is { Length: > 0 } ? $" with '{name}'" : string.Empty;

    private Task RunEcho() => RunToasting(
        async ad =>
        {
            var echoed = await _commands.Echo(ad, Role, Encoding.UTF8.GetBytes(EchoInput));
            return Encoding.UTF8.GetString(echoed);
        });

    // The LED card carries no text: the indicator itself shows the outcome.
    private Task RunGetLed() => Run(
        async ad =>
        {
            LedOn = await _commands.GetLed(ad, Role);
            return string.Empty;
        },
        _ => { });

    private Task RunSetLed(bool on) => Run(
        async ad =>
        {
            LedOn = await _commands.SetLed(ad, Role, on);
            return string.Empty;
        },
        _ => { });

    private Task RunGetSerialNumber() => RunToasting(
        async ad => $"Advertising as '{await _commands.GetSerialNumber(ad, Role)}'");

    private Task RunSetSerialNumber()
    {
        if (!ushort.TryParse(SerialInput.Trim(), out var serialNumber) || serialNumber == 0)
        {
            _toasts.Error("Serial number must be a whole number from 1 to 65535.");
            return Task.CompletedTask;
        }

        return RunToasting(
            async ad =>
            {
                var name = await _commands.SetSerialNumber(ad, Role, serialNumber);
                // The write stuck; offer a fresh value for the next one.
                SerialInput = RandomSerial();
                return $"Now advertising as '{name}'";
            });
    }

    /// <summary>Run a command whose response is shown as an info toast rather than card text.</summary>
    private Task RunToasting(Func<IAdvertisement, Task<string>> command) => Run(
        command,
        r =>
        {
            if (r.Length > 0 && r != WorkingText)
            {
                _toasts.Info(r);
            }
        });

    private static string RandomSerial() => Random.Shared.Next(1, ushort.MaxValue + 1).ToString();

    private Task RunPair() => Run(
        async ad =>
        {
            PairResult = ConfirmPrompt(timeout: false);
            try
            {
                await _pairing.Pair(ad, Role);
                // Success needs no extra line: the status above now says it.
                return string.Empty;
            }
            catch (StonyGrey.BlueConnect.Verdigris.VerdigrisException ex)
                when (ex.Status == StonyGrey.BlueConnect.Verdigris.Protocol.Status.Unauthenticated)
            {
                return string.IsNullOrEmpty(ex.Detail)
                    ? ConfirmPrompt(timeout: true)
                    : $"Pairing not confirmed: {ex.Detail}";
            }
        },
        r => PairResult = r);

    private Task RunScanProvisioned() => RunCredential(async () =>
    {
        var text = await _qrScanner.ScanAsync();
        if (text is null)
        {
            // Cancelled — nothing to report.
            return;
        }

        await StoreProvisionedPayload(text);
    });

    private Task RunSaveProvisioned() => RunCredential(async () =>
    {
        var input = ProvisionInput.Trim();
        if (input.Length == 0)
        {
            _toasts.Error("Paste the SGNK1 payload first.");
            return;
        }

        if (await StoreProvisionedPayload(input))
        {
            ProvisionInput = string.Empty;
        }
    });

    private async Task<bool> StoreProvisionedPayload(string text)
    {
        if (NoiseEnrollmentPayload.TryParse(text, out var enrollment))
        {
            if (Role != PairingRole.Low)
            {
                _toasts.Warning("An enrollment QR enrolls the low tier — scan it on the low card.");
                return false;
            }

            if (_selection.Current is not { } advertisement)
            {
                _toasts.Error("Select the device first.");
                return false;
            }

            await _pairing.Enroll(advertisement, enrollment!);
            return true;
        }

        if (!NoiseQrPayload.TryParse(text, out var payload))
        {
            _toasts.Error("That is not a BlueConnect Noise credential (SGNK1) or enrollment (SGNE1) payload.");
            return false;
        }

        var tier = Role == PairingRole.High ? Tier.High : Tier.Low;
        if (payload!.Tier != tier)
        {
            var name = payload.Tier == Tier.High ? "high" : "low";
            _toasts.Warning($"That QR is the {name} Noise credential — scan it on the {name} card.");
            return false;
        }

        await _pairing.StorePayload(Role, payload);
        return true;
    }

    private async Task RunCredential(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.Here().LogWarning(ex, "Provisioned-credential action failed for {Role}.", Role);
            _toasts.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string ConfirmPrompt(bool timeout)
    {
        var button = Role == PairingRole.High ? "Button 3" : "Button 2";
        return timeout
            ? $"Pairing not confirmed. Press {button} on the device within 30 seconds and try again."
            : $"Press {button} on the device to confirm…";
    }

    private Task RunUnpair() => Run(
        async ad =>
        {
            await _pairing.Unpair(ad, Role);
            // Success needs no extra line: the status above now says it.
            return string.Empty;
        },
        r => PairResult = r);

    private async Task Run(Func<IAdvertisement, Task<string>> command, Action<string> setResult)
    {
        if (_selection.Current is not { } advertisement)
        {
            return;
        }

        IsBusy = true;
        try
        {
            setResult(WorkingText);
            setResult(await command(advertisement));
        }
        catch (Exception ex)
        {
            _logger.Here().LogWarning(ex, "Verdigris command failed against '{Device}'.", advertisement.LocalName);
            setResult(string.Empty);
            _toasts.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class LowPrivilegeCommandsViewModel : PrivilegedCommandsViewModel
{
    public LowPrivilegeCommandsViewModel(
        ICommandService commands,
        IDiagnosticsService diagnostics,
        INoiseCredentialService pairing,
        IScanner scanner,
        DeviceSelection selection,
        IToasts toasts,
        IQrScanService qrScanner,
        ILogger<LowPrivilegeCommandsViewModel> logger)
        : base(PairingRole.Low, commands, diagnostics, pairing, scanner, selection, toasts, qrScanner, logger)
    {
    }
}

public sealed class HighPrivilegeCommandsViewModel : PrivilegedCommandsViewModel
{
    public HighPrivilegeCommandsViewModel(
        ICommandService commands,
        IDiagnosticsService diagnostics,
        INoiseCredentialService pairing,
        IScanner scanner,
        DeviceSelection selection,
        IToasts toasts,
        IQrScanService qrScanner,
        ILogger<HighPrivilegeCommandsViewModel> logger)
        : base(PairingRole.High, commands, diagnostics, pairing, scanner, selection, toasts, qrScanner, logger)
    {
    }
}

