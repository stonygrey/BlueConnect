// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';
import 'dart:collection';
import 'dart:typed_data';

import '../abstractions/exceptions.dart';

/// The inbound ATT value queue behind `BlePeripheral.read`: notifications are
/// pushed from the platform stream, and one reader pops them in order. A
/// [pop] with a timeout withdraws its waiter when the timeout elapses, so a
/// late value is kept for the next reader instead of being lost to a future
/// nobody awaits.
final class ReceiveQueue {
  final _items = Queue<Uint8List>();
  Completer<Uint8List?>? _waiter;
  Timer? _timer;
  bool _closed = false;

  void push(Uint8List value) {
    final waiter = _waiter;
    if (waiter != null) {
      _waiter = null;
      _timer?.cancel();
      waiter.complete(value);
      return;
    }
    if (!_closed) {
      _items.add(value);
    }
  }

  /// Fails the pending and every future [pop] with [error].
  void close(Object error) {
    _closed = true;
    final waiter = _waiter;
    _waiter = null;
    _timer?.cancel();
    waiter?.completeError(error);
  }

  /// The next value, or null when [timeout] passes first.
  Future<Uint8List?> pop({Duration? timeout}) {
    if (_items.isNotEmpty) {
      return Future.value(_items.removeFirst());
    }
    if (_closed) {
      return Future.error(BleTransportException('The peripheral has been closed.'));
    }
    if (_waiter != null) {
      return Future.error(StateError('ReceiveQueue has one reader at a time.'));
    }
    final waiter = Completer<Uint8List?>();
    _waiter = waiter;
    if (timeout != null) {
      _timer = Timer(timeout, () {
        if (identical(_waiter, waiter)) {
          _waiter = null;
          waiter.complete(null);
        }
      });
    }
    return waiter.future;
  }
}
