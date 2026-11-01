// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/**
 * A stand-in for `@stonygrey/torann` with no cryptography: handshake messages
 * have the lengths the real suites produce and carry the payload in clear,
 * and the transport appends a 16-byte tag of zeros. The link, framing, and
 * Verdigris layers above it are what the tests exercise; the real Noise
 * session is exercised against the board.
 */
export class TorannError extends Error {
  constructor(
    readonly code: string,
    message: string
  ) {
    super(message);
    this.name = 'TorannError';
  }
}

const keyLength = 32;
const tagLength = 16;

class FakeKeyPair {
  constructor(private readonly priv: Uint8Array) {}

  get publicKey(): Uint8Array {
    // The "public key" is the private key with every byte inverted.
    return this.priv.map((b) => ~b & 0xff);
  }

  privateKey(): Uint8Array {
    return new Uint8Array(this.priv);
  }

  close(): void {}
}

class FakeTransport {
  writeMessage(payload: Uint8Array): Uint8Array {
    const out = new Uint8Array(payload.length + tagLength);
    out.set(payload);
    return out;
  }

  readMessage(message: Uint8Array): Uint8Array {
    if (message.length < tagLength || !message.subarray(message.length - tagLength).every((b) => b === 0)) {
      throw new TorannError('authenticationFailed', 'The message failed authentication.');
    }
    return message.slice(0, message.length - tagLength);
  }

  close(): void {}
}

/** The XX message lengths: 32, 97, 65; the IK lengths: 97, 49. The payload byte is last before the final tag. */
class FakeHandshake {
  private step = 0;
  private remote: Uint8Array | null;
  private transport: FakeTransport | null = null;

  constructor(
    private readonly pattern: 'XX' | 'IK',
    readonly initiator: boolean,
    private readonly local: FakeKeyPair,
    remoteStatic: Uint8Array | null
  ) {
    this.remote = remoteStatic;
  }

  private lengths(): number[] {
    return this.pattern === 'XX' ? [keyLength, 97, 65] : [97, 49];
  }

  writeMessage(payload: Uint8Array = new Uint8Array(0)): { message: Uint8Array; handshakeHash: Uint8Array | null; transport: FakeTransport | null } {
    const lengths = this.lengths();
    const message = new Uint8Array(lengths[this.step]);
    // Messages that carry the static key put it first; the payload goes before the trailing tag.
    const carriesStatic = this.pattern === 'XX' ? this.step > 0 : this.step === 0;
    if (carriesStatic) message.set(this.local.publicKey, 0);
    if (payload.length > 0) message[message.length - tagLength - 1] = payload[0];
    this.step++;
    return { message, ...this.completion() };
  }

  readMessage(message: Uint8Array): { payload: Uint8Array; handshakeHash: Uint8Array | null; transport: FakeTransport | null } {
    const lengths = this.lengths();
    if (message.length !== lengths[this.step]) {
      throw new TorannError('authenticationFailed', `Handshake message ${this.step + 1} has the wrong length.`);
    }
    const carriesStatic = this.pattern === 'XX' ? this.step > 0 : this.step === 0;
    if (carriesStatic) this.remote = message.slice(0, keyLength);
    const payload = message.length > keyLength ? Uint8Array.of(message[message.length - tagLength - 1]) : new Uint8Array(0);
    this.step++;
    return { payload, ...this.completion() };
  }

  remoteStaticPublicKey(): Uint8Array | null {
    return this.remote;
  }

  close(): void {}

  private completion(): { handshakeHash: Uint8Array | null; transport: FakeTransport | null } {
    if (this.step < this.lengths().length) return { handshakeHash: null, transport: null };
    this.transport ??= new FakeTransport();
    return { handshakeHash: new Uint8Array(32), transport: this.transport };
  }
}

export class NoiseProtocol {
  private constructor(readonly name: string) {}

  static parse(name: string): NoiseProtocol {
    if (!name.startsWith('Noise_')) throw new TorannError('invalidProtocolName', `'${name}' is not a valid Noise protocol name.`);
    return new NoiseProtocol(name);
  }

  generateKeyPair(): FakeKeyPair {
    return new FakeKeyPair(Uint8Array.from({ length: keyLength }, () => 1 + Math.floor(Math.random() * 255)));
  }

  createKeyPair(privateKey: Uint8Array): FakeKeyPair {
    if (privateKey.length !== keyLength) throw new TorannError('invalidArgument', 'The private key must be 32 bytes.');
    return new FakeKeyPair(new Uint8Array(privateKey));
  }

  createHandshake(options: { initiator: boolean; prologue?: Uint8Array; localStatic?: FakeKeyPair; remoteStatic?: Uint8Array | null }): FakeHandshake {
    const pattern = this.name.includes('_IK_') ? 'IK' : 'XX';
    return new FakeHandshake(pattern, options.initiator, options.localStatic!, options.remoteStatic ?? null);
  }
}
