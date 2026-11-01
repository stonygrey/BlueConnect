// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Abstractions;


public enum ScanEventType
{
    Added,
    Updated,
    Expired,
}

public record struct ScanState(ScanEventType Type, IAdvertisement Advertisement);

public interface IScanner : IObservable<ScanState>, IDisposable, IAsyncDisposable
{
    IReadOnlyList<IAdvertisement> Peripherals { get; }

    (int MinDbm, int MaxDbm) RssiLimits { get; }

    bool IsScanning { get; }

    Task Start(CancellationToken cancellationToken = default);

    IPeripheral CreatePeripheral(IAdvertisement advertisement);

    Task<IAdvertisement> ScanForDevice(Predicate<IAdvertisement> predicate, int timeoutSeconds = BlueConnectConstants.ScannerExpirationSeconds, bool skipCache = false, CancellationToken cancellationToken = default);
}
