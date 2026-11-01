// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/device_selection.dart';
import '../data/pairing_repository.dart';
import '../domain/toasts.dart';
import 'screens.dart';

/// Navigation: the scan list is the home page, and a device's
/// pages are pushed on top once a device is selected, with the app bar's
/// back arrow returning to the list. Toasts are snackbars.
class ZaffreApp extends StatelessWidget {
  const ZaffreApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'Zaffre Flutter',
        theme: ThemeData(colorSchemeSeed: const Color(0xFF0014A8), useMaterial3: true),
        darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF0014A8), brightness: Brightness.dark, useMaterial3: true),
        home: const HomeScreen(),
      );
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  StreamSubscription<Toast>? _toasts;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final messenger = ScaffoldMessenger.of(context);
    _toasts ??= context.read<Toasts>().messages.listen((toast) {
      messenger.hideCurrentSnackBar();
      messenger.showSnackBar(SnackBar(
        key: const ValueKey('toast'),
        content: Text(toast.text),
        backgroundColor: switch (toast.severity) {
          ToastSeverity.info => null,
          ToastSeverity.warning => Colors.orange.shade800,
          ToastSeverity.error => Colors.red.shade800,
        },
        duration: const Duration(seconds: 4),
        showCloseIcon: true,
      ));
    });
  }

  @override
  void dispose() {
    _toasts?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        body: SafeArea(
          child: ScanScreen(onSelect: (device) {
            Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const DevicePage()));
          }),
        ),
      );
}

/// The selected device's pages: the Noise trust tab, the Low and High
/// command tabs, and the Loop tab, under the device's name.
class DevicePage extends StatefulWidget {
  const DevicePage({super.key});

  @override
  State<DevicePage> createState() => _DevicePageState();
}

class _DevicePageState extends State<DevicePage> {
  int _tab = 0;

  @override
  Widget build(BuildContext context) {
    final selected = context.watch<DeviceSelection>().value;
    return Scaffold(
      appBar: AppBar(title: Text(selected?.localName ?? '', key: const ValueKey('device-heading'))),
      body: switch (_tab) {
        0 => const NoiseScreen(),
        1 => const CommandsScreen(Tier.low),
        2 => const CommandsScreen(Tier.high),
        _ => const LoopScreen(),
      },
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: (index) => setState(() => _tab = index),
        destinations: const [
          NavigationDestination(key: ValueKey('tab-noise'), icon: Icon(Icons.lock), label: 'Noise'),
          NavigationDestination(key: ValueKey('tab-low'), icon: Icon(Icons.arrow_downward), label: 'Low'),
          NavigationDestination(key: ValueKey('tab-high'), icon: Icon(Icons.arrow_upward), label: 'High'),
          NavigationDestination(key: ValueKey('tab-loop'), icon: Icon(Icons.repeat), label: 'Loop'),
        ],
      ),
    );
  }
}
