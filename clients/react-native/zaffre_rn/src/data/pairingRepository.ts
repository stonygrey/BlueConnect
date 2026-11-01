// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { IoLog, LinkIdentity, LinkSecurity, utf8Decode, utf8Encode, type PrivilegeLevel } from '@stonygrey/blueconnect';

import { Observable } from '../state/observable';
import type { CredentialStore } from './credentialStore';

/** The two privilege tiers, each with its own app identity and pin. */
export type Tier = 'low' | 'high';

export const tiers: readonly Tier[] = ['low', 'high'];

export interface TierInfo {
  readonly privilege: PrivilegeLevel;
  readonly title: string;
  /** The nRF54L15-DK silkscreen label of the confirming button. */
  readonly button: string;
  readonly logName: string;
}

export const tierInfo: Record<Tier, TierInfo> = {
  low: { privilege: 'low', title: 'Low privilege', button: 'Button 2', logName: 'Low' },
  high: { privilege: 'high', title: 'High privilege', button: 'Button 3', logName: 'High' },
};

/** The source of a pin: the button ceremony, a USB-provisioned QR, or enrollment. */
export type CredentialOrigin = 'paired' | 'provisioned' | 'enrolled';

export const originLabels: Record<CredentialOrigin, string> = { paired: 'Paired', provisioned: 'Provisioned', enrolled: 'Enrolled' };

function parseOrigin(value: string | null): CredentialOrigin | null {
  return value === 'paired' || value === 'provisioned' || value === 'enrolled' ? value : null;
}

export interface TierCredential {
  readonly identity: LinkIdentity;
  readonly deviceKey: Uint8Array | null;
  readonly deviceName: string | null;
  readonly origin: CredentialOrigin | null;
}

export function isPaired(credential: TierCredential): boolean {
  return credential.deviceKey !== null;
}

/**
 * Identity and pin storage per tier. The store is asynchronous, so the
 * repository loads after construction and reports `isLoaded`; a missing
 * identity is generated on first use.
 */
export class PairingRepository extends Observable {
  private readonly credentials = new Map<Tier, TierCredential>();

  constructor(private readonly store: CredentialStore) {
    super();
  }

  get isLoaded(): boolean {
    return this.credentials.size === tiers.length;
  }

  credential(tier: Tier): TierCredential | null {
    return this.credentials.get(tier) ?? null;
  }

  async load(): Promise<void> {
    for (const tier of tiers) {
      const identity = await this.loadOrGenerateIdentity(tier);
      const deviceKey = await this.store.get(`zaffre.pair.device.key.${tier}`);
      const deviceName = text(await this.store.get(`zaffre.pair.device.name.${tier}`));
      const origin = parseOrigin(text(await this.store.get(`zaffre.pair.origin.${tier}`)));
      this.credentials.set(tier, { identity, deviceKey, deviceName, origin: deviceKey === null ? null : (origin ?? 'paired') });
      console.log(`ZAFFRE-NOISE-PUB: ${tierInfo[tier].logName} ${IoLog.hex(identity.publicKey)} cert=none.`);
    }
    this.notify();
  }

  /** The security for a normal session: IK against the pin when paired. */
  security(tier: Tier): LinkSecurity {
    const credential = this.credentials.get(tier)!;
    return new LinkSecurity(credential.identity, credential.deviceKey);
  }

  /** The security for the pairing ceremony: XX, any device key accepted. */
  pairingSecurity(tier: Tier): LinkSecurity {
    return new LinkSecurity(this.credentials.get(tier)!.identity, null);
  }

  /** Pins the device key for the tier: after the button ceremony (`paired`) or after `Enroll` (`enrolled`). */
  async record(tier: Tier, deviceKey: Uint8Array, deviceName: string | null, origin: CredentialOrigin = 'paired'): Promise<void> {
    await this.store.set(`zaffre.pair.device.key.${tier}`, deviceKey);
    await this.store.set(`zaffre.pair.device.name.${tier}`, deviceName === null ? null : utf8Encode(deviceName));
    await this.store.set(`zaffre.pair.origin.${tier}`, utf8Encode(origin));
    this.credentials.set(tier, { identity: this.credentials.get(tier)!.identity, deviceKey, deviceName, origin });
    console.log(`${originLabels[origin]} (${tierInfo[tier].logName}) with '${deviceName ?? ''}' (${IoLog.hex(deviceKey)}).`);
    this.notify();
  }

  /** Installs a USB-provisioned credential: the identity from the QR replaces the tier's own, and the device key is pinned. No BLE traffic. */
  async storeProvisioned(tier: Tier, identity: LinkIdentity, deviceKey: Uint8Array, deviceName: string | null): Promise<void> {
    await this.store.set(`zaffre.link.identity.${tier}`, identity.privateKey);
    this.credentials.set(tier, { identity, deviceKey: null, deviceName: null, origin: null });
    await this.record(tier, deviceKey, deviceName, 'provisioned');
  }

  /**
   * Deletes the pin. A provisioned tier also gets a fresh identity, because
   * the one from the QR stays locked into the device's slot until USB
   * possession clears it.
   */
  async clear(tier: Tier): Promise<void> {
    const current = this.credentials.get(tier)!;
    const wasProvisioned = current.origin === 'provisioned';
    await this.store.set(`zaffre.pair.device.key.${tier}`, null);
    await this.store.set(`zaffre.pair.device.name.${tier}`, null);
    await this.store.set(`zaffre.pair.origin.${tier}`, null);
    this.credentials.set(tier, { identity: current.identity, deviceKey: null, deviceName: null, origin: null });
    if (wasProvisioned) {
      await this.store.set(`zaffre.link.identity.${tier}`, null);
      this.credentials.set(tier, { identity: await this.loadOrGenerateIdentity(tier), deviceKey: null, deviceName: null, origin: null });
    }
    this.notify();
  }

  private async loadOrGenerateIdentity(tier: Tier): Promise<LinkIdentity> {
    const key = `zaffre.link.identity.${tier}`;
    const stored = await this.store.get(key);
    if (stored !== null) {
      try {
        return LinkIdentity.fromPrivateKey(stored);
      } catch {
        // A corrupt entry is replaced below.
      }
    }
    const identity = LinkIdentity.generate();
    await this.store.set(key, identity.privateKey);
    console.log(`Generated ${tierInfo[tier].logName} app link identity ${IoLog.hex(identity.publicKey)}.`);
    return identity;
  }
}

function text(bytes: Uint8Array | null): string | null {
  return bytes === null ? null : utf8Decode(bytes);
}
