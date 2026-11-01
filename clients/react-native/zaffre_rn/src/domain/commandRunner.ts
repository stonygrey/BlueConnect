// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import {
  LinkIdentity,
  LinkSecurity,
  NoiseEnrollmentPayload,
  NoiseQrPayload,
  Status,
  VerdigrisError,
  VerdigrisPeripheral,
  type Advertisement,
  type BleScanner,
  type BlueConnectOptions,
} from '@stonygrey/blueconnect';

import { tierInfo, type PairingRepository, type Tier } from '../data/pairingRepository';
import { Observable } from '../state/observable';
import { describe, type Toasts } from './toasts';

/**
 * Runs one Verdigris command at a time: each command opens its own
 * connection, runs, disconnects, and releases the peripheral.
 */
export class CommandRunner extends Observable {
  private busy = false;
  private readonly led = new Map<Tier, boolean>();
  private readonly results = new Map<Tier, string>();

  constructor(
    private readonly scanner: BleScanner,
    private readonly options: BlueConnectOptions,
    private readonly pairings: PairingRepository,
    private readonly toasts: Toasts
  ) {
    super();
  }

  get isBusy(): boolean {
    return this.busy;
  }

  /** LED 1 as last reported by the device, per tier page; null until read. */
  ledOn(tier: Tier): boolean | null {
    return this.led.get(tier) ?? null;
  }

  /** The pairing card's result line. */
  pairResult(tier: Tier): string {
    return this.results.get(tier) ?? '';
  }

  /** Connect, run `body`, disconnect, close. The connection is never kept between commands. */
  private async session<T>(device: Advertisement, security: LinkSecurity, body: (v: VerdigrisPeripheral) => Promise<T>): Promise<T> {
    const peripheral = this.scanner.createPeripheral(device);
    const verdigris = new VerdigrisPeripheral(peripheral, this.options, security);
    try {
      await peripheral.connect();
      const result = await body(verdigris);
      await peripheral.disconnect();
      return result;
    } finally {
      await verdigris.close();
    }
  }

  /** A command on the tier's pinned session. A failure becomes an error toast, and null is returned. */
  async run<T>(device: Advertisement | null, tier: Tier, body: (v: VerdigrisPeripheral) => Promise<T>): Promise<T | null> {
    if (device === null || this.busy) return null;
    this.setBusy(true);
    try {
      return await this.session(device, this.pairings.security(tier), body);
    } catch (e) {
      console.log(`Verdigris command failed against '${device.localName}': ${describe(e)}`);
      this.toasts.error(describe(e));
      return null;
    } finally {
      this.setBusy(false);
    }
  }

  async readLed(device: Advertisement | null, tier: Tier): Promise<void> {
    const on = await this.run(device, tier, (v) => v.getLed());
    if (on !== null) {
      this.led.set(tier, on);
      this.notify();
    }
  }

  /** Tap on the LED: read it when unknown, otherwise switch it. */
  async tapLed(device: Advertisement | null, tier: Tier): Promise<void> {
    const current = this.led.get(tier);
    if (current === undefined) {
      await this.readLed(device, tier);
      return;
    }
    const on = await this.run(device, tier, (v) => v.setLed(!current));
    if (on !== null) {
      this.led.set(tier, on);
      this.notify();
    }
  }

  /** The ceremony: an XX session that accepts any device key, `Pair`, and the button within 30 s. On Ack the proven device key is pinned. */
  async pair(device: Advertisement | null, tier: Tier): Promise<void> {
    if (device === null || this.busy) return;
    this.setBusy(true);
    this.results.set(tier, `Press ${tierInfo[tier].button} on the device to confirm…`);
    this.notify();
    try {
      const key = await this.session(device, this.pairings.pairingSecurity(tier), (v) => {
        // The marker a hardware test watches for to confirm over USB.
        console.log(`ZAFFRE-PAIR: armed ${tier}`);
        return v.pair(tierInfo[tier].privilege);
      });
      await this.pairings.record(tier, key, device.localName);
      this.results.set(tier, '');
    } catch (e) {
      if (e instanceof VerdigrisError && e.status === Status.UNAUTHENTICATED) {
        this.results.set(
          tier,
          e.detail !== null ? `Pairing not confirmed: ${e.detail}` : `Pairing not confirmed. Press ${tierInfo[tier].button} on the device within 30 seconds and try again.`
        );
      } else {
        this.results.set(tier, '');
        this.toasts.error(describe(e));
      }
    } finally {
      this.setBusy(false);
    }
  }

