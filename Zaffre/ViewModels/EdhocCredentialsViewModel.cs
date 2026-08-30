// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;

using ReactiveUI.Reactive;

using StonyGrey.BlueConnect.Secure;
using StonyGrey.Spake2Plus;

using Zaffre.Services.Abstractions;

namespace Zaffre.ViewModels;

/// <summary>
/// The EDHOC tab's credential cards: one credential per tier, either paired
/// via an enrolling handshake plus the device's button ceremony, or entered
/// by scanning the provisioning QR (SGEK1 payload) or pasting it. A stored
/// credential is everything a session needs.
/// </summary>
public sealed class EdhocCredentialsViewModel : ViewModelBase
{
    private readonly IEdhocCredentialService _credentials;
    private readonly IQrScanService _scanner;
    private readonly IToasts _toasts;
    private readonly DeviceSelection _selection;
    private readonly ILogger<EdhocCredentialsViewModel> _logger;
    private readonly ObservableAsPropertyHelper<EdhocCredentials> _state;

    private string _lowInput = string.Empty;
    private string _highInput = string.Empty;
    private bool _isBusy;

    public EdhocCredentialsViewModel(
        IEdhocCredentialService credentials,
        IQrScanService scanner,
        IToasts toasts,
        DeviceSelection selection,
        ILogger<EdhocCredentialsViewModel> logger)
    {
        _credentials = credentials;
        _scanner = scanner;
        _toasts = toasts;
        _selection = selection;
        _logger = logger;

        // Loads persisted credential state into the service's stream.
        _ = _credentials.InitializeAsync();

        _state = _credentials
            .ToProperty(this, x => x.State)
            .DisposeWith(Disposables);
    }

    public EdhocCredentials State => _state.Value;

    public bool CanScan => _scanner.IsSupported;

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }

    public string LowInput { get => _lowInput; set => this.RaiseAndSetIfChanged(ref _lowInput, value); }

    public string HighInput { get => _highInput; set => this.RaiseAndSetIfChanged(ref _highInput, value); }

    public string StatusFor(PairingRole role)
    {
        var state = State.For(role);
        if (!state.HasCredential)
        {
            return "No credential.";
        }

        return "Sessions are encrypted.";
    }

    /// <summary>
    /// Enroll with the selected device: fresh keys both ends, pinned by the
    /// device's button ceremony. Refused when the slot is USB-provisioned.
    /// </summary>
    public Task Pair(PairingRole role) => Run(async () =>
    {
        if (_selection.Current is not { } advertisement)
        {
            _toasts.Error("Select a device on the scan page first.");
            return;
        }

        await _credentials.Pair(advertisement, role);
    });

    /// <summary>Scan a QR; the payload's tier must match this card's.</summary>
    public Task Scan(PairingRole role) => Run(async () =>
    {
        var text = await _scanner.ScanAsync();
        if (text is null)
        {
            // Cancelled — nothing to report.
            return;
        }

        await Store(role, text);
    });

    /// <summary>Store a pasted SGEK1 payload.</summary>
    public Task SaveManual(PairingRole role) => Run(async () =>
    {
        var input = (role == PairingRole.High ? HighInput : LowInput).Trim();
        if (input.Length == 0)
        {
            _toasts.Error("Paste the SGEK1 payload first.");
            return;
        }

        if (await Store(role, input))
        {
            if (role == PairingRole.High)
            {
                HighInput = string.Empty;
            }
            else
            {
                LowInput = string.Empty;
            }
        }
    });

    public Task Clear(PairingRole role) => Run(() => _credentials.Clear(role));

    private async Task<bool> Store(PairingRole role, string text)
    {
        if (!EdhocQrPayload.TryParse(text, out var payload))
        {
            _toasts.Error("That is not a BlueConnect EDHOC credential (SGEK1 payload).");
            return false;
        }

        if (payload!.Tier != TierFor(role))
        {
            var tier = TierName(payload.Tier);
            _toasts.Warning($"That QR is the {tier} EDHOC credential — scan it on the {tier} card.");
            return false;
        }

        await _credentials.StorePayload(role, payload);
        return true;
    }

    private async Task Run(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.Here().LogWarning(ex, "EDHOC credential action failed.");
            _toasts.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static PakeTier TierFor(PairingRole role) => role == PairingRole.High ? PakeTier.High : PakeTier.Low;

    private static string TierName(PakeTier tier) => tier == PakeTier.High ? "high" : "low";
}
