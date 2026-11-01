// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.Shared.Services;

using Zaffre.Services.Abstractions;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Verdigris;

namespace Zaffre.Services;

/// <summary>
/// Runs the connection test loop: connect, issue a Verdigris command, disconnect,
/// wait a random interval, repeat. Publishes cumulative results as a stream
/// of <see cref="ConnectionTestState"/>.
/// </summary>
public class DiagnosticsService : ObservableService<ConnectionTestState>, IDiagnosticsService
{
    private readonly IScanner _scanner;
    private readonly BlueConnectOptions _options;
    private readonly object _mutateLock = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IDisposable? _ticker;
    private long _totalConnectTicks;
    private int _connectSamples;

    private readonly ILogger<DiagnosticsService> _logger;
    private readonly INoiseCredentialService _pairing;

    public DiagnosticsService(IScanner scanner, INoiseCredentialService pairing, IOptions<BlueConnectOptions> options, ILogger<DiagnosticsService> logger)
        : base(new ConnectionTestState())
    {
        _scanner = scanner;
        _pairing = pairing;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartConnectTest(IAdvertisement ad, CancellationToken cancellationToken = default)
    {
        await StopConnectTest(cancellationToken).ConfigureAwait(false);

        _totalConnectTicks = 0;
        _connectSamples = 0;
        _cts = new CancellationTokenSource();
        Publish(new ConnectionTestState(
            DeviceName: ad.LocalName,
            StartTime: DateTime.Now,
            IsRunning: true));

        // Tick ElapsedTime once a second so the view updates between cycles.
        _ticker = Observable
            .Interval(TimeSpan.FromSeconds(1))
            .Subscribe(_ => Mutate(s => s with { ElapsedTime = DateTime.Now - s.StartTime }));

        _loop = Task.Run(() => RunLoop(ad, _cts.Token), CancellationToken.None);
    }

    public async Task StopConnectTest(CancellationToken cancellationToken = default)
    {
        var loop = _loop;
        if (_cts is { } cts)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (loop is not null)
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _ticker?.Dispose();
        _ticker = null;
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    /// <summary>Awaits the test loop, so "disposed" means "no cycle still touching the radio".</summary>
    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await StopConnectTest().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Disposal must not throw.
            _logger.Here().LogWarning(ex, "Stopping the test loop during disposal failed.");
        }

        ReleaseResources();
    }

    /// <summary>The sync path cancels without awaiting the loop. Prefer <see cref="DisposeAsync"/>.</summary>
    protected override void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
        {
            return;
        }

        _cts?.Cancel();
        ReleaseResources();
    }

    protected override void DisposeCore()
    {
        _ticker?.Dispose();
        _cts?.Dispose();
    }

    private async Task RunLoop(IAdvertisement ad, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunCycle(ad, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(NextCycleDelay(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Mutate(s => s with { IsRunning = false, ElapsedTime = DateTime.Now - s.StartTime });
    }

    private async Task RunCycle(IAdvertisement ad, CancellationToken ct)
    {
        IVerdigrisPeripheral? verdigris = null;
        try
        {
            // The loop's cycle command is Echo — high privilege.
            var security = await _pairing.GetLinkSecurity(PairingRole.High, ct).ConfigureAwait(false);
            verdigris = _scanner.CreateVerdigrisPeripheral(ad, _options, _logger, security);

            var stopwatch = Stopwatch.StartNew();
            await verdigris.Peripheral.Connect(ct).ConfigureAwait(false);
            RecordConnectTime(stopwatch.Elapsed);

            try
            {
                await IssueCommand(verdigris, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Here().LogWarning(ex, "Command failed against '{Device}'.", ad.LocalName);
                Mutate(s => s with { CommandFailures = s.CommandFailures + 1 });
            }

            await verdigris.Peripheral.Disconnect(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Here().LogWarning(ex, "Connection cycle failed against '{Device}'.", ad.LocalName);
            Mutate(s => s with { ConnectionFailures = s.ConnectionFailures + 1 });
        }
        finally
        {
            if (verdigris is not null)
            {
                // Owns and disposes the underlying peripheral.
                await verdigris.DisposeAsync().ConfigureAwait(false);
            }

            Mutate(s => s with { TestCount = s.TestCount + 1, ElapsedTime = DateTime.Now - s.StartTime });
        }
    }

    private async Task IssueCommand(IVerdigrisPeripheral verdigris, CancellationToken ct)
    {
        // Timeout is enforced inside the Verdigris layer (CommandTimeoutSeconds).
        var payload = new byte[_options.IsEchoVerificationEnabled ? 1536 : 16];
        Random.Shared.NextBytes(payload);

        var echoed = await verdigris.Echo(payload, ct).ConfigureAwait(false);

        if (!echoed.AsSpan().SequenceEqual(payload))
        {
            throw new InvalidOperationException("Echoed payload does not match the command.");
        }
    }

    private void RecordConnectTime(TimeSpan connectTime)
    {
        _totalConnectTicks += connectTime.Ticks;
        _connectSamples++;
        var average = TimeSpan.FromTicks(_totalConnectTicks / _connectSamples);

        Mutate(s => s with
        {
            MinConnectTime = _connectSamples == 1 ? connectTime : TimeSpan.FromTicks(Math.Min(s.MinConnectTime.Ticks, connectTime.Ticks)),
            MaxConnectTime = TimeSpan.FromTicks(Math.Max(s.MaxConnectTime.Ticks, connectTime.Ticks)),
            AverageConnectTime = average,
        });
    }

    private TimeSpan NextCycleDelay()
        => TimeSpan.FromSeconds(Random.Shared.Next(_options.MinCycleDelaySeconds, _options.MaxCycleDelaySeconds + 1));

    private void Mutate(Func<ConnectionTestState, ConnectionTestState> update)
    {
        lock (_mutateLock)
        {
            Publish(update(State));
        }
    }
}
