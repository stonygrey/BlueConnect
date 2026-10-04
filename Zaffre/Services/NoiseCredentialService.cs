// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;
using StonyGrey.BlueConnect.Verdigris;

using StonyGrey.Shared.Services;

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

public sealed class NoiseCredentialService : ObservableService<NoiseCredentials>, INoiseCredentialService
{
    private readonly IScanner _scanner;
    private readonly BlueConnectOptions _options;
    private readonly ILogger<NoiseCredentialService> _logger;
    private readonly Dictionary<PairingRole, LinkIdentity> _identities = [];

    public NoiseCredentialService(IScanner scanner, IOptions<BlueConnectOptions> options, ILogger<NoiseCredentialService> logger)
        : base(new NoiseCredentials(new NoiseCredentialState(false, null), new NoiseCredentialState(false, null)))
    {
        _scanner = scanner;
        _options = options.Value;
        _logger = logger;
    }

    private static string IdentityKey(PairingRole role) => $"zaffre.link.identity.{RoleKey(role)}";

    private static string CertKey(PairingRole role) => $"zaffre.link.cert.{RoleKey(role)}";

    private static string PinKey(PairingRole role) => $"zaffre.pair.device.key.{RoleKey(role)}";

    private static string PinNameKey(PairingRole role) => $"zaffre.pair.device.name.{RoleKey(role)}";

    private static string OriginKey(PairingRole role) => $"zaffre.pair.origin.{RoleKey(role)}";

