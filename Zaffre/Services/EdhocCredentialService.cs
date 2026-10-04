// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;
using StonyGrey.BlueConnect.Verdigris;
using StonyGrey.Edhoc;
using StonyGrey.Spake2Plus;

using StonyGrey.Shared.Services;

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

public sealed class EdhocCredentialService : ObservableService<EdhocCredentials>, IEdhocCredentialService
{
    private readonly IScanner _scanner;
    private readonly BlueConnectOptions _options;
    private readonly ILogger<EdhocCredentialService> _logger;

    public EdhocCredentialService(
        IScanner scanner,
        IOptions<BlueConnectOptions> options,
        ILogger<EdhocCredentialService> logger)
        : base(new EdhocCredentials(
            new EdhocCredentialState(false, null), new EdhocCredentialState(false, null)))
    {
        _scanner = scanner;
        _options = options.Value;
        _logger = logger;
    }

    private static string PayloadKey(PairingRole role) => $"zaffre.edhoc.cred.{RoleKey(role)}";

    private static string NameKey(PairingRole role) => $"zaffre.edhoc.device.name.{RoleKey(role)}";

    private static string OriginKey(PairingRole role) => $"zaffre.edhoc.origin.{RoleKey(role)}";

    private static string RoleKey(PairingRole role) => role == PairingRole.High ? "high" : "low";

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await PublishState().ConfigureAwait(false);
    }

    public async Task<EdhocSecurity> GetLinkSecurity(PairingRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await PairingStore.GetAsync(PayloadKey(role)).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"No credential stored for the {RoleKey(role)} tier — scan the device's QR on the EDHOC tab.");

            if (!EdhocQrPayload.TryParse(stored, out var payload))
            {
                throw new InvalidOperationException(
                    $"The stored {RoleKey(role)} credential is corrupt — clear it and scan the QR again.");
            }

            return payload!.ToSecurity();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task Pair(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var kid = role == PairingRole.High ? BlueConnectEdhoc.HighKid : BlueConnectEdhoc.LowKid;

        // Reuse the stored key so re-pairing keeps this app's identity;
        // generate one on first use. Enrollment security carries no device
        // CCS — the link announces the enrollment EAD and learns it.
        byte[] privateKey;
        var stored = await PairingStore.GetAsync(PayloadKey(role)).ConfigureAwait(false);
        if (stored is not null && EdhocQrPayload.TryParse(stored, out var existing))
        {
            privateKey = existing!.InitiatorPrivateKey;
        }
        else
        {
            privateKey = EdhocCredential.Generate([kid], subject: null).PrivateKey!.Value.ToArray();
            _logger.Here().LogInformation("Generated the {Role} EDHOC initiator key.", role);
        }

        var security = new EdhocSecurity(privateKey, kid, DeviceCcs: null);

        await using var verdigris = _scanner.CreateVerdigrisPeripheral(advertisement, _options, _logger, security);
        await verdigris.Peripheral.Connect(cancellationToken).ConfigureAwait(false);
        await verdigris.Pair(
            role == PairingRole.High
                ? StonyGrey.BlueConnect.Verdigris.Protocol.Privilege.High
                : StonyGrey.BlueConnect.Verdigris.Protocol.Privilege.Low,
            cancellationToken).ConfigureAwait(false);
        var deviceCcs = verdigris.LearnedDeviceCcs
            ?? throw new InvalidOperationException("The device did not present a credential to pin.");
        await verdigris.Peripheral.Disconnect(cancellationToken).ConfigureAwait(false);

        // The ceremony pinned this key on the device; store the learned pair
        // exactly as if its QR had been scanned.
        var payload = new EdhocQrPayload(
            role == PairingRole.High ? PakeTier.High : PakeTier.Low,
            privateKey, deviceCcs,
            advertisement.LocalName is { Length: > 0 } ? advertisement.LocalName : null);
        await StorePayload(role, payload, CredentialOrigin.Paired, cancellationToken).ConfigureAwait(false);
        _logger.Here().LogInformation("Paired ({Role}) with '{Device}' over EDHOC enrollment.", role, advertisement.LocalName);
    }

    public async Task StorePayload(PairingRole role, EdhocQrPayload payload, CredentialOrigin origin = CredentialOrigin.Provisioned, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Round-trips through Encode/ToSecurity, so anything stored is known
        // to parse and carry a usable key pair.
        var text = payload.Encode();
        _ = payload.ToSecurity();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PairingStore.SetAsync(PayloadKey(role), text).ConfigureAwait(false);
            if (payload.DeviceName is { Length: > 0 })
            {
                await PairingStore.SetAsync(NameKey(role), payload.DeviceName).ConfigureAwait(false);
            }
            else
            {
                PairingStore.Remove(NameKey(role));
            }
            await PairingStore.SetAsync(OriginKey(role), CredentialOriginStorage.Format(origin)).ConfigureAwait(false);

            _logger.Here().LogInformation("Stored the {Role} EDHOC credential.", role);
        }
        finally
        {
            Gate.Release();
        }

        await PublishState().ConfigureAwait(false);
    }

    public async Task Clear(PairingRole role)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            PairingStore.Remove(PayloadKey(role));
            PairingStore.Remove(NameKey(role));
            PairingStore.Remove(OriginKey(role));
        }
        finally
        {
            Gate.Release();
        }

        await PublishState().ConfigureAwait(false);
    }

    private async Task PublishState()
    {
        Publish(new EdhocCredentials(
            await LoadState(PairingRole.Low).ConfigureAwait(false),
            await LoadState(PairingRole.High).ConfigureAwait(false)));
    }

    private static async Task<EdhocCredentialState> LoadState(PairingRole role)
    {
        var payload = await PairingStore.GetAsync(PayloadKey(role)).ConfigureAwait(false);
        var name = await PairingStore.GetAsync(NameKey(role)).ConfigureAwait(false);
        var origin = await PairingStore.GetAsync(OriginKey(role)).ConfigureAwait(false);
        return new EdhocCredentialState(payload is not null, name, CredentialOriginStorage.Parse(origin));
    }
}
