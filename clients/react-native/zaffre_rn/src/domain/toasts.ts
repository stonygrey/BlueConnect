// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

export type ToastSeverity = 'info' | 'warning' | 'error';

export interface Toast {
  readonly severity: ToastSeverity;
  readonly text: string;
}

/** The transient messages the app shows as snackbars. */
export class Toasts {
  private readonly listeners = new Set<(toast: Toast) => void>();

  onMessage(listener: (toast: Toast) => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  info(text: string): void {
    this.emit({ severity: 'info', text });
  }

  warning(text: string): void {
    this.emit({ severity: 'warning', text });
  }

  error(text: string): void {
    this.emit({ severity: 'error', text });
  }

  private emit(toast: Toast): void {
    for (const listener of Array.from(this.listeners)) {
      listener(toast);
    }
  }
}

/** The toast text for `error`. */
export function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
