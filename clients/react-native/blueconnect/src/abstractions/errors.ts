// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/**
 * The failures the library reports. The messages are written for the app's
 * toasts.
 */
export class BlueConnectError extends Error {
  constructor(
    message: string,
    readonly cause?: unknown
  ) {
    super(message);
    this.name = 'BlueConnectError';
  }
}

/** The GATT layer or the segment layer failed. */
export class BleTransportError extends BlueConnectError {
  constructor(message: string, cause?: unknown) {
    super(message, cause);
    this.name = 'BleTransportError';
  }
}

/** The handshake or an AEAD check failed. */
export class LinkSecurityError extends BlueConnectError {
  constructor(message: string, cause?: unknown) {
    super(message, cause);
    this.name = 'LinkSecurityError';
  }
}

/** A connect attempt, retry window, or command ran out of time. */
export class BleTimeoutError extends BlueConnectError {
  constructor(message: string, cause?: unknown) {
    super(message, cause);
    this.name = 'BleTimeoutError';
  }
}

/** A call the current state does not allow. */
export class InvalidStateError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'InvalidStateError';
  }
}
