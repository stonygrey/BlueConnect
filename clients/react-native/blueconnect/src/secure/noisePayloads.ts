// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { LinkSecurityError } from '../abstractions/errors';
import { base64UrlDecode, base64UrlEncode } from '../core/base64';
import { concatBytes, isZero } from '../core/bytes';
import type { PrivilegeLevel } from '../verdigris/privilege';
import { LinkProtocol } from './linkProtocol';

/**
 * `String.split` on `:` with a limit: at most `limit` parts, the last one
 * keeping any further separators.
 */
function splitLimited(text: string, limit: number): string[] {
  const parts: string[] = [];
  let rest = text;
  while (parts.length < limit - 1) {
    const i = rest.indexOf(':');
    if (i < 0) break;
    parts.push(rest.substring(0, i));
    rest = rest.substring(i + 1);
  }
  parts.push(rest);
  return parts;
}

function checkedKeys(first: Uint8Array, second: Uint8Array, what: string): void {
  if (first.length !== LinkProtocol.keyLength || second.length !== LinkProtocol.keyLength || isZero(first) || isZero(second)) {
    throw new LinkSecurityError(`Noise ${what} payload fields are out of range.`);
  }
}

/**
 * A USB-provisioned Noise credential as the QR carries it:
 * `SGNK1:<L|H>:<base64url 64 bytes>[:<NAME>]`, the blob being the app's
 * X25519 private key followed by the device's X25519 public key. The QR is
 * the credential itself.
 */
export class NoiseQrPayload {
  static readonly prefix = 'SGNK1';

  constructor(
    readonly tier: PrivilegeLevel,
    readonly appPrivateKey: Uint8Array,
    readonly devicePublicKey: Uint8Array,
    readonly deviceName: string | null = null
  ) {}

  encode(): string {
    checkedKeys(this.appPrivateKey, this.devicePublicKey, 'QR');
    const encoded = base64UrlEncode(concatBytes(this.appPrivateKey, this.devicePublicKey));
    const letter = this.tier === 'high' ? 'H' : 'L';
    const head = `${NoiseQrPayload.prefix}:${letter}:${encoded}`;
    return this.deviceName ? `${head}:${this.deviceName}` : head;
  }

  /** Null for anything that is not a well-formed SGNK1 payload with non-zero keys. */
  static parse(text: string): NoiseQrPayload | null {
    const parts = splitLimited(text.trim(), 4);
    if (parts.length < 3 || parts[0] !== NoiseQrPayload.prefix) return null;
    let tier: PrivilegeLevel;
    switch (parts[1]) {
      case 'L':
        tier = 'low';
        break;
      case 'H':
        tier = 'high';
        break;
      default:
        return null;
    }
    const blob = base64UrlDecode(parts[2]);
    if (blob === null || blob.length !== 2 * LinkProtocol.keyLength) return null;
    const appPrivate = blob.slice(0, LinkProtocol.keyLength);
    const devicePublic = blob.slice(LinkProtocol.keyLength);
    if (isZero(appPrivate) || isZero(devicePublic)) return null;
    return new NoiseQrPayload(tier, appPrivate, devicePublic, parts.length === 4 && parts[3].length > 0 ? parts[3] : null);
  }
}

/**
 * An enrollment card: `SGNE1:<base64url 64 bytes>[:<NAME>]`, the blob being
 * the device's enrollment private key followed by the device's public key.
 * Used once, to enroll the phone's own low-tier key, then discarded.
 */
export class NoiseEnrollmentPayload {
  static readonly prefix = 'SGNE1';

  constructor(
    readonly enrollmentPrivateKey: Uint8Array,
    readonly devicePublicKey: Uint8Array,
    readonly deviceName: string | null = null
  ) {}

  encode(): string {
    checkedKeys(this.enrollmentPrivateKey, this.devicePublicKey, 'enrollment');
    const encoded = base64UrlEncode(concatBytes(this.enrollmentPrivateKey, this.devicePublicKey));
    const head = `${NoiseEnrollmentPayload.prefix}:${encoded}`;
    return this.deviceName ? `${head}:${this.deviceName}` : head;
  }

  static parse(text: string): NoiseEnrollmentPayload | null {
    const parts = splitLimited(text.trim(), 3);
    if (parts.length < 2 || parts[0] !== NoiseEnrollmentPayload.prefix) return null;
    const blob = base64UrlDecode(parts[1]);
    if (blob === null || blob.length !== 2 * LinkProtocol.keyLength) return null;
    const enrollmentPrivate = blob.slice(0, LinkProtocol.keyLength);
    const devicePublic = blob.slice(LinkProtocol.keyLength);
    if (isZero(enrollmentPrivate) || isZero(devicePublic)) return null;
    return new NoiseEnrollmentPayload(enrollmentPrivate, devicePublic, parts.length === 3 && parts[2].length > 0 ? parts[2] : null);
  }
}
