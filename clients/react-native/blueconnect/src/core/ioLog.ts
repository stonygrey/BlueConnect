// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { SegmentHeader } from './segmentHeader';

const digits = '0123456789ABCDEF';

/**
 * The device I/O log lines, in the exact format `lib/smalt` parses. Lines go
 * through `sink`, which defaults to `console.log` so that the Metro terminal
 * and the device log carry them; the app may replace it.
 */
export const IoLog = {
  prefix: '_BLE_IO',

  sink: (line: string): void => {
    console.log(line);
  },

  info(line: string): void {
    IoLog.sink(line);
  },

  warn(line: string): void {
    IoLog.sink(line);
  },

  debug(line: string): void {
    IoLog.sink(line);
  },

  message(direction: string, type: number, bytes: Uint8Array): string {
    return `${IoLog.prefix} message ${direction} type=${(type & 0xff).toString(16).padStart(2, '0')} len=${bytes.length} hex=${IoLog.hex(bytes)}`;
  },

  segment(direction: string, seq: number, flags: number, bytes: Uint8Array): string {
    return `${IoLog.prefix} segment ${direction} seq=${seq} flags=${IoLog.formatFlags(flags)} len=${bytes.length} hex=${IoLog.hex(bytes)}`;
  },

  formatFlags(flags: number): string {
    const parts: string[] = [];
    if (flags & SegmentHeader.start) parts.push('start');
    if (flags & SegmentHeader.end) parts.push('end');
    if (flags & SegmentHeader.abort) parts.push('abort');
    return parts.length === 0 ? 'none' : parts.join('+');
  },

  /** Uppercase hex with no separators, as `Convert.ToHexString` produces. */
  hex(bytes: Uint8Array): string {
    let out = '';
    for (const b of bytes) {
      out += digits[(b >> 4) & 0x0f] + digits[b & 0x0f];
    }
    return out;
  },
};
