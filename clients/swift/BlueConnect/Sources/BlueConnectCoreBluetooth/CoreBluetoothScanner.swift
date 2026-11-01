// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import CoreBluetooth
import Foundation

/// The iOS and Mac Catalyst scanner: one `CBCentralManager` on a private
/// queue, filtered on the Verdigris service UUID with duplicates allowed so
/// that RSSI and the status byte keep updating.
public final class CoreBluetoothScanner: NSObject, BleScanner, CBCentralManagerDelegate, @unchecked Sendable {
    let options: BlueConnectOptions
    let registry = AdvertisementRegistry(rssiLimits: (minDbm: -127, maxDbm: 0))
    private let queue = DispatchQueue(label: "com.stonygrey.blueconnect.ble")
    private let serviceUuid: CBUUID
    private var central: CBCentralManager!
    private let lock = NSLock()
    private var nativePeripherals: [String: CBPeripheral] = [:]
    private var connections: [String: CoreBluetoothPeripheral] = [:]
    private var startWaiters: [CheckedContinuation<Void, any Error>] = []
    private var startRequested = false

    public init(options: BlueConnectOptions = BlueConnectOptions()) {
        self.options = options
        serviceUuid = CBUUID(string: options.serviceUuid)
        super.init()
        central = CBCentralManager(delegate: self, queue: queue)
    }

    public var peripherals: [Advertisement] { registry.peripherals }

    public var isScanning: Bool { central.isScanning }

    public func start() async throws {
        if central.isScanning {
            return
        }
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            startRequested = true
            startWaiters.append(continuation)
            lock.unlock()
            tryStartScan()
        }
    }

    public func events() -> AsyncStream<ScanEvent> { registry.events() }

    public func lastSeen(_ id: String) -> Date? { registry.lastSeen(id) }

    public func createPeripheral(_ advertisement: Advertisement) throws -> any BlePeripheral {
        lock.lock()
        let native = nativePeripherals[advertisement.id]
        lock.unlock()
        guard let cached = registry.cached(advertisement.id), let native else {
            throw BlueConnectError.invalidOperation(
                "'\(advertisement.localName)' (\(advertisement.id)) is no longer in the scan cache.")
        }
        return CoreBluetoothPeripheral(scanner: self, central: central, cb: native, advertisement: cached, options: options)
    }

    // MARK: Connection routing

    func register(_ peripheral: CoreBluetoothPeripheral) {
        lock.lock()
        connections[peripheral.advertisement.id] = peripheral
        lock.unlock()
        registry.register(peripheral.advertisement.id)
    }

    func unregister(_ peripheral: CoreBluetoothPeripheral) {
        lock.lock()
        if connections[peripheral.advertisement.id] === peripheral {
            connections[peripheral.advertisement.id] = nil
        }
        lock.unlock()
        registry.unregister(peripheral.advertisement.id)
    }

    private func connection(for peripheral: CBPeripheral) -> CoreBluetoothPeripheral? {
        lock.lock()
        defer { lock.unlock() }
        return connections[peripheral.identifier.uuidString]
    }

    // MARK: Scan start

    private func tryStartScan() {
        lock.lock()
        let requested = startRequested
        lock.unlock()
        guard requested else { return }
        switch central.state {
        case .poweredOn:
            if !central.isScanning {
                central.scanForPeripherals(
                    withServices: [serviceUuid],
                    options: [CBCentralManagerScanOptionAllowDuplicatesKey: true])
            }
            resumeStartWaiters(nil)
        case .unsupported, .unauthorized:
            resumeStartWaiters(BlueConnectError.transport("Bluetooth is unavailable: \(central.state.rawValue)."))
        default:
            // Unknown, resetting, powered off: keep waiting for the next state update.
            break
        }
    }

    private func resumeStartWaiters(_ error: (any Error)?) {
        lock.lock()
        let waiters = startWaiters
        startWaiters.removeAll()
        lock.unlock()
        for waiter in waiters {
            if let error {
                waiter.resume(throwing: error)
            } else {
                waiter.resume()
            }
        }
    }

    // MARK: CBCentralManagerDelegate

    public func centralManagerDidUpdateState(_ central: CBCentralManager) {
        tryStartScan()
    }

    public func centralManager(
        _ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
        advertisementData: [String: Any], rssi RSSI: NSNumber
    ) {
        let id = peripheral.identifier.uuidString
        let name = (advertisementData[CBAdvertisementDataLocalNameKey] as? String) ?? peripheral.name ?? "(unnamed)"
        let isConnectable = (advertisementData[CBAdvertisementDataIsConnectable] as? NSNumber)?.boolValue ?? false
        // The Stony Grey manufacturer data (company id + status byte); only a
        // packet that carries it updates `secured`. Others keep the last value.
        var secured: Bool?
        if let manufacturerData = advertisementData[CBAdvertisementDataManufacturerDataKey] as? Data, manufacturerData.count >= 3 {
            let bytes = [UInt8](manufacturerData)
            if bytes[0] == UInt8(options.manufacturerCompanyId & 0xFF), bytes[1] == UInt8(options.manufacturerCompanyId >> 8) {
                secured = bytes[2] & BlueConnectConstants.securedStatusMask != 0
            }
        }
        lock.lock()
        nativePeripherals[id] = peripheral
        lock.unlock()
        registry.observe(id: id, localName: name, isConnectable: isConnectable, secured: secured, dbm: RSSI.intValue)
    }

    public func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        connection(for: peripheral)?.onConnected()
    }

    public func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: (any Error)?) {
        connection(for: peripheral)?.onConnectFailed(error)
    }

    public func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: (any Error)?) {
        connection(for: peripheral)?.onDisconnected(error)
    }
}