    private static string RoleKey(PairingRole role) => role == PairingRole.High ? "high" : "low";

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var role in new[] { PairingRole.Low, PairingRole.High })
        {
            var identity = await GetIdentity(role).ConfigureAwait(false);
            ExportPublicKeyFile(role, identity);
            await TryImportCertFile(role, identity).ConfigureAwait(false);
            var cert = await LoadCert(role).ConfigureAwait(false);
            _logger.Here().LogInformation(
                "ZAFFRE-NOISE-PUB: {Role} {Key} cert={Cert}.",
                role, Convert.ToHexString(identity.StaticPublicKey),
                cert is null ? "none" : $"{cert.Length}B");
        }

        await PublishState().ConfigureAwait(false);
    }

    public async Task<LinkSecurity> GetLinkSecurity(PairingRole role, CancellationToken cancellationToken = default)
    {
        var pin = await PairingStore.GetAsync(PinKey(role)).ConfigureAwait(false);
        return new LinkSecurity(
            await GetIdentity(role).ConfigureAwait(false),
            pin is null ? null : Convert.FromBase64String(pin),
            await LoadCert(role).ConfigureAwait(false));
    }

    public async Task<byte[]> GetStaticPublicKey(PairingRole role, CancellationToken cancellationToken = default)
    {
        var identity = await GetIdentity(role).ConfigureAwait(false);
        return identity.StaticPublicKey;
    }

    public async Task InstallEndorsement(PairingRole role, byte[] blob)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(blob);

        var identity = await GetIdentity(role).ConfigureAwait(false);
        if (!ClientEndorsement.IsCertBlob(blob) ||
            !blob.AsSpan(1, ClientEndorsement.SubjectLength).SequenceEqual(identity.StaticPublicKey))
        {
            throw new ArgumentException("Endorsement is not a v1 blob for this role's Noise static.");
        }

        await PairingStore.SetAsync(CertKey(role), Convert.ToBase64String(blob)).ConfigureAwait(false);
        _logger.Here().LogInformation("Installed endorsement for {Role} ({Len} B).", role, blob.Length);
    }

    public async Task Pair(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Accept-any security: trust here is established by the button
            // press on the device, not by a pre-known key.
            var security = new LinkSecurity(
                await GetIdentity(role).ConfigureAwait(false),
                PinnedPeerKey: null,
                await LoadCert(role).ConfigureAwait(false));

            await using var verdigris = _scanner.CreateVerdigrisPeripheral(advertisement, _options, _logger, security);
            await verdigris.Peripheral.Connect(cancellationToken).ConfigureAwait(false);
            var deviceKey = await verdigris.Pair(
                role == PairingRole.High
                    ? StonyGrey.BlueConnect.Verdigris.Protocol.Privilege.High
                    : StonyGrey.BlueConnect.Verdigris.Protocol.Privilege.Low,
                cancellationToken).ConfigureAwait(false);
            await verdigris.Peripheral.Disconnect(cancellationToken).ConfigureAwait(false);

            await PairingStore.SetAsync(PinKey(role), Convert.ToBase64String(deviceKey)).ConfigureAwait(false);
            await PairingStore.SetAsync(PinNameKey(role), advertisement.LocalName).ConfigureAwait(false);
            await PairingStore.SetAsync(OriginKey(role), CredentialOriginStorage.Format(CredentialOrigin.Paired)).ConfigureAwait(false);
            await PublishState().ConfigureAwait(false);
            _logger.Here().LogInformation("Paired ({Role}) with '{Device}' ({Key}).", role, advertisement.LocalName, Convert.ToHexString(deviceKey));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task Unpair(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var security = await GetLinkSecurity(role, cancellationToken).ConfigureAwait(false);
                if (security.PinnedPeerKey is null)
                {
                    throw new InvalidOperationException("Not paired.");
                }

                await using var verdigris = _scanner.CreateVerdigrisPeripheral(advertisement, _options, _logger, security);
                await verdigris.Peripheral.Connect(cancellationToken).ConfigureAwait(false);
                await verdigris.Unpair(cancellationToken).ConfigureAwait(false);
                await verdigris.Peripheral.Disconnect(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
            {
                // Still forget locally; the device can be cleared with its
                // button (hold Button 3 for 5 s) or lib/device clear.
                _logger.Here().LogWarning(ex, "Device-side unpair ({Role}) failed; clearing the local pairing anyway.", role);
            }

            // A provisioned credential is the identity itself: the QR carried
            // the private key, and the device's slot is locked against remote
            // unpair (and correctly refused above). Forgetting only the pin
            // would leave this phone able to open XX sessions that the device
            // still resolves to the slot. Discard the identity (and any cert
            // bound to it) so the phone genuinely loses the tier; scanning the
            // QR again restores it, because the label is the credential.
            var provisioned = CredentialOriginStorage.Parse(await PairingStore.GetAsync(OriginKey(role)).ConfigureAwait(false))
                              == CredentialOrigin.Provisioned;

            PairingStore.Remove(PinKey(role));
            PairingStore.Remove(PinNameKey(role));
            PairingStore.Remove(OriginKey(role));
            if (provisioned)
            {
                PairingStore.Remove(IdentityKey(role));
                PairingStore.Remove(CertKey(role));
                _identities.Remove(role);
                _logger.Here().LogInformation(
                    "Discarded the provisioned {Role} identity; the device keeps its locked slot until cleared over USB.", role);
            }
            await PublishState().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task Enroll(IAdvertisement advertisement, NoiseEnrollmentPayload payload, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            const PairingRole role = PairingRole.Low;
            var identity = await GetIdentity(role).ConfigureAwait(false);

            // The enrollment key is the session's identity for this one call;
            // the device's key from the card is the pin, so the session runs
            // IK and the device is authenticated before Enroll is sent.
            var enrollment = new LinkSecurity(new LinkIdentity(payload.EnrollmentPrivateKey), PinnedPeerKey: payload.DevicePublicKey);

            await using var verdigris = _scanner.CreateVerdigrisPeripheral(advertisement, _options, _logger, enrollment);
            await verdigris.Peripheral.Connect(cancellationToken).ConfigureAwait(false);
            await verdigris.Enroll(identity.StaticPublicKey, cancellationToken).ConfigureAwait(false);
            await verdigris.Peripheral.Disconnect(cancellationToken).ConfigureAwait(false);

            await PairingStore.SetAsync(PinKey(role), Convert.ToBase64String(payload.DevicePublicKey)).ConfigureAwait(false);
            await PairingStore.SetAsync(PinNameKey(role), payload.DeviceName is { Length: > 0 } ? payload.DeviceName : advertisement.LocalName).ConfigureAwait(false);
            await PairingStore.SetAsync(OriginKey(role), CredentialOriginStorage.Format(CredentialOrigin.Enrolled)).ConfigureAwait(false);
            await PublishState().ConfigureAwait(false);
            _logger.Here().LogInformation(
                "Enrolled (Low) with '{Device}' ({Key}).", advertisement.LocalName, Convert.ToHexString(identity.StaticPublicKey));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task StorePayload(PairingRole role, NoiseQrPayload payload)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var identity = new LinkIdentity(payload.AppPrivateKey);
            await PairingStore.SetAsync(IdentityKey(role), Convert.ToBase64String(identity.StaticPrivateKey)).ConfigureAwait(false);
            _identities[role] = identity;

            await PairingStore.SetAsync(PinKey(role), Convert.ToBase64String(payload.DevicePublicKey)).ConfigureAwait(false);
            if (payload.DeviceName is { Length: > 0 })
            {
                await PairingStore.SetAsync(PinNameKey(role), payload.DeviceName).ConfigureAwait(false);
            }
            else
            {
                PairingStore.Remove(PinNameKey(role));
            }
            await PairingStore.SetAsync(OriginKey(role), CredentialOriginStorage.Format(CredentialOrigin.Provisioned)).ConfigureAwait(false);

            await PublishState().ConfigureAwait(false);
            _logger.Here().LogInformation(
                "Installed a provisioned {Role} credential (app key {Key}).",
                role, Convert.ToHexString(identity.StaticPublicKey));
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task PublishState()
    {
        Publish(new NoiseCredentials(
            await LoadState(PairingRole.Low).ConfigureAwait(false),
            await LoadState(PairingRole.High).ConfigureAwait(false)));
    }

    private static async Task<NoiseCredentialState> LoadState(PairingRole role)
    {
        var pin = await PairingStore.GetAsync(PinKey(role)).ConfigureAwait(false);
        var name = await PairingStore.GetAsync(PinNameKey(role)).ConfigureAwait(false);
        var origin = await PairingStore.GetAsync(OriginKey(role)).ConfigureAwait(false);
        return new NoiseCredentialState(pin is not null, name, CredentialOriginStorage.Parse(origin));
    }

    private static async Task<byte[]?> LoadCert(PairingRole role)
    {
        var stored = await PairingStore.GetAsync(CertKey(role)).ConfigureAwait(false);
        if (stored is null)
        {
            return null;
        }

        try
        {
            var blob = Convert.FromBase64String(stored);
            return ClientEndorsement.IsCertBlob(blob) ? blob : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void ExportPublicKeyFile(PairingRole role, LinkIdentity identity)
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, $"noise-{RoleKey(role)}-pub.hex");
            File.WriteAllText(path, Convert.ToHexString(identity.StaticPublicKey) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.Here().LogDebug(ex, "Could not export {Role} Noise public key file.", role);
        }
    }

    private async Task TryImportCertFile(PairingRole role, LinkIdentity identity)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, $"noise-{RoleKey(role)}.cert");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var blob = ReadCertFile(path);
            if (blob is null ||
                !ClientEndorsement.IsCertBlob(blob) ||
                !blob.AsSpan(1, ClientEndorsement.SubjectLength).SequenceEqual(identity.StaticPublicKey))
            {
                _logger.Here().LogWarning("Ignoring endorsement file {Path}: not a v1 blob for this identity.", path);
                return;
            }

            await PairingStore.SetAsync(CertKey(role), Convert.ToBase64String(blob)).ConfigureAwait(false);
            _logger.Here().LogInformation("Imported endorsement for {Role} from {Path}.", role, path);
        }
        catch (Exception ex)
        {
            _logger.Here().LogWarning(ex, "Failed to import endorsement from {Path}.", path);
        }
    }

    private static byte[]? ReadCertFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == ClientEndorsement.BlobLength)
        {
            return bytes;
        }

        var text = File.ReadAllText(path).Trim();
        if (text.Length == ClientEndorsement.BlobLength * 2)
        {
            try
            {
                return Convert.FromHexString(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private async Task<LinkIdentity> GetIdentity(PairingRole role)
    {
        if (_identities.TryGetValue(role, out var cached))
        {
            return cached;
        }

        var stored = await PairingStore.GetAsync(IdentityKey(role)).ConfigureAwait(false);
        if (stored is not null)
        {
            var restored = new LinkIdentity(Convert.FromBase64String(stored));
            _identities[role] = restored;
            return restored;
        }

        var identity = LinkIdentity.Generate();
        await PairingStore.SetAsync(IdentityKey(role), Convert.ToBase64String(identity.StaticPrivateKey)).ConfigureAwait(false);
        _identities[role] = identity;
        _logger.Here().LogInformation("Generated {Role} app link identity {Key}.", role, Convert.ToHexString(identity.StaticPublicKey));
        return identity;
    }
}
