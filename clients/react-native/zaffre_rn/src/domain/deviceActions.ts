// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { utf8Decode, utf8Encode } from '@stonygrey/blueconnect';

import type { DeviceSelection } from '../data/deviceSelection';
import type { PairingRepository, Tier } from '../data/pairingRepository';
import type { CommandRunner } from './commandRunner';
import type { SoakLoop } from './soakLoop';
import type { Toasts } from './toasts';

/**
 * The actions behind the Noise, Low, and High tabs, on the selected device:
 * pairing and the per-tier commands, with the toasts for their results. The
 * screens hold the text inputs; this class holds no view state.
 */
export class DeviceActions {
  constructor(
    private readonly selection: DeviceSelection,
    private readonly pairings: PairingRepository,
    private readonly commands: CommandRunner,
    private readonly loop: SoakLoop,
    private readonly toasts: Toasts
  ) {}

  /** Commands and pairing are allowed when a device is selected, the credentials are loaded, nothing else is running, and the loop is stopped. */
  get canRun(): boolean {
    return this.selection.value !== null && this.pairings.isLoaded && !this.commands.isBusy && !this.loop.isRunning;
  }

  pair(tier: Tier): Promise<void> {
    return this.commands.pair(this.selection.value, tier);
  }

  unpair(tier: Tier): Promise<void> {
    return this.commands.unpair(this.selection.value, tier);
  }

  /** A scanned SGNK1 or SGNE1 payload for the tier's card. */
  provision(tier: Tier, text: string): Promise<void> {
    return this.commands.provision(this.selection.value, tier, text);
  }

  async readSerial(tier: Tier): Promise<void> {
    const name = await this.commands.run(this.selection.value, tier, (v) => v.getSerialNumber());
    if (name !== null) this.toasts.info(`Advertising as '${name}'`);
  }

  /** True when the device accepted the serial number, so the input can move on to a fresh random value. */
  async writeSerial(tier: Tier, input: string): Promise<boolean> {
    const serial = Number(input.trim());
    if (!Number.isInteger(serial) || serial < 1 || serial > 65535) {
      this.toasts.error('Serial number must be a whole number from 1 to 65535.');
      return false;
    }
    const name = await this.commands.run(this.selection.value, tier, (v) => v.setSerialNumber(serial));
    if (name === null) return false;
    this.toasts.info(`Now advertising as '${name}'`);
    return true;
  }

  readLed(tier: Tier): Promise<void> {
    return this.commands.readLed(this.selection.value, tier);
  }

  tapLed(tier: Tier): Promise<void> {
    return this.commands.tapLed(this.selection.value, tier);
  }

  async echo(tier: Tier, text: string): Promise<void> {
    const reply = await this.commands.run(this.selection.value, tier, (v) => v.echo(utf8Encode(text)));
    if (reply !== null) {
      const echoed = utf8Decode(reply);
      if (echoed.length > 0) this.toasts.info(echoed);
    }
  }
}

export function randomSerial(): string {
  return String(1 + Math.floor(Math.random() * 65535));
}
