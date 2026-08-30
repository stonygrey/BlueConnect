// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;

using ReactiveUI.Reactive;

using StonyGrey.Spake2Plus;

using Zaffre.Services.Abstractions;

namespace Zaffre.ViewModels;

/// <summary>
/// The PAKE tab's credential cards: one setup code per tier, entered by
/// scanning the device's QR (SGPK1 payload) or typing. There is no pairing
/// ceremony — a stored code is everything a session needs.
/// </summary>
public sealed class PakeCredentialsViewModel : ViewModelBase
{
    private readonly IPakeCredentialService _credentials;
    private readonly IQrScanService _scanner;
    private readonly IToasts _toasts;
    private readonly ILogger<PakeCredentialsViewModel> _logger;
    private readonly ObservableAsPropertyHelper<PakeCredentials> _state;

    private string _lowInput = string.Empty;
    private string _highInput = string.Empty;
    private bool _isBusy;

    public PakeCredentialsViewModel(
        IPakeCredentialService credentials,
        IQrScanService scanner,
        IToasts toasts,
        ILogger<PakeCredentialsViewModel> logger)
    {
        _credentials = credentials;
        _scanner = scanner;
        _toasts = toasts;
        _logger = logger;

        // Loads persisted credential state into the service's stream.
        _ = _credentials.InitializeAsync();

        _state = _credentials
            .ToProperty(this, x => x.State)
            .DisposeWith(Disposables);
    }

    public PakeCredentials State => _state.Value;

    public bool CanScan => _scanner.IsSupported;

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }

    public string LowInput { get => _lowInput; set => this.RaiseAndSetIfChanged(ref _lowInput, value); }

    public string HighInput { get => _highInput; set => this.RaiseAndSetIfChanged(ref _highInput, value); }

    public string StatusFor(PairingRole role)
    {
        var state = State.For(role);
        if (!state.HasCode)
        {
            return "No setup code.";
        }

        return "Sessions are encrypted.";
    }

    /// <summary>Scan a QR; the payload's tier must match this card's.</summary>
    public Task Scan(PairingRole role) => Run(async () =>
    {
        var text = await _scanner.ScanAsync();
        if (text is null)
        {
            // Cancelled — nothing to report.
            return;
        }

        if (!PakeQrPayload.TryParse(text, out var payload))
        {
            _toasts.Error("That QR is not a BlueConnect setup code.");
            return;
        }

        if (payload!.Tier != TierFor(role))
        {
            var tier = TierName(payload.Tier);
            _toasts.Warning($"That QR is the {tier} setup code — scan it on the {tier} card.");
            return;
        }

        await _credentials.StoreCode(role, payload.Code, payload.DeviceName);
    });

    /// <summary>Store a typed code; full SGPK1 payloads are accepted too.</summary>
    public Task SaveManual(PairingRole role) => Run(async () =>
    {
        var input = (role == PairingRole.High ? HighInput : LowInput).Trim();
        if (input.Length == 0)
        {
            _toasts.Error("Enter the setup code first.");
            return;
        }

        var code = input;
        string? name = null;
        if (PakeQrPayload.TryParse(input, out var payload))
        {
            if (payload!.Tier != TierFor(role))
            {
                var tier = TierName(payload.Tier);
                _toasts.Warning($"That payload is the {tier} setup code — save it on the {tier} card.");
                return;
            }

            code = payload.Code;
            name = payload.DeviceName;
        }
        else if (!SetupCode.IsValid(code))
        {
            _toasts.Error("That is not a valid setup code (16 characters, XXXX-XXXX-XXXX-XXXX).");
            return;
        }

        await _credentials.StoreCode(role, code, name);
        if (role == PairingRole.High)
        {
            HighInput = string.Empty;
        }
        else
        {
            LowInput = string.Empty;
        }
    });

    public Task Clear(PairingRole role) => Run(() => _credentials.Clear(role));

    private async Task Run(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.Here().LogWarning(ex, "PAKE credential action failed.");
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
