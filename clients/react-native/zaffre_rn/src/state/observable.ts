// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { useSyncExternalStore } from 'react';

/**
 * A plain object with mutable fields that tells React when they change: the
 * external-store pattern React 18 added `useSyncExternalStore` for. Each
 * `notify` bumps a version; a component that calls `useObservable` on the
 * object re-renders when the version moves and reads the fields directly.
 */
export class Observable {
  private readonly listeners = new Set<() => void>();
  private version = 0;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  readonly snapshot = (): number => this.version;

  protected notify(): void {
    this.version++;
    for (const listener of Array.from(this.listeners)) {
      listener();
    }
  }
}

/** Re-renders the calling component whenever `store` notifies. */
export function useObservable(store: Observable): void {
  useSyncExternalStore(store.subscribe, store.snapshot, store.snapshot);
}
