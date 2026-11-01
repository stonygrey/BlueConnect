// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';
import 'dart:collection';

/// A FIFO mutual-exclusion gate for async code: two concurrent
/// `SegmentLink.write` calls must not interleave their segments on the air.
final class AsyncGate {
  bool _locked = false;
  final _waiters = Queue<Completer<void>>();

  Future<void> acquire() {
    if (!_locked) {
      _locked = true;
      return Future.value();
    }
    final waiter = Completer<void>();
    _waiters.add(waiter);
    return waiter.future;
  }

  void release() {
    if (_waiters.isEmpty) {
      _locked = false;
    } else {
      _waiters.removeFirst().complete();
    }
  }

  Future<T> withLock<T>(Future<T> Function() body) async {
    await acquire();
    try {
      return await body();
    } finally {
      release();
    }
  }
}
