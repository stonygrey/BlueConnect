// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/** The rejection of `withTimeout` when the deadline passes first. */
export class DeadlineExceeded extends Error {
  constructor(readonly timeoutMs: number) {
    super(`Timed out after ${timeoutMs} ms.`);
    this.name = 'DeadlineExceeded';
  }
}

export function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Resolves with `work`, or rejects with `DeadlineExceeded` after `timeoutMs`.
 * JavaScript has no task cancellation, so the work continues after the
 * deadline; its late outcome is observed here and dropped.
 */
export function withTimeout<T>(work: Promise<T>, timeoutMs: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      work.catch(() => undefined);
      reject(new DeadlineExceeded(timeoutMs));
    }, timeoutMs);
    work.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error) => {
        clearTimeout(timer);
        reject(error);
      }
    );
  });
}
