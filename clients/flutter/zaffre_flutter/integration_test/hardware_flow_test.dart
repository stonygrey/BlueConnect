// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:zaffre_flutter/main.dart' as app;

/// Drives the app against the Verdigris DK over the real radio: pair both
/// tiers, run the High and Low commands, soak three loop cycles, and unpair.
/// It needs the board advertising, the Bluetooth permissions granted to the
/// app, and a watcher on the device log that runs `lib/device confirm` over
/// the board's USB shell when the app prints `ZAFFRE-PAIR: armed <tier>`
/// (see `documents/native-clients.md`). The spinner on the scan page never
/// settles, so the helpers pump on a fixed cadence instead of `pumpAndSettle`.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('pair, commands, loop, and unpair', (tester) async {
    await tester.pumpWidget(app.buildApp());

    // Scan: the board is the only Verdigris device in range.
    await waitFor(tester, find.byKey(const ValueKey('device-row')), const Duration(seconds: 60));
    await tester.tap(find.byKey(const ValueKey('device-row')).first);
    // The device's pages are pushed on top of the list; let the route settle.
    await waitFor(tester, find.byKey(const ValueKey('tab-noise')), const Duration(seconds: 10));
    for (var i = 0; i < 4; i++) {
      await pump(tester);
    }

    // Pair High, then Low; the confirm comes over USB from the watcher. A
    // tier the app still holds from earlier use is unpaired first.
    await tester.tap(find.byKey(const ValueKey('tab-noise')));
    await pump(tester);
    for (final tier in ['high', 'low']) {
      if (find.byKey(ValueKey('unpair-$tier')).evaluate().isNotEmpty) {
        await waitEnabled(tester, 'unpair-$tier');
        await tapKey(tester, 'unpair-$tier');
        await waitForText(tester, 'status-$tier', 'Not paired', const Duration(seconds: 45));
      }
      await waitEnabled(tester, 'pair-$tier');
      await tapKey(tester, 'pair-$tier');
      await waitForText(tester, 'status-$tier', 'Sessions are encrypted', const Duration(seconds: 45));
    }

    // High: serial read and write, LED read and toggle, echo.
    await tester.tap(find.byKey(const ValueKey('tab-high')));
    await pump(tester);
    await tapAndExpectToast(tester, 'serial-read-high', 'Advertising as');
    await tapAndExpectToast(tester, 'serial-write-high', 'Now advertising as');
    await waitEnabled(tester, 'led-high');
    await tapKey(tester, 'led-high');
    await waitForText(tester, 'led-label-high', 'Tap to switch', const Duration(seconds: 45));
    final before = textOf(tester, 'led-label-high');
    await waitEnabled(tester, 'led-high');
    await tapKey(tester, 'led-high');
    await waitUntil(tester, () => textOf(tester, 'led-label-high') != before && textOf(tester, 'led-label-high').contains('Tap to switch'), const Duration(seconds: 45));
    await tapAndExpectToast(tester, 'echo-write-high', 'Hello, Verdigris!');

    // Low: reads work, echo is refused by the device.
    await tester.tap(find.byKey(const ValueKey('tab-low')));
    await pump(tester);
    await tapAndExpectToast(tester, 'serial-read-low', 'Advertising as');
    await tapAndExpectToast(tester, 'echo-write-low', 'STATUS_PERMISSION_DENIED');

    // Loop: three cycles with no failures.
    await tester.tap(find.byKey(const ValueKey('tab-loop')));
    await pump(tester);
    await waitEnabled(tester, 'loop-start');
    await tapKey(tester, 'loop-start');
    await waitUntil(tester, () => (int.tryParse(textOf(tester, 'loop-tests')) ?? 0) >= 3, const Duration(seconds: 180));
    await tapKey(tester, 'loop-stop');
    await waitEnabled(tester, 'loop-start', const Duration(seconds: 60));
    expect(textOf(tester, 'loop-connection-failures'), '0');
    expect(textOf(tester, 'loop-command-failures'), '0');

    // Unpair both tiers.
    await tester.tap(find.byKey(const ValueKey('tab-noise')));
    await pump(tester);
    for (final tier in ['high', 'low']) {
      await waitEnabled(tester, 'unpair-$tier');
      await tapKey(tester, 'unpair-$tier');
      await waitForText(tester, 'status-$tier', 'Not paired', const Duration(seconds: 45));
    }
  });
}

Future<void> pump(WidgetTester tester) => tester.pump(const Duration(milliseconds: 300));

/// Taps the widget with [key] after scrolling it into view: a tap on a
/// widget below the fold of a scrolling page is dropped.
Future<void> tapKey(WidgetTester tester, String key) async {
  final finder = find.byKey(ValueKey(key));
  await tester.ensureVisible(finder);
  await pump(tester);
  await tester.tap(finder);
}

Future<void> waitUntil(WidgetTester tester, bool Function() condition, Duration timeout) async {
  final deadline = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(deadline)) {
    await pump(tester);
    if (condition()) return;
  }
  fail('condition not met within $timeout');
}

Future<void> waitFor(WidgetTester tester, Finder finder, Duration timeout) => waitUntil(tester, () => finder.evaluate().isNotEmpty, timeout);

bool isEnabled(WidgetTester tester, String key) {
  final finder = find.byKey(ValueKey(key));
  if (finder.evaluate().isEmpty) return false;
  final widget = tester.widget(finder);
  return switch (widget) {
    ButtonStyleButton b => b.onPressed != null,
    IconButton b => b.onPressed != null,
    InkWell w => w.onTap != null,
    _ => true,
  };
}

Future<void> waitEnabled(WidgetTester tester, String key, [Duration timeout = const Duration(seconds: 60)]) =>
    waitUntil(tester, () => isEnabled(tester, key), timeout);

String textOf(WidgetTester tester, String key) {
  final finder = find.byKey(ValueKey(key));
  if (finder.evaluate().isEmpty) return '';
  return (tester.widget(finder) as Text).data ?? '';
}

Future<void> waitForText(WidgetTester tester, String key, String text, Duration timeout) =>
    waitUntil(tester, () => textOf(tester, key).contains(text), timeout);

Future<void> tapAndExpectToast(WidgetTester tester, String key, String text) async {
  await waitEnabled(tester, key);
  await tapKey(tester, key);
  await waitUntil(tester, () => find.textContaining(text).evaluate().isNotEmpty, const Duration(seconds: 45));
}
