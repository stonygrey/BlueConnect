// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';

enum ToastSeverity { info, warning, error }

final class Toast {
  const Toast(this.severity, this.text);

  final ToastSeverity severity;
  final String text;
}

/// The transient messages the app shows as snackbars.
final class Toasts {
  final _controller = StreamController<Toast>.broadcast();

  Stream<Toast> get messages => _controller.stream;

  void info(String text) => _controller.add(Toast(ToastSeverity.info, text));

  void warning(String text) => _controller.add(Toast(ToastSeverity.warning, text));

  void error(String text) => _controller.add(Toast(ToastSeverity.error, text));
}

/// The toast text for [error].
String describe(Object error) => error.toString();
