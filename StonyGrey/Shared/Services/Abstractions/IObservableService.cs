// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Shared.Services.Abstractions;

/// <summary>
/// A service whose state is a snapshot plus a stream: <see cref="State"/> is
/// the current value, and subscribing immediately yields it before any
/// updates (BehaviorSubject semantics) — subscribers never need a defensive
/// StartWith. Event-stream services (toasts, scan sightings) are not this;
/// they have no meaningful "current value".
/// </summary>
public interface IObservableService<out TState> : IService, IObservable<TState>
{
    TState State { get; }
}
