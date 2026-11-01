// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BleTransportError, VerdigrisPeripheral, bytesEqual, delay, type Advertisement, type BleScanner, type BlueConnectOptions } from '@stonygrey/blueconnect';

import type { PairingRepository } from '../data/pairingRepository';
import { Observable } from '../state/observable';

/**
 * The Loop tab: connect, echo, disconnect, over and over, with the
 * high-tier pairing. JavaScript has no task cancellation, so `stop` raises
 * a flag that the loop checks between steps and waits for the cycle in
 * flight to end.
 */
export class SoakLoop extends Observable {
  isRunning = false;
  deviceName = '';
  tests = 0;
  connectionFailures = 0;
  commandFailures = 0;
  minConnectMs: number | null = null;
  avgConnectMs: number | null = null;
  maxConnectMs: number | null = null;
  started: number | null = null;
  elapsedMs = 0;
  private stopRequested = false;
  private run: Promise<void> | null = null;
  private ticker: ReturnType<typeof setInterval> | null = null;

  constructor(
    private readonly scanner: BleScanner,
    private readonly options: BlueConnectOptions,
    private readonly pairings: PairingRepository
  ) {
    super();
  }

  async start(device: Advertisement): Promise<void> {
    await this.stop();
    this.tests = 0;
    this.connectionFailures = 0;
    this.commandFailures = 0;
    this.minConnectMs = null;
    this.avgConnectMs = null;
    this.maxConnectMs = null;
    this.deviceName = device.localName;
    this.started = Date.now();
    this.elapsedMs = 0;
    this.isRunning = true;
    this.stopRequested = false;
    this.ticker = setInterval(() => this.tick(), 1_000);
    this.notify();
    this.run = this.loop(device);
  }

  async stop(): Promise<void> {
    this.stopRequested = true;
    if (this.ticker !== null) {
      clearInterval(this.ticker);
      this.ticker = null;
    }
    await this.run;
    this.run = null;
    this.isRunning = false;
    this.notify();
  }

  private tick(): void {
    if (this.started !== null) {
      this.elapsedMs = Date.now() - this.started;
      this.notify();
    }
  }

  private async loop(device: Advertisement): Promise<void> {
    while (!this.stopRequested) {
      const security = this.pairings.security('high');
      try {
        const peripheral = this.scanner.createPeripheral(device);
        const verdigris = new VerdigrisPeripheral(peripheral, this.options, security);
        try {
          const began = Date.now();
          await peripheral.connect();
          this.recordConnect(Date.now() - began);
          try {
            const payload = randomPayload(this.options.isEchoVerificationEnabled ? 1536 : 16);
            const reply = await verdigris.echo(payload);
            if (!bytesEqual(reply, payload)) {
              throw new BleTransportError('Echoed payload does not match the command.');
            }
          } catch {
            this.commandFailures++;
          }
          try {
            await peripheral.disconnect();
          } catch {
            // The cycle still counts.
          }
        } catch {
          this.connectionFailures++;
        } finally {
          await verdigris.close();
        }
      } catch {
        this.connectionFailures++;
      }
      this.tests++;
      this.tick();
      if (this.stopRequested) break;
      const span = this.options.maxCycleDelayMs - this.options.minCycleDelayMs;
      await delay(this.options.minCycleDelayMs + Math.floor(Math.random() * (span + 1)));
    }
    this.isRunning = false;
    this.notify();
  }

  private recordConnect(ms: number): void {
    this.minConnectMs = this.minConnectMs === null ? ms : Math.min(this.minConnectMs, ms);
    this.maxConnectMs = this.maxConnectMs === null ? ms : Math.max(this.maxConnectMs, ms);
    this.avgConnectMs = ((this.avgConnectMs ?? 0) * this.tests + ms) / (this.tests + 1);
  }
}

function randomPayload(count: number): Uint8Array {
  return Uint8Array.from({ length: count }, () => Math.floor(Math.random() * 256));
}
