// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

/// <summary>
/// Cumulative test results
/// </summary>
public record struct ConnectionTestState(
    string DeviceName = "",
    TimeSpan MinConnectTime = default,
    TimeSpan MaxConnectTime = default,
    TimeSpan AverageConnectTime = default,
    TimeSpan ElapsedTime = default,
    DateTime StartTime = default,
    int TestCount = 0,
    int ConnectionFailures = 0,
    int CommandFailures = 0,
    bool IsRunning = false);

public interface IDiagnosticsService : IObservableService<ConnectionTestState>
{
    Task StartConnectTest(IAdvertisement ad, CancellationToken cancellationToken = default);
    Task StopConnectTest(CancellationToken cancellationToken = default);
}
