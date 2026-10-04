// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core.Tests;

/// <summary>
/// <see cref="PeripheralBase.Connect"/> retry window: failed attempts repeat
/// until one succeeds, the window runs out, or the caller cancels.
/// </summary>
public class PeripheralConnectRetryTests
{
    [Fact]
    public async Task RetriesUntilAnAttemptSucceeds()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 30, failures: 2);

        await peripheral.Connect();

        Assert.Equal(3, peripheral.Attempts);
        Assert.True(peripheral.IsConnected);
    }

    [Fact]
    public async Task ReportsTheLastFailureOnceTheWindowIsSpent()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 2, failures: int.MaxValue);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => peripheral.Connect());

        Assert.IsType<BleTransportException>(ex.InnerException);
        Assert.Contains("did not succeed within 2s", ex.Message);
        Assert.InRange(peripheral.Attempts, 2, 3);
        Assert.False(peripheral.IsConnected);
    }

    [Fact]
    public async Task ZeroWindowRetriesUntilCancelled()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 0, failures: int.MaxValue);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peripheral.Connect(cts.Token));

        Assert.InRange(peripheral.Attempts, 2, 4);
    }

    [Fact]
    public async Task AnAttemptNeverOutlivesTheWindow()
    {
        // A hanging attempt (60 s connect timeout) inside a 2 s window is cut at the window.
        var peripheral = new FakePeripheral(retryWindowSeconds: 2, failures: 0, hang: true, connectTimeoutSeconds: 60);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => peripheral.Connect());

        Assert.Contains("timed out after 2s", ex.InnerException?.Message);
        Assert.Equal(1, peripheral.Attempts);
    }

    [Fact]
    public async Task CancellationStopsRetriesImmediately()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 30, failures: int.MaxValue);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peripheral.Connect(cts.Token));
    }

    [Fact]
    public async Task AdvertisingGateKeepsRetryingWhileTheDeviceIsSeen()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 30, failures: 2, retryOnlyWhileAdvertising: true);
        peripheral.Seen(DateTimeOffset.Now);

        await peripheral.Connect();

        Assert.Equal(3, peripheral.Attempts);
    }

    [Fact]
    public async Task AdvertisingGateGivesUpWhenTheDeviceHasGoneQuiet()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 30, failures: int.MaxValue, retryOnlyWhileAdvertising: true);
        peripheral.Seen(DateTimeOffset.Now - TimeSpan.FromSeconds(BlueConnectConstants.ScannerExpirationSeconds + 1));

        var ex = await Assert.ThrowsAsync<BleTransportException>(() => peripheral.Connect());

        Assert.Contains("no longer advertising", ex.Message);
        Assert.Contains("scripted connect failure 1", ex.InnerException?.Message);
        Assert.Equal(1, peripheral.Attempts);
    }

    [Fact]
    public async Task AdvertisingGateGivesUpWhenTheDeviceWasNeverCached()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 30, failures: int.MaxValue, retryOnlyWhileAdvertising: true);

        await Assert.ThrowsAsync<BleTransportException>(() => peripheral.Connect());

        Assert.Equal(1, peripheral.Attempts);
    }

    [Fact]
    public async Task WithoutTheGateAQuietDeviceIsStillRetried()
    {
        var peripheral = new FakePeripheral(retryWindowSeconds: 30, failures: 2, retryOnlyWhileAdvertising: false);

        await peripheral.Connect();

        Assert.Equal(3, peripheral.Attempts);
    }

    private sealed class FakeScanner(IOptions<BlueConnectOptions> options)
        : ScannerBase(options, NullLogger.Instance)
    {
        public BlueConnectOptions OptionsForTests => Options;

        public void Feed(IAdvertisement advertisement) => Upsert(advertisement);

        public override (int MinDbm, int MaxDbm) RssiLimits => (-127, 20);
        public override bool IsScanning => false;
        public override Task Start(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IPeripheral CreatePeripheral(IAdvertisement advertisement) => throw new NotSupportedException();
    }

    private sealed class FakePeripheral : PeripheralBase
    {
        private readonly bool _hang;
        private int _failuresLeft;

        private readonly FakeScanner _scanner;

        public FakePeripheral(int retryWindowSeconds, int failures, bool hang = false, int connectTimeoutSeconds = 5, bool retryOnlyWhileAdvertising = false)
            : this(new FakeScanner(Microsoft.Extensions.Options.Options.Create(new BlueConnectOptions
            {
                ConnectRetryTimeoutSeconds = retryWindowSeconds,
                ConnectTimeoutSeconds = connectTimeoutSeconds,
                RetryOnlyWhileAdvertising = retryOnlyWhileAdvertising,
            })), failures, hang)
        {
        }

        private FakePeripheral(FakeScanner scanner, int failures, bool hang)
            : base(scanner, new Ad(DateTimeOffset.Now), scanner.OptionsForTests)
        {
            _scanner = scanner;
            _failuresLeft = failures;
            _hang = hang;
        }

        /// <summary>Puts an advertisement for this device in the scanner cache, stamped at <paramref name="at"/>.</summary>
        public void Seen(DateTimeOffset at) => _scanner.Feed(new Ad(at));

        public int Attempts { get; private set; }

        public override int MTU => 20;

        protected override async Task ConnectCore(CancellationToken cancellationToken)
        {
            Attempts++;
            if (_hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new BleTransportException($"scripted connect failure {Attempts}");
            }
        }

        protected override Task DisconnectCore(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task WriteChunk(byte[] chunk, CancellationToken cancellationToken) => Task.CompletedTask;
        protected override void AbortConnect()
        {
        }

        private sealed class Ad(DateTimeOffset timestamp) : IAdvertisement
        {
            public string Id => "00:11:22:33:44:55";
            public string LocalName => "fake";
            public bool IsConnectable => true;
            public bool IsSecured => false;
            public AuthenticationScheme AuthenticationScheme => AuthenticationScheme.Noise;
            public Rssi Rssi => new(-50, -50, -50);
            public DateTimeOffset Timestamp => timestamp;
            public bool Equals(IAdvertisement? other) => other?.Id == Id;
        }
    }
}
