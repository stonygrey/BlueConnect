// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BleManager, State, type Device } from 'react-native-ble-plx';

import type { Advertisement } from '../abstractions/advertisement';
import type { BlePeripheral } from '../abstractions/blePeripheral';
import type { BleScanner, ScanEvent } from '../abstractions/bleScanner';
import { BlueConnectConstants, defaultOptions, type BlueConnectOptions } from '../abstractions/blueConnectOptions';
import { BleTransportError, InvalidStateError } from '../abstractions/errors';
import { AdvertisementRegistry } from '../core/advertisementRegistry';
import { base64Decode } from '../core/base64';
import { withTimeout } from '../core/timeout';
import { BlePlxPeripheral } from './blePlxPeripheral';

/**
 * The scanner over react-native-ble-plx: a scan filtered on the Verdigris
 * service UUID, merged into the shared registry. The library hands over
 * the manufacturer data as the packet carries it, company id first in
 * little-endian order, so the status byte is the third byte. On iOS the
 * library reports the connectable flag; where it is absent the device is
 * reported as connectable.
 */
export class BlePlxScanner implements BleScanner {
  /** How long `start` waits for the radio to report itself powered on. */
  static readonly powerOnTimeoutMs = 10_000;

  readonly manager: BleManager;
  private readonly registry = new AdvertisementRegistry(-127, 20);
  private scanning = false;

  constructor(
    readonly options: BlueConnectOptions = defaultOptions,
    manager?: BleManager
  ) {
    this.manager = manager ?? new BleManager();
  }

  get peripherals(): Advertisement[] {
    return this.registry.peripherals;
  }

  get isScanning(): boolean {
    return this.scanning;
  }

  onEvent(listener: (event: ScanEvent) => void): () => void {
    return this.registry.onEvent(listener);
  }

  lastSeen(id: string): number | null {
    return this.registry.lastSeen(id);
  }

  async start(): Promise<void> {
    if (this.scanning) return;
    // The first use of the manager makes iOS ask for the Bluetooth
    // permission; the state settles once the user has answered.
    const state = await this.waitForRadio();
    if (state === State.Unsupported || state === State.Unauthorized) {
      throw new BleTransportError(`Bluetooth is unavailable: ${state}.`);
    }
    if (state === State.PoweredOff) {
      throw new BleTransportError('Bluetooth is disabled.');
    }
    this.manager.startDeviceScan([this.options.serviceUuid], { allowDuplicates: true }, (error, device) => {
      if (error) {
        this.scanning = false;
        return;
      }
      if (device) this.handle(device);
    });
    this.scanning = true;
  }

  createPeripheral(advertisement: Advertisement): BlePeripheral {
    const cached = this.registry.cached(advertisement.id);
    if (cached === null) {
      throw new InvalidStateError(`'${advertisement.localName}' (${advertisement.id}) is no longer in the scan cache.`);
    }
    return new BlePlxPeripheral(this, cached, this.options);
  }

  /** Marks a device as connecting so that the registry does not expire it. */
  register(id: string): void {
    this.registry.register(id);
  }

  unregister(id: string): void {
    this.registry.unregister(id);
  }

  /** The first state that is not `Unknown` or `Resetting`. */
  private waitForRadio(): Promise<State> {
    return withTimeout(
      new Promise<State>((resolve) => {
        const subscription = this.manager.onStateChange((state) => {
          if (state !== State.Unknown && state !== State.Resetting) {
            subscription.remove();
            resolve(state);
          }
        }, true);
      }),
      BlePlxScanner.powerOnTimeoutMs
    );
  }

  private handle(device: Device): void {
    const name = device.localName || device.name || this.registry.cached(device.id)?.localName || '(unnamed)';
    let secured: boolean | null = null;
    const data = device.manufacturerData ? base64Decode(device.manufacturerData) : null;
    if (data && data.length >= 3) {
      const companyId = data[0] | (data[1] << 8);
      if (companyId === this.options.manufacturerCompanyId) {
        const status = data[2];
        secured = (status & BlueConnectConstants.securedStatusMask) !== 0;
      }
    }
    this.registry.observe({
      id: device.id,
      localName: name,
      isConnectable: device.isConnectable ?? true,
      secured,
      dbm: device.rssi ?? 0,
    });
  }
}
