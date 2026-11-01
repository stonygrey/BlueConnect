// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { TorannError, type HandshakeState, type Transport } from '@stonygrey/torann';

import type { BlePeripheral } from '../abstractions/blePeripheral';
import { InvalidStateError, LinkSecurityError } from '../abstractions/errors';
import { bytesEqual, isZero, utf8Encode } from '../core/bytes';
import { IoLog } from '../core/ioLog';
import { SegmentLink } from '../core/segmentLink';
import { FrameType } from './frameType';
import { LinkProtocol, type LinkSecurity } from './linkProtocol';

/**
 * Encrypted, mutually authenticated link over a peripheral, as initiator.
 * An initiator holding a pinned peer key opens with Noise IK; otherwise
 * Noise XX runs. After the handshake every message is AEAD-sealed as a
 * `0x02` frame. This is the initiator half only; the responder half is the
 * firmware. Torann's calls are synchronous, so only the segment I/O is
 * awaited.
 */
export class SecureLink {
  /** Bound into the handshake transcript. Framing is below this layer. */
  static readonly prologue = utf8Encode('BlueConnectLink/1');
  /**
   * Messages 2 and 3 carry this single ignored byte instead of an empty
   * payload: Android's native AES-GCM rejects zero-length inputs.
   */
  static readonly handshakePayload = Uint8Array.of(0x5a);
  static readonly handshakeMessage1Length = LinkProtocol.keyLength;
  static readonly handshakeMessage2Length = LinkProtocol.keyLength + LinkProtocol.keyLength + LinkProtocol.tagLength + 1 + LinkProtocol.tagLength;
  static readonly handshakeMessage3Length = LinkProtocol.keyLength + LinkProtocol.tagLength + 1 + LinkProtocol.tagLength;
  static readonly ikHandshakeMessage1Length = LinkProtocol.keyLength + LinkProtocol.keyLength + LinkProtocol.tagLength + 1 + LinkProtocol.tagLength;
  static readonly ikHandshakeMessage2Length = LinkProtocol.keyLength + 1 + LinkProtocol.tagLength;

  private readonly segments: SegmentLink;
  private transport: Transport | null = null;
  private closed = false;

  /** The peer's proven static key; available once the handshake has completed. */
  remoteStaticKey: Uint8Array | null = null;

  constructor(
    peripheral: BlePeripheral,
    maxFrame: number,
    private readonly security: LinkSecurity
  ) {
    this.segments = new SegmentLink(peripheral, maxFrame);
  }

  get isEstablished(): boolean {
    return this.transport !== null;
  }

  /** Runs the handshake, IK when a peer key is pinned and XX otherwise, before any traffic flows. */
  async establish(): Promise<void> {
    if (this.closed) {
      throw new InvalidStateError('The secure link has been closed.');
    }
    if (this.isEstablished) return;
    const staticKeyPair = LinkProtocol.xx().createKeyPair(this.security.identity.privateKey);
    let handshake: HandshakeState | null = null;
    try {
      const pin = this.security.pinnedPeerKey;
      let split: Transport | null;
      if (pin !== null) {
        // IK: message 1 both proves this identity and shows we already hold
        // the peer's key; the reply completes the session. A stale pin fails
        // on the device and yields silence.
        const state = step(() => LinkProtocol.ik().createHandshake({ initiator: true, prologue: SecureLink.prologue, localStatic: staticKeyPair, remoteStatic: pin }));
        handshake = state;
        const message1 = step(() => state.writeMessage(SecureLink.handshakePayload));
        await this.writeTyped(FrameType.handshake, message1.message);
        const message2 = await this.readHandshakeFrame();
        expectHandshakeLength(message2, SecureLink.ikHandshakeMessage2Length, 2);
        const read2 = step(() => state.readMessage(message2));
        expectHandshakePayload(read2.payload);
        split = read2.transport;
      } else {
        const state = step(() => LinkProtocol.xx().createHandshake({ initiator: true, prologue: SecureLink.prologue, localStatic: staticKeyPair }));
        handshake = state;
        const message1 = step(() => state.writeMessage());
        await this.writeTyped(FrameType.handshake, message1.message);
        const message2 = await this.readHandshakeFrame();
        expectHandshakeLength(message2, SecureLink.handshakeMessage2Length, 2);
        const read2 = step(() => state.readMessage(message2));
        expectHandshakePayload(read2.payload);
        this.verifyPeer(state.remoteStaticPublicKey());
        const message3 = step(() => state.writeMessage(SecureLink.handshakePayload));
        await this.writeTyped(FrameType.handshake, message3.message);
        split = message3.transport;
      }
      this.remoteStaticKey = handshake.remoteStaticPublicKey();
      if (split === null) {
        throw new LinkSecurityError('The handshake completed without producing a transport.');
      }
      this.transport = split;
    } finally {
      handshake?.close();
      staticKeyPair.close();
    }
    IoLog.info(`Secure link established; peer static key ${IoLog.hex(this.remoteStaticKey ?? new Uint8Array(0))}.`);
  }

