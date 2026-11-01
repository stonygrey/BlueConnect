// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement, BleScanner } from '@stonygrey/blueconnect';

import type { DeviceSelection } from '../data/deviceSelection';
import { describe } from '../domain/toasts';
import { Observable } from '../state/observable';

/** The scan list, sampled from the scanner every 500 ms. */
export class ScanModel extends Observable {
  devices: Advertisement[] = [];
  scanFailure: string | null = null;
  private sampling: ReturnType<typeof setInterval> | null = null;

  constructor(
    private readonly scanner: BleScanner,
    private readonly selection: DeviceSelection
  ) {
    super();
  }

  async start(): Promise<void> {
    try {
      await this.scanner.start();
    } catch (e) {
      this.scanFailure = describe(e);
      this.notify();
      return;
    }
    this.sampling ??= setInterval(() => {
      this.devices = [...this.scanner.peripherals].sort((a, b) => b.rssi.valueDbm - a.rssi.valueDbm);
      this.selection.refresh(this.devices);
      this.notify();
    }, 500);
  }

  stop(): void {
    if (this.sampling !== null) {
      clearInterval(this.sampling);
      this.sampling = null;
    }
  }

  select(device: Advertisement): void {
    this.selection.select(device);
  }
}
