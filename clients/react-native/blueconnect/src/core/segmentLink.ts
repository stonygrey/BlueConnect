// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { BlePeripheral } from '../abstractions/blePeripheral';
import { BleTransportError, InvalidStateError } from '../abstractions/errors';
import { AsyncGate } from './asyncGate';
import { concatBytes } from './bytes';
import { IoLog } from './ioLog';
import { SegmentHeader } from './segmentHeader';
import { withTimeout } from './timeout';

/**
 * Message pipe over a peripheral: one `write` is one buffer, one `read`
 * returns one complete buffer. Each ATT value is a self-describing segment
 * (`documents/ble-framing.md`). The caller owns the peripheral and reads
 * from one place at a time.
 */
export class SegmentLink {
  static readonly defaultIncompleteTimeoutMs = 5_000;
  /** Bound on the best-effort `ABORT` after a failed `write`. */
  static readonly abortWriteTimeoutMs = 2_000;

  private readonly send = new AsyncGate();
  private readonly incompleteTimeoutMs: number;
  private chunks: Uint8Array[] = [];
  private accumulated = 0;
  private assembling = false;
  private seqIn = 0;

  constructor(
    private readonly peripheral: BlePeripheral,
    private readonly maxMessage: number,
    incompleteTimeoutMs?: number
  ) {
    if (maxMessage <= 0) {
      throw new RangeError('maxMessage must be positive');
    }
    this.incompleteTimeoutMs = incompleteTimeoutMs ?? SegmentLink.defaultIncompleteTimeoutMs;
  }

  async write(message: Uint8Array): Promise<void> {
    if (message.length > this.maxMessage) {
      throw new InvalidStateError(`Message is ${message.length} bytes; the maximum is ${this.maxMessage}.`);
    }
    await this.send.withLock(async () => {
      const payloadMtu = Math.max(1, this.peripheral.mtu);
      const cap = Math.max(0, payloadMtu - 1);
      if (cap < 1 && message.length > 0) {
        throw new BleTransportError('ATT payload is too small to carry a segment.');
      }
      let seq = Math.floor(Math.random() * SegmentHeader.seqModulus);
      let offset = 0;
      let first = true;
      try {
        do {
          const n = Math.min(cap, message.length - offset);
          const last = offset + n === message.length;
          let flags = 0;
          if (first) flags |= SegmentHeader.start;
          if (last) flags |= SegmentHeader.end;
          const segment = new Uint8Array(1 + n);
          segment[0] = SegmentHeader.pack(seq, flags);
          segment.set(message.subarray(offset, offset + n), 1);
          IoLog.info(IoLog.segment('write', seq, flags, segment));
          await this.peripheral.write(segment);
          offset += n;
          first = false;
          seq = (seq + 1) % SegmentHeader.seqModulus;
        } while (offset < message.length);
      } catch (e) {
        try {
          await withTimeout(this.sendAbort((seq + 1) % SegmentHeader.seqModulus), SegmentLink.abortWriteTimeoutMs);
        } catch {
          // Best effort; the write already failed.
        }
        throw e;
      }
    });
  }

  async read(): Promise<Uint8Array> {
    this.resetRx();
    for (;;) {
      let pdu: Uint8Array;
      if (this.assembling) {
        const next = await this.peripheral.read(this.incompleteTimeoutMs);
        if (next === null) {
          this.resetRx();
          await this.trySendAbort(0);
          throw new BleTransportError('Incomplete message: timed out waiting for the next segment.');
        }
        pdu = next;
      } else {
        pdu = (await this.peripheral.read())!;
      }
      this.logInbound(pdu);
      if (pdu.length === 0 || SegmentHeader.has(pdu[0], SegmentHeader.reserved)) {
        this.resetRx();
        await this.trySendAbort(0);
        throw new BleTransportError('Malformed segment header.');
      }
      const header = pdu[0];
      if (SegmentHeader.has(header, SegmentHeader.abort)) {
        const wasAssembling = this.assembling;
        this.resetRx();
        if (wasAssembling) {
          throw new BleTransportError('Peer aborted the message.');
        }
        continue;
      }
      const seq = SegmentHeader.seq(header);
      if (SegmentHeader.has(header, SegmentHeader.start)) {
        this.assembling = true;
        this.seqIn = seq;
        this.chunks = [pdu.subarray(1)];
        this.accumulated = pdu.length - 1;
      } else {
        if (!this.assembling) {
          await this.trySendAbort(0);
          throw new BleTransportError('Orphan continuation segment.');
        }
        if (seq !== (this.seqIn + 1) % SegmentHeader.seqModulus) {
          this.resetRx();
          await this.trySendAbort(0);
          throw new BleTransportError('Segment sequence gap.');
        }
        this.seqIn = seq;
        this.chunks.push(pdu.subarray(1));
        this.accumulated += pdu.length - 1;
      }
      if (this.accumulated > this.maxMessage) {
        this.resetRx();
        await this.trySendAbort(0);
        throw new BleTransportError(`Message exceeds the ${this.maxMessage}-byte cap.`);
      }
      if (SegmentHeader.has(header, SegmentHeader.end)) {
        const complete = concatBytes(...this.chunks);
        this.resetRx();
        return complete;
      }
    }
  }

  private logInbound(pdu: Uint8Array): void {
    if (pdu.length === 0) {
      IoLog.info(IoLog.segment('read', 0, 0, pdu));
      return;
    }
    IoLog.info(IoLog.segment('read', SegmentHeader.seq(pdu[0]), SegmentHeader.flags(pdu[0]), pdu));
  }

  private resetRx(): void {
    this.assembling = false;
    this.chunks = [];
    this.accumulated = 0;
  }

  /** Best effort: the read that triggered this already failed. */
  private async trySendAbort(seq: number): Promise<void> {
    try {
      await this.send.withLock(() => this.sendAbort(seq));
    } catch {
      // The read already failed.
    }
  }

  private async sendAbort(seq: number): Promise<void> {
    const abort = Uint8Array.of(SegmentHeader.pack(seq, SegmentHeader.abort));
    IoLog.info(IoLog.segment('write', seq, SegmentHeader.abort, abort));
    await this.peripheral.write(abort);
  }
}
