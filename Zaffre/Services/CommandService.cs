// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Verdigris;

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

public sealed class CommandService : ICommandService
{
    private readonly IScanner _scanner;
    private readonly INoiseCredentialService _pairing;
    private readonly IPakeCredentialService _pakeCredentials;
    private readonly IEdhocCredentialService _edhocCredentials;
    private readonly BlueConnectOptions _options;
    private readonly ILogger<CommandService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public CommandService(IScanner scanner, INoiseCredentialService pairing, IPakeCredentialService pakeCredentials, IEdhocCredentialService edhocCredentials, IOptions<BlueConnectOptions> options, ILogger<CommandService> logger)
    {
        _scanner = scanner;
        _pairing = pairing;
        _pakeCredentials = pakeCredentials;
        _edhocCredentials = edhocCredentials;
        _options = options.Value;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<byte[]> Echo(IAdvertisement advertisement, PairingRole role, byte[] payload, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.Echo(payload, cancellationToken), cancellationToken);

    public Task<double> SquareRoot(IAdvertisement advertisement, PairingRole role, double value, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.SquareRoot(value, cancellationToken), cancellationToken);

    public Task<ulong> Fibonacci(IAdvertisement advertisement, PairingRole role, uint n, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.Fibonacci(n, cancellationToken), cancellationToken);

    public Task<DateTimeOffset> GetTime(IAdvertisement advertisement, PairingRole role, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.GetTime(cancellationToken), cancellationToken);

    public Task SetTime(IAdvertisement advertisement, PairingRole role, DateTimeOffset time, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, async v =>
        {
            await v.SetTime(time, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    public Task<bool> GetLed(IAdvertisement advertisement, PairingRole role, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.GetLed(cancellationToken), cancellationToken);

    public Task<bool> SetLed(IAdvertisement advertisement, PairingRole role, bool on, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.SetLed(on, cancellationToken), cancellationToken);

    public Task<string> GetSerialNumber(IAdvertisement advertisement, PairingRole role, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.GetSerialNumber(cancellationToken), cancellationToken);

    public Task<string> SetSerialNumber(IAdvertisement advertisement, PairingRole role, ushort serialNumber, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default)
        => Invoke(advertisement, role, kind, v => v.SetSerialNumber(serialNumber, cancellationToken), cancellationToken);

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task<T> Invoke<T>(IAdvertisement advertisement, PairingRole role, LinkKind kind, Func<IVerdigrisPeripheral, Task<T>> action, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // No pre-flight state checks: the device answers immediately and
            // authoritatively (UNAUTHENTICATED / PERMISSION_DENIED), so the
            // error the user sees is the device's own verdict.
            StonyGrey.BlueConnect.Secure.ILinkSecurity security = kind switch
            {
                LinkKind.Pake => await _pakeCredentials.GetLinkSecurity(role, cancellationToken).ConfigureAwait(false),
                LinkKind.Edhoc => await _edhocCredentials.GetLinkSecurity(role, cancellationToken).ConfigureAwait(false),
                _ => await _pairing.GetLinkSecurity(role, cancellationToken).ConfigureAwait(false),
            };
            await using var verdigris = _scanner.CreateVerdigrisPeripheral(advertisement, _options, _logger, security);
            await verdigris.Peripheral.Connect(cancellationToken).ConfigureAwait(false);
            var result = await action(verdigris).ConfigureAwait(false);
            await verdigris.Peripheral.Disconnect(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
