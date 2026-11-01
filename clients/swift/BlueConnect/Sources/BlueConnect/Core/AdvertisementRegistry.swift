// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// The scan cache that every backend shares: the last advertisement per
/// device, the subscribers' event streams, the set of devices with a
/// connection in progress (which never expire), and the 5 s expiry sweep.
/// A class guarded by a lock, not an actor, because backends read
/// `peripherals` synchronously from a platform callback and the app reads it
/// from the main thread.
public final class AdvertisementRegistry: @unchecked Sendable {
    private let lock = NSLock()
    private var cache: [String: Advertisement] = [:]
    private var connections: Set<String> = []
    private var subscribers: [UUID: AsyncStream<ScanEvent>.Continuation] = [:]
    private var expiry: Task<Void, Never>?
    private let rssiLimits: (minDbm: Int, maxDbm: Int)

    /// `rssiLimits` are exclusive bounds; samples outside them are ignored.
    public init(rssiLimits: (minDbm: Int, maxDbm: Int)) {
        self.rssiLimits = rssiLimits
        expiry = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(5))
                self?.expireStaleEntries()
            }
        }
    }

    deinit {
        expiry?.cancel()
    }

    public var peripherals: [Advertisement] {
        lock.lock()
        defer { lock.unlock() }
        return Array(cache.values)
    }

    public func events() -> AsyncStream<ScanEvent> {
        let id = UUID()
        let (stream, continuation) = AsyncStream<ScanEvent>.makeStream()
        continuation.onTermination = { [weak self] _ in
            self?.removeSubscriber(id)
        }
        lock.lock()
        subscribers[id] = continuation
        lock.unlock()
        return stream
    }

    public func cached(_ id: String) -> Advertisement? {
        lock.lock()
        defer { lock.unlock() }
        return cache[id]
    }

    public func lastSeen(_ id: String) -> Date? {
        cached(id)?.timestamp
    }

    /// Merges one packet. `secured` is nil for packets without the Stony Grey
    /// manufacturer data, which keep the previous value.
    public func observe(id: String, localName: String, isConnectable: Bool, secured: Bool?, dbm: Int) {
        let now = Date()
        lock.lock()
        let previous = cache[id]
        let advertisement: Advertisement
        if let previous {
            advertisement = Advertisement(
                id: id, localName: localName, isConnectable: isConnectable,
                isSecured: secured ?? previous.isSecured,
                rssi: mergeRssi(previous.rssi, dbm), timestamp: now)
        } else {
            advertisement = Advertisement(
                id: id, localName: localName, isConnectable: isConnectable,
                isSecured: secured ?? false,
                rssi: Rssi(dbm), timestamp: now)
        }
        cache[id] = advertisement
        let subscribers = Array(self.subscribers.values)
        lock.unlock()
        let event = ScanEvent(type: previous == nil ? .added : .updated, advertisement: advertisement)
        for subscriber in subscribers {
            subscriber.yield(event)
        }
    }

    /// Marks a device as connecting or connected so that it is not expired
    /// while it has stopped advertising.
    public func register(_ id: String) {
        lock.lock()
        connections.insert(id)
        lock.unlock()
    }

    public func unregister(_ id: String) {
        lock.lock()
        connections.remove(id)
        lock.unlock()
    }

    /// Ends every subscriber's stream.
    public func finish() {
        expiry?.cancel()
        lock.lock()
        let subscribers = Array(self.subscribers.values)
        self.subscribers.removeAll()
        lock.unlock()
        for subscriber in subscribers {
            subscriber.finish()
        }
    }

    private func mergeRssi(_ previous: Rssi, _ dbm: Int) -> Rssi {
        dbm > rssiLimits.minDbm && dbm < rssiLimits.maxDbm
            ? Rssi(valueDbm: dbm, minDbm: min(previous.minDbm, dbm), maxDbm: max(previous.maxDbm, dbm))
            : previous
    }

    private func removeSubscriber(_ id: UUID) {
        lock.lock()
        subscribers[id] = nil
        lock.unlock()
    }

    private func expireStaleEntries() {
        let cutoff = Date().addingTimeInterval(-BlueConnectConstants.scannerExpiration.seconds)
        lock.lock()
        let expired = cache.values.filter { $0.timestamp < cutoff && !connections.contains($0.id) }
        for advertisement in expired {
            cache[advertisement.id] = nil
        }
        let subscribers = Array(self.subscribers.values)
        lock.unlock()
        for advertisement in expired {
            let event = ScanEvent(type: .expired, advertisement: advertisement)
            for subscriber in subscribers {
                subscriber.yield(event)
            }
        }
    }
}

extension Duration {
    /// The duration in seconds, for `Date` arithmetic.
    public var seconds: TimeInterval {
        let (seconds, attoseconds) = components
        return TimeInterval(seconds) + TimeInterval(attoseconds) / 1e18
    }
}
