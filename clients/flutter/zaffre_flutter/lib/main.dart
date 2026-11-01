// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'data/credential_store.dart';
import 'data/device_selection.dart';
import 'data/pairing_repository.dart';
import 'domain/command_runner.dart';
import 'domain/soak_loop.dart';
import 'domain/toasts.dart';
import 'ui/app.dart';
import 'ui/view_models.dart';

/// The Flutter client of the Verdigris device. The object graph is built
/// here and handed to the widget tree through `provider`, as Flutter's
/// architecture guide lays out: data layer, domain objects, view models.
void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(buildApp());
}

Widget buildApp() {
  const options = BlueConnectOptions();
  final scanner = BackendScanner(options: options);
  final selection = DeviceSelection();
  final pairings = PairingRepository(SecureCredentialStore());
  final toasts = Toasts();
  final commands = CommandRunner(scanner, options, pairings, toasts);
  final loop = SoakLoop(scanner, options, pairings);
  return MultiProvider(
    providers: [
      Provider<Toasts>.value(value: toasts),
      ChangeNotifierProvider<PairingRepository>.value(value: pairings),
      ChangeNotifierProvider<DeviceSelection>.value(value: selection),
      ChangeNotifierProvider<CommandRunner>.value(value: commands),
      ChangeNotifierProvider<SoakLoop>.value(value: loop),
      ChangeNotifierProvider(create: (_) => ScanViewModel(scanner, selection)..start()),
      ChangeNotifierProvider(create: (_) => NoiseViewModel(selection, pairings, commands, loop, toasts)),
      ChangeNotifierProvider(create: (_) => LoopViewModel(selection, loop)),
    ],
    child: const ZaffreApp(),
  );
}
