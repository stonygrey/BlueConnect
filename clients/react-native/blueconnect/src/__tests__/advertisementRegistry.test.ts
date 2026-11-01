// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { ScanEvent } from '../abstractions/bleScanner';
import { AdvertisementRegistry } from '../core/advertisementRegistry';

/** The registry behaviour: merge, RSSI bounds, expiry, and the connection hold. */
describe('AdvertisementRegistry', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  const sample = (overrides: Partial<Parameters<AdvertisementRegistry['observe']>[0]> = {}) => ({
    id: 'a',
    localName: 'Verdigris 1',
    isConnectable: true,
    secured: null,
    dbm: -60,
    ...overrides,
  });

  test('first packet adds, later packets update and keep the status when absent', () => {
    const registry = new AdvertisementRegistry(-127, 20);
    const events: ScanEvent[] = [];
    registry.onEvent((e) => events.push(e));
    registry.observe(sample({ secured: true }));
    registry.observe(sample({ dbm: -50 }));
    expect(events.map((e) => e.type)).toEqual(['added', 'updated']);
    const latest = registry.cached('a')!;
    expect(latest.isSecured).toBe(true);
    expect(latest.rssi).toEqual({ valueDbm: -50, minDbm: -60, maxDbm: -50 });
    registry.close();
  });

  test('samples outside the bounds keep the previous rssi', () => {
    const registry = new AdvertisementRegistry(-127, 20);
    registry.observe(sample({ dbm: -60 }));
    registry.observe(sample({ dbm: 127 }));
    expect(registry.cached('a')!.rssi.valueDbm).toBe(-60);
    registry.close();
  });

  test('entries expire after the scanner window unless a connection holds them', () => {
    const registry = new AdvertisementRegistry(-127, 20);
    const events: ScanEvent[] = [];
    registry.onEvent((e) => events.push(e));
    registry.observe(sample({ id: 'held' }));
    registry.observe(sample({ id: 'loose' }));
    registry.register('held');
    jest.advanceTimersByTime(66_000);
    expect(events.filter((e) => e.type === 'expired').map((e) => e.advertisement.id)).toEqual(['loose']);
    expect(registry.cached('held')).not.toBeNull();
    expect(registry.lastSeen('loose')).toBeNull();
    registry.close();
  });
});
