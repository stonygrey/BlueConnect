// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { NoiseProtocol } from '@stonygrey/torann';

let xx: NoiseProtocol | null = null;
let ik: NoiseProtocol | null = null;

/** The Noise suites the link runs. The parsed protocols are cached after the first use. */
export const LinkProtocol = {
  name: 'Noise_XX_25519_AESGCM_SHA256',
  pinnedName: 'Noise_IK_25519_AESGCM_SHA256',
  keyLength: 32,
  tagLength: 16,

  xx(): NoiseProtocol {
    return (xx ??= NoiseProtocol.parse(LinkProtocol.name));
  },

  ik(): NoiseProtocol {
    return (ik ??= NoiseProtocol.parse(LinkProtocol.pinnedName));
  },
} as const;

/** The app's long-term X25519 identity for one tier. */
export class LinkIdentity {
  private constructor(
    readonly privateKey: Uint8Array,
    readonly publicKey: Uint8Array
  ) {}

  /** Derives the public key with Torann. Throws `RangeError` for a key that is not 32 bytes. */
  static fromPrivateKey(privateKey: Uint8Array): LinkIdentity {
    if (privateKey.length !== LinkProtocol.keyLength) {
      throw new RangeError(`A link identity private key is ${LinkProtocol.keyLength} bytes.`);
    }
    const keyPair = LinkProtocol.xx().createKeyPair(privateKey);
    try {
      return new LinkIdentity(new Uint8Array(privateKey), keyPair.publicKey);
    } finally {
      keyPair.close();
    }
  }

  static generate(): LinkIdentity {
    const keyPair = LinkProtocol.xx().generateKeyPair();
    try {
      return new LinkIdentity(keyPair.privateKey(), keyPair.publicKey);
    } finally {
      keyPair.close();
    }
  }
}

/**
 * What a session is opened with: this side's identity and, once paired, the
 * device key it must present. No endorsement: this library does not carry
 * client certificates.
 */
export class LinkSecurity {
  constructor(
    readonly identity: LinkIdentity,
    readonly pinnedPeerKey: Uint8Array | null
  ) {}
}
