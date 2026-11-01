// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import '../abstractions/blueconnect_options.dart';
import '../abstractions/exceptions.dart';
import 'io_log.dart';

/// The connect policy: attempts bounded by `connectTimeout`, repeated after
/// a 1 s delay until `connectRetryTimeout` runs out, and abandoned early when
/// the device has stopped advertising and `retryOnlyWhileAdvertising` is set.
/// Backends call it from `connect()` with their single-attempt connect as
/// [attempt].
abstract final class ConnectRetry {
  static const retryDelay = Duration(seconds: 1);

  /// [attempt] receives the time it may take; [isPresent] reports whether the
  /// scanner has seen the device within the expiry window.
  static Future<void> run({
    required BlueConnectOptions options,
    required String name,
    required bool Function() isPresent,
    required Future<void> Function(Duration timeout) attempt,
  }) async {
    final unbounded = options.connectRetryTimeout <= Duration.zero;
    final window = options.connectRetryTimeout;
    final attemptTimeout = options.connectTimeout;
    final started = Stopwatch()..start();
    var attemptNumber = 1;
    while (true) {
      final remaining = unbounded ? attemptTimeout : window - started.elapsed;
      try {
        await attempt(attemptTimeout < remaining ? attemptTimeout : remaining);
        return;
      } catch (e) {
        final elapsed = started.elapsed;
        if (!unbounded && elapsed + retryDelay >= window) {
          throw BleTimeoutException(
            "Connecting to '$name' did not succeed within ${window.inSeconds}s "
            '($attemptNumber attempt(s); last failure: $e)',
            e,
          );
        }
        IoLog.warn("Connect attempt $attemptNumber to '$name' failed after "
            '${(elapsed.inMilliseconds / 1000).toStringAsFixed(1)}s ($e); retrying.');
        await Future<void>.delayed(retryDelay);
        if (options.retryOnlyWhileAdvertising && !isPresent()) {
          throw BleTransportException(
            "'$name' is no longer advertising; giving up after $attemptNumber attempt(s) (last failure: $e)",
            e,
          );
        }
      }
      attemptNumber++;
    }
  }
}
