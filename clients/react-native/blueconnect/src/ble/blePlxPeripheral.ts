// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { Platform } from 'react-native';
import type { Device, Subscription } from 'react-native-ble-plx';

import type { Advertisement } from '../abstractions/advertisement';
import type { BlePeripheral, ConnectionState } from '../abstractions/blePeripheral';
import { BlueConnectConstants, type BlueConnectOptions } from '../abstractions/blueConnectOptions';
import { BleTimeoutError, BleTransportError, InvalidStateError } from '../abstractions/errors';
import { base64Decode, base64Encode } from '../core/base64';
import { runConnectRetry } from '../core/connectRetry';
import { ReceiveQueue } from '../core/receiveQueue';
import { DeadlineExceeded, withTimeout } from '../core/timeout';
import type { BlePlxScanner } from './blePlxScanner';

/**
 * One GATT connection through react-native-ble-plx. The library queues GATT
 * operations itself and returns promises, so this class only orders them:
 * connect, settle the MTU, discover, subscribe, then one write at a time.
 * Values cross the bridge as base64 strings.
 */
export class BlePlxPeripheral implements BlePeripheral {
  private readonly received = new ReceiveQueue();
  private readonly listeners = new Set<(state: ConnectionState) => void>();
  private device: Device | null = null;
  private values: Subscription | null = null;
  private disconnection: Subscription | null = null;
  private connected = false;
  private closed = false;
  private payloadMtu = 0;

  constructor(
    private readonly scanner: BlePlxScanner,
    readonly advertisement: Advertisement,
    private readonly options: BlueConnectOptions
  ) {}

  get isConnected(): boolean {
    return this.connected;
  }

  get mtu(): number {
    return this.connected ? this.payloadMtu : 0;
  }

  onStateChange(listener: (state: ConnectionState) => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  async connect(): Promise<void> {
    if (this.closed) {
      throw new InvalidStateError('The peripheral has been closed.');
    }
    await runConnectRetry(
      this.options,
      this.advertisement.localName,
      () => {
        const seen = this.scanner.lastSeen(this.advertisement.id);
        return seen !== null && Date.now() - seen <= BlueConnectConstants.scannerExpirationMs;
      },
      (timeoutMs) => this.connectOnce(timeoutMs)
    );
  }

  private async connectOnce(timeoutMs: number): Promise<void> {
    this.scanner.register(this.advertisement.id);
    try {
      await withTimeout(this.connectCore(), timeoutMs);
      this.connected = true;
      this.emit('connected');
    } catch (e) {
      await this.abortConnect();
      if (e instanceof DeadlineExceeded) {
        throw new BleTimeoutError(`Connecting to '${this.advertisement.localName}' timed out after ${Math.round(timeoutMs / 1000)}s.`);
      }
      throw e;
    }
  }

  private async connectCore(): Promise<void> {
    const manager = this.scanner.manager;
    const id = this.advertisement.id;
    let device = await manager.connectToDevice(id, { timeout: this.options.connectTimeoutMs });
    this.device = device;
    this.disconnection?.remove();
    this.disconnection = device.onDisconnected(() => {
      if (this.connected) {
        this.connected = false;
        this.emit('disconnected');
        this.received.close(new BleTransportError('Peripheral disconnected.'));
      }
    });
    device = await device.discoverAllServicesAndCharacteristics();
    // iOS negotiates the ATT MTU itself and the library reports it; Android
    // negotiates on request. The usable payload is 3 less.
    if (Platform.OS === 'android') {
      try {
        device = await device.requestMTU(247);
      } catch {
        // The default of 23 stays in force.
      }
    }
    const att = device.mtu > 0 ? device.mtu : 23;
    this.payloadMtu = Math.max(20, att - 3);
    this.device = device;
    const characteristics = await device.characteristicsForService(this.options.serviceUuid).catch(() => null);
    if (characteristics === null) {
      throw new BleTransportError(`Peripheral does not expose GATT service ${this.options.serviceUuid}.`);
    }
    if (!characteristics.some((c) => sameUuid(c.uuid, this.options.rxCharacteristicUuid))) {
      throw new BleTransportError(`RX characteristic ${this.options.rxCharacteristicUuid} not found.`);
    }
    if (!characteristics.some((c) => sameUuid(c.uuid, this.options.txCharacteristicUuid))) {
      throw new BleTransportError(`TX characteristic ${this.options.txCharacteristicUuid} not found.`);
    }
    this.values?.remove();
    this.values = device.monitorCharacteristicForService(this.options.serviceUuid, this.options.txCharacteristicUuid, (error, characteristic) => {
      if (error || !characteristic?.value) return;
      const bytes = base64Decode(characteristic.value);
      if (bytes) this.received.push(bytes);
    });
  }

  async disconnect(): Promise<void> {
    if (!this.connected) return;
    await this.scanner.manager.cancelDeviceConnection(this.advertisement.id);
    this.connected = false;
    this.emit('disconnected');
  }

  read(timeoutMs?: number): Promise<Uint8Array | null> {
    return this.received.pop(timeoutMs);
  }

  async write(bytes: Uint8Array): Promise<void> {
    const device = this.device;
    if (!this.connected || device === null) {
      throw new InvalidStateError('Peripheral is not connected.');
    }
    const chunk = Math.max(1, this.payloadMtu);
    for (let offset = 0; offset < bytes.length; offset += chunk) {
      const end = Math.min(offset + chunk, bytes.length);
      await device.writeCharacteristicWithResponseForService(this.options.serviceUuid, this.options.rxCharacteristicUuid, base64Encode(bytes.subarray(offset, end)));
    }
  }

  async close(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    if (this.connected) {
      try {
        await withTimeout(this.disconnect(), 5_000);
      } catch {
        // Teardown must not throw; the cleanup below still runs.
      }
    } else {
      await this.abortConnect();
    }
    this.values?.remove();
    this.values = null;
    this.disconnection?.remove();
    this.disconnection = null;
    this.scanner.unregister(this.advertisement.id);
    this.received.close(new BleTransportError('The peripheral has been closed.'));
    this.listeners.clear();
  }

  private async abortConnect(): Promise<void> {
    try {
      await withTimeout(this.scanner.manager.cancelDeviceConnection(this.advertisement.id), 5_000);
    } catch {
      // Best effort; the attempt already failed.
    }
  }

  private emit(state: ConnectionState): void {
    for (const listener of Array.from(this.listeners)) {
      listener(state);
    }
  }
}

function sameUuid(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}
