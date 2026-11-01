// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement } from '@stonygrey/blueconnect';

import { Observable } from '../state/observable';

/** The device the user tapped on the scan list, shared by every screen. */
export class DeviceSelection extends Observable {
  value: Advertisement | null = null;

  select(advertisement: Advertisement | null): void {
    this.value = advertisement;
    this.notify();
  }

  /** Replaces the selection with a fresher advertisement of the same device. */
  refresh(advertisements: Advertisement[]): void {
    const current = this.value;
    if (current === null) return;
    const fresh = advertisements.find((a) => a.id === current.id);
    if (fresh && fresh !== current) {
      this.value = fresh;
      this.notify();
    }
  }
}
