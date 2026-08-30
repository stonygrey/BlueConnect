// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Secure;
using StonyGrey.Spake2Plus;

using StonyGrey.Shared.Services;

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

public sealed class PakeCredentialService : ObservableService<PakeCredentials>, IPakeCredentialService
{
    private readonly ILogger<PakeCredentialService> _logger;
    private readonly Dictionary<PairingRole, PakeSecurity> _derived = [];

    public PakeCredentialService(ILogger<PakeCredentialService> logger)
        : base(new PakeCredentials(new PakeCredentialState(false, null), new PakeCredentialState(false, null)))
    {
        _logger = logger;
    }

    private static string CodeKey(PairingRole role) => $"zaffre.pake.code.{RoleKey(role)}";

    private static string NameKey(PairingRole role) => $"zaffre.pake.device.name.{RoleKey(role)}";

    private static string RoleKey(PairingRole role) => role == PairingRole.High ? "high" : "low";

    private static PakeTier TierFor(PairingRole role) => role == PairingRole.High ? PakeTier.High : PakeTier.Low;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await PublishState().ConfigureAwait(false);
    }

    public async Task<PakeSecurity> GetLinkSecurity(PairingRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_derived.TryGetValue(role, out var cached))
            {
                return cached;
            }

            var code = await PairingStore.GetAsync(CodeKey(role)).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"No setup code stored for the {RoleKey(role)} tier — scan the device's QR on the PAKE tab.");

            var security = await DeriveAsync(role, code, cancellationToken).ConfigureAwait(false);
            _derived[role] = security;
            return security;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task StoreCode(PairingRole role, string code, string? deviceName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Derive first: it validates the code and pays the PBKDF2 cost once,
        // so a bad code never reaches storage.
        var security = await DeriveAsync(role, code, cancellationToken).ConfigureAwait(false);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PairingStore.SetAsync(CodeKey(role), SetupCode.Normalize(code)).ConfigureAwait(false);
            if (deviceName is { Length: > 0 })
            {
                await PairingStore.SetAsync(NameKey(role), deviceName).ConfigureAwait(false);
            }
            else
            {
                PairingStore.Remove(NameKey(role));
            }

            _derived[role] = security;
            _logger.Here().LogInformation("Stored the {Role} PAKE setup code.", role);
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
            PairingStore.Remove(CodeKey(role));
            PairingStore.Remove(NameKey(role));
            _derived.Remove(role);
        }
        finally
        {
            Gate.Release();
        }

        await PublishState().ConfigureAwait(false);
    }

    private static Task<PakeSecurity> DeriveAsync(PairingRole role, string code, CancellationToken cancellationToken)
        // 100k PBKDF2 iterations: off the caller's (UI) thread.
        => Task.Run(() => PakeSecurity.FromCode(code, TierFor(role)), cancellationToken);

    private async Task PublishState()
    {
        Publish(new PakeCredentials(
            await LoadState(PairingRole.Low).ConfigureAwait(false),
            await LoadState(PairingRole.High).ConfigureAwait(false)));
    }

    private static async Task<PakeCredentialState> LoadState(PairingRole role)
    {
        var code = await PairingStore.GetAsync(CodeKey(role)).ConfigureAwait(false);
        var name = await PairingStore.GetAsync(NameKey(role)).ConfigureAwait(false);
        return new PakeCredentialState(code is not null, name);
    }
}