  /**
   * A scanned credential for the tier's card: an `SGNE1` card enrolls the
   * low tier over the air, an `SGNK1` credential is installed without any
   * traffic, and anything else is refused.
   */
  async provision(device: Advertisement | null, tier: Tier, text: string): Promise<void> {
    const trimmed = text.trim();
    if (trimmed.length === 0) {
      this.toasts.error('Scan the SGNK1 payload first.');
      return;
    }
    const card = NoiseEnrollmentPayload.parse(trimmed);
    if (card !== null) {
      if (tier !== 'low') {
        this.toasts.warning('An enrollment QR enrolls the low tier — scan it on the low card.');
        return;
      }
      if (device === null) {
        this.toasts.error('Select the device first.');
        return;
      }
      await this.enroll(device, card);
      return;
    }
    const credential = NoiseQrPayload.parse(trimmed);
    if (credential === null) {
      this.toasts.error('That is not a BlueConnect Noise credential (SGNK1) or enrollment (SGNE1) payload.');
      return;
    }
    if (credential.tier !== tier) {
      this.toasts.warning(`That QR is the ${credential.tier} Noise credential — scan it on the ${credential.tier} card.`);
      return;
    }
    try {
      const identity = LinkIdentity.fromPrivateKey(credential.appPrivateKey);
      await this.pairings.storeProvisioned(tier, identity, credential.devicePublicKey, credential.deviceName);
    } catch (e) {
      this.toasts.error(describe(e));
    }
  }

  /**
   * One IK session as the enrollment key, with the device key from the card
   * pinned, carrying `Enroll` with the low identity's public key. The
   * enrollment key is used for this call and never stored.
   */
  private async enroll(device: Advertisement, card: NoiseEnrollmentPayload): Promise<void> {
    if (this.busy) return;
    this.setBusy(true);
    try {
      const enrollmentIdentity = LinkIdentity.fromPrivateKey(card.enrollmentPrivateKey);
      const security = new LinkSecurity(enrollmentIdentity, card.devicePublicKey);
      const lowPublicKey = this.pairings.credential('low')!.identity.publicKey;
      await this.session(device, security, (v) => v.enroll(lowPublicKey));
      await this.pairings.record('low', card.devicePublicKey, card.deviceName ?? device.localName, 'enrolled');
    } catch (e) {
      console.log(`Enrollment failed against '${device.localName}': ${describe(e)}`);
      this.toasts.error(describe(e));
    } finally {
      this.setBusy(false);
    }
  }

  /** `Unpair` on the pinned session, then the local pin is cleared even if the device call failed. */
  async unpair(device: Advertisement | null, tier: Tier): Promise<void> {
    if (device === null || this.busy) return;
    const credential = this.pairings.credential(tier);
    if (credential === null || credential.deviceKey === null) {
      this.toasts.error('Not paired.');
      return;
    }
    this.setBusy(true);
    try {
      await this.session(device, this.pairings.security(tier), (v) => v.unpair());
    } catch (e) {
      console.log(`Device-side unpair (${tierInfo[tier].logName}) failed; clearing the local pairing anyway: ${describe(e)}`);
    } finally {
      this.setBusy(false);
    }
    await this.pairings.clear(tier);
    this.results.set(tier, '');
    this.notify();
  }

  private setBusy(value: boolean): void {
    this.busy = value;
    this.notify();
  }
}
