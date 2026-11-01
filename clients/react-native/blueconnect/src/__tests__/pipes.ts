// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement } from '../abstractions/advertisement';
import type { BlePeripheral, ConnectionState } from '../abstractions/blePeripheral';
import { BleTransportError } from '../abstractions/errors';
import { ReceiveQueue } from '../core/receiveQueue';

export const pipeAdvertisement: Advertisement = {
  id: 'pipe',
  localName: 'pipe',
  isConnectable: true,
  isSecured: false,
  rssi: { valueDbm: -50, minDbm: -50, maxDbm: -50 },
  timestamp: Date.now(),
};

export function payload(length: number): Uint8Array {
  return Uint8Array.from({ length }, (_, i) => i & 0xff);
}

/**
 * Test double: inbound ATT values are queued by the test, outbound writes
 * are recorded and can be scripted to fail.
 */
export class ScriptedPipe implements BlePeripheral {
  private readonly inbound = new ReceiveQueue();
  readonly written: Uint8Array[] = [];
  private failAt: number | null = null;
  mtu = 20;
  readonly advertisement = pipeAdvertisement;
  readonly isConnected = true;

  enqueue(bytes: number[]): void {
    this.inbound.push(Uint8Array.from(bytes));
  }

  failOnWrite(index: number): void {
    this.failAt = index;
  }

  async connect(): Promise<void> {}

  async disconnect(): Promise<void> {}

  read(timeoutMs?: number): Promise<Uint8Array | null> {
    return this.inbound.pop(timeoutMs);
  }

  async write(bytes: Uint8Array): Promise<void> {
    if (this.failAt === this.written.length) {
      this.failAt = null;
      throw new BleTransportError('scripted write failure');
    }
    this.written.push(bytes);
  }

  onStateChange(_listener: (state: ConnectionState) => void): () => void {
    return () => {};
  }

  async close(): Promise<void> {
    this.inbound.close(new BleTransportError('closed'));
  }
}

/** Two cross-wired in-memory peripherals. Each `write` is one ATT value. */
export class LoopbackPipe implements BlePeripheral {
  private readonly inbound = new ReceiveQueue();
  private peer!: LoopbackPipe;
  readonly advertisement = pipeAdvertisement;
  readonly isConnected = true;

  private constructor(public mtu: number) {}

  static pair(mtu = 20): [LoopbackPipe, LoopbackPipe] {
    const a = new LoopbackPipe(mtu);
    const b = new LoopbackPipe(mtu);
    a.peer = b;
    b.peer = a;
    return [a, b];
  }

  async connect(): Promise<void> {}

  async disconnect(): Promise<void> {}

  read(timeoutMs?: number): Promise<Uint8Array | null> {
    return this.inbound.pop(timeoutMs);
  }

  async write(bytes: Uint8Array): Promise<void> {
    this.peer.inbound.push(bytes);
  }

  onStateChange(_listener: (state: ConnectionState) => void): () => void {
    return () => {};
  }

  async close(): Promise<void> {
    this.inbound.close(new BleTransportError('closed'));
  }
}
