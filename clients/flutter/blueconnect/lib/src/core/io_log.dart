// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'segment_header.dart';

/// The device I/O log lines, in the exact format `lib/smalt` parses. Lines go
/// through [sink], which defaults to `print` so that `flutter logs` and
/// logcat carry them; the app may replace it. Android's logcat truncates a
/// line near 4 KB, so the largest frames lose the tail of their hex there.
abstract final class IoLog {
  static const prefix = '_BLE_IO';

  static void Function(String line) sink = print;

  static void info(String line) => sink(line);

  static void warn(String line) => sink(line);

  static void debug(String line) => sink(line);

  static String message(String direction, int type, Uint8List bytes) =>
      '$prefix message $direction type=${(type & 0xFF).toRadixString(16).padLeft(2, '0')} len=${bytes.length} hex=${hex(bytes)}';

  static String segment(String direction, int seq, int flags, Uint8List bytes) =>
      '$prefix segment $direction seq=$seq flags=${formatFlags(flags)} len=${bytes.length} hex=${hex(bytes)}';

  static String formatFlags(int flags) {
    final parts = <String>[];
    if (flags & SegmentHeader.start != 0) parts.add('start');
    if (flags & SegmentHeader.end != 0) parts.add('end');
    if (flags & SegmentHeader.abort != 0) parts.add('abort');
    return parts.isEmpty ? 'none' : parts.join('+');
  }

  static const _digits = '0123456789ABCDEF';

  /// Uppercase hex with no separators, as `Convert.ToHexString` produces.
  static String hex(List<int> bytes) {
    final out = StringBuffer();
    for (final b in bytes) {
      out.write(_digits[(b >> 4) & 0x0F]);
      out.write(_digits[b & 0x0F]);
    }
    return out.toString();
  }
}