  async send(message: Uint8Array): Promise<void> {
    await this.establish();
    const sealed = step(() => this.transport!.writeMessage(message));
    await this.writeTyped(FrameType.encrypted, sealed);
  }

  async receive(): Promise<Uint8Array> {
    await this.establish();
    for (;;) {
      const [type, payload] = await this.readTyped();
      if (type === FrameType.encrypted) {
        try {
          return this.transport!.readMessage(payload);
        } catch (e) {
          if (e instanceof TorannError) {
            throw new LinkSecurityError('AEAD authentication failed; the frame was forged, corrupted, or replayed.', e);
          }
          throw e;
        }
      }
      if (type === FrameType.plaintext) {
        throw new LinkSecurityError('Plaintext frame on a secure link.');
      }
      IoLog.debug(`Ignoring frame type ${type} on an established secure link.`);
    }
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    this.transport?.close();
    this.transport = null;
  }

  private verifyPeer(remoteStatic: Uint8Array | null): void {
    // Torann leaves non-contributory-key checks to the application (Noise
    // spec §12.1); an all-zero static is never a legitimate identity.
    if (remoteStatic === null || isZero(remoteStatic)) {
      throw new LinkSecurityError('Peer presented an all-zero static key.');
    }
    const pin = this.security.pinnedPeerKey;
    if (pin !== null && !bytesEqual(remoteStatic, pin)) {
      throw new LinkSecurityError("Peer's static key does not match the paired key.");
    }
  }

  private async writeTyped(type: number, payload: Uint8Array): Promise<void> {
    const bundled = FrameType.bundle(type, payload);
    IoLog.info(IoLog.message('write', type, bundled));
    await this.segments.write(bundled);
  }

  private async readTyped(): Promise<[number, Uint8Array]> {
    const bundled = await this.segments.read();
    const [type, payload] = FrameType.split(bundled);
    IoLog.info(IoLog.message('read', type, bundled));
    return [type, payload];
  }

  private async readHandshakeFrame(): Promise<Uint8Array> {
    const [type, payload] = await this.readTyped();
    if (type !== FrameType.handshake) {
      throw new LinkSecurityError(`Expected a handshake frame but received type ${type}.`);
    }
    return payload;
  }
}

/** Runs one Torann step, mapping its failures into the link taxonomy. */
function step<T>(body: () => T): T {
  try {
    return body();
  } catch (e) {
    if (e instanceof TorannError) {
      throw new LinkSecurityError(`The handshake failed: ${e.message}`, e);
    }
    throw e;
  }
}

function expectHandshakeLength(message: Uint8Array, expected: number, number: number): void {
  if (message.length !== expected) {
    throw new LinkSecurityError(`Handshake message ${number} is ${message.length} bytes; expected ${expected}.`);
  }
}

function expectHandshakePayload(payload: Uint8Array): void {
  if (!bytesEqual(payload, SecureLink.handshakePayload)) {
    throw new LinkSecurityError('Handshake payload does not match the protocol constant.');
  }
}
