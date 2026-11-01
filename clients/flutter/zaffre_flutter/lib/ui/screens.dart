// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/build_info.dart';
import '../data/device_selection.dart';
import '../data/pairing_repository.dart';
import '../domain/command_runner.dart';
import '../domain/soak_loop.dart';
import 'qr_scan_page.dart';
import 'view_models.dart';

/// The scan list: name, identifier, padlock, and RSSI,
/// strongest first. Tapping a row selects it and hands it to [onSelect],
/// which opens the device's pages.
class ScanScreen extends StatelessWidget {
  const ScanScreen({required this.onSelect, super.key});

  final void Function(Advertisement device) onSelect;

  @override
  Widget build(BuildContext context) {
    final scan = context.watch<ScanViewModel>();
    final selected = context.watch<DeviceSelection>().value;
    final theme = Theme.of(context);
    return Stack(children: [
      Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(padding: const EdgeInsets.all(16), child: Text('Select a device', style: Theme.of(context).textTheme.headlineSmall)),
        Expanded(
          child: scan.devices.isEmpty
              ? Center(
                  child: scan.scanFailure != null
                      ? Text(scan.scanFailure!)
                      : const Column(mainAxisSize: MainAxisSize.min, children: [CircularProgressIndicator(), SizedBox(height: 8), Text('Scanning for Verdigris devices…')]),
                )
              : ListView(
                  children: [
                    for (final device in scan.devices)
                      Card(
                        key: const ValueKey('device-row'),
                        child: ListTile(
                          title: Text(device.localName),
                          subtitle: Text(device.id),
                          trailing: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(device.isSecured ? Icons.lock : Icons.lock_open, color: device.isSecured ? Colors.green : null),
                              if (!device.isConnectable) const Text('not connectable', style: TextStyle(color: Colors.orange)),
                              Padding(padding: const EdgeInsets.only(left: 8), child: Text('${device.rssi.valueDbm} dBm')),
                              if (device.id == selected?.id) const Icon(Icons.check),
                            ],
                          ),
                          onTap: () {
                            scan.select(device);
                            onSelect(device);
                          },
                        ),
                      ),
                  ],
                ),
        ),
      ],
      ),
      Positioned(
        left: 12,
        bottom: 6,
        child: IgnorePointer(
          child: FutureBuilder<String>(
            future: BuildInfo.label,
            builder: (context, snapshot) => Text(
              snapshot.data ?? '',
              key: const ValueKey('build-label'),
              style: theme.textTheme.labelSmall?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
          ),
        ),
      ),
    ]);
  }
}

/// The trust tab: one pairing card per tier, High first.
class NoiseScreen extends StatelessWidget {
  const NoiseScreen({super.key});

  @override
  Widget build(BuildContext context) => ListView(
        padding: const EdgeInsets.all(16),
        children: const [
          SizedBox(height: 8),
          Text('Noise XX link security; each tier is enabled independently.'),
          Text("Pair: Mutual key pinning, confirmed with the board's button.", style: TextStyle(fontSize: 12)),
          SizedBox(height: 8),
          PairingCard(Tier.high),
          PairingCard(Tier.low),
        ],
      );
}

class PairingCard extends StatelessWidget {
  const PairingCard(this.tier, {super.key});

  final Tier tier;

  @override
  Widget build(BuildContext context) {
    final noise = context.watch<NoiseViewModel>();
    final pairings = context.watch<PairingRepository>();
    final commands = context.watch<CommandRunner>();
    context.watch<SoakLoop>();
    final selected = context.watch<DeviceSelection>().value;
    final credential = pairings.credential(tier);
    if (credential == null) {
      return const Card(child: Padding(padding: EdgeInsets.all(16), child: Text('Loading credentials…')));
    }
    final canRun = noise.canRun;
    final state = credential.isPaired ? (credential.origin ?? CredentialOrigin.paired).label : 'Unsecured';
    final result = commands.pairResult(tier);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('${tier.title}: $state', key: ValueKey('title-${tier.key}'), style: Theme.of(context).textTheme.titleMedium),
            Text(_status(credential, selected), key: ValueKey('status-${tier.key}'), style: const TextStyle(fontSize: 12)),
            const SizedBox(height: 8),
            if (credential.isPaired)
              OutlinedButton(key: ValueKey('unpair-${tier.key}'), onPressed: canRun ? () => noise.unpair(tier) : null, child: const Text('Unpair'))
            else
              Row(children: [
                FilledButton(key: ValueKey('pair-${tier.key}'), onPressed: canRun ? () => noise.pair(tier) : null, child: const Text('Pair')),
                const SizedBox(width: 12),
                Text('Confirm with ${tier.button}', style: const TextStyle(fontSize: 12)),
              ]),
              const Divider(),
              Row(children: [
                OutlinedButton.icon(
                  key: ValueKey('scan-${tier.key}'),
                  onPressed: canRun
                      ? () async {
                          final text = await Navigator.of(context).push(MaterialPageRoute<String>(builder: (_) => const QrScanPage()));
                          if (text != null) await noise.provision(tier, text);
                        }
                      : null,
                  icon: const Icon(Icons.qr_code_scanner),
                  label: const Text('Scan QR'),
                ),
                const SizedBox(width: 12),
                const Expanded(child: Text('Provision from the QR created over USB, or an enrollment card.', style: TextStyle(fontSize: 12))),
              ]),
            if (result.isNotEmpty) Text(result, key: ValueKey('result-${tier.key}'), style: const TextStyle(fontSize: 12)),
          ],
        ),
      ),
    );
  }

  String _status(TierCredential credential, Advertisement? selected) {
    if (!credential.isPaired) return "Not paired (${tier.key}). Pair to run this page's commands";
    if (selected?.isSecured == true) return 'Sessions are encrypted.';
    final with_ = credential.deviceName != null ? " with '${credential.deviceName}'" : '';
    return 'Paired (${tier.key})$with_, but the device reports no pairings. It may have been reset; unpair to start over.';
  }
}

/// The Low and High tabs: the serial number, LED 1, and Echo cards.
class CommandsScreen extends StatefulWidget {
  const CommandsScreen(this.tier, {super.key});

  final Tier tier;

  @override
  State<CommandsScreen> createState() => _CommandsScreenState();
}

class _CommandsScreenState extends State<CommandsScreen> {
  late final TextEditingController _serial;
  late final TextEditingController _echo;

  @override
  void initState() {
    super.initState();
    final noise = context.read<NoiseViewModel>();
    _serial = TextEditingController(text: noise.serialInput[widget.tier]);
    _echo = TextEditingController(text: noise.echoInput[widget.tier]);
  }

  @override
  void dispose() {
    _serial.dispose();
    _echo.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final tier = widget.tier;
    final noise = context.watch<NoiseViewModel>();
    final commands = context.watch<CommandRunner>();
    context.watch<SoakLoop>();
    context.watch<DeviceSelection>();
    context.watch<PairingRepository>();
    final canRun = noise.canRun;
    if (_serial.text != noise.serialInput[tier] && !_serialFocused) {
      _serial.text = noise.serialInput[tier]!;
    }
    final on = commands.ledOn(tier);
    final ledLabel = switch (on) {
      true => 'LED 1 is on. Tap to switch it off',
      false => 'LED 1 is off. Tap to switch it on',
      null => 'LED 1 state not read yet. Tap to read it',
    };
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(tier.title, style: Theme.of(context).textTheme.titleLarge),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('Serial Number', style: Theme.of(context).textTheme.titleMedium),
              TextField(
                key: ValueKey('serial-input-${tier.key}'),
                controller: _serial,
                decoration: const InputDecoration(labelText: 'Serial number'),
                keyboardType: TextInputType.number,
                onChanged: (value) => noise.serialInput[tier] = value,
              ),
              const SizedBox(height: 8),
              Row(children: [
                OutlinedButton(key: ValueKey('serial-read-${tier.key}'), onPressed: canRun ? () => noise.readSerial(tier) : null, child: const Text('Read')),
                const SizedBox(width: 8),
                OutlinedButton(key: ValueKey('serial-write-${tier.key}'), onPressed: canRun ? () => noise.writeSerial(tier) : null, child: const Text('Write')),
              ]),
            ]),
          ),
        ),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('LED 1', style: Theme.of(context).textTheme.titleMedium),
              Row(children: [
                InkWell(
                  key: ValueKey('led-${tier.key}'),
                  onTap: canRun ? () => noise.tapLed(tier) : null,
                  customBorder: const CircleBorder(),
                  child: Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: switch (on) { true => Colors.amber, false => Colors.grey, null => Theme.of(context).colorScheme.outline },
                    ),
                    child: on == null ? const Icon(Icons.refresh, color: Colors.white) : null,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(child: Text(ledLabel, key: ValueKey('led-label-${tier.key}'), style: const TextStyle(fontSize: 12))),
                IconButton(key: ValueKey('led-read-${tier.key}'), onPressed: canRun ? () => noise.readLed(tier) : null, icon: const Icon(Icons.refresh), tooltip: 'Read LED state'),
              ]),
            ]),
          ),
        ),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('Echo', style: Theme.of(context).textTheme.titleMedium?.copyWith(color: tier == Tier.low ? const Color(0xFFC62828) : null)),
              TextField(
                key: ValueKey('echo-input-${tier.key}'),
                controller: _echo,
                decoration: const InputDecoration(labelText: 'Payload'),
                onChanged: (value) => noise.echoInput[tier] = value,
              ),
              const SizedBox(height: 8),
              OutlinedButton(key: ValueKey('echo-write-${tier.key}'), onPressed: canRun ? () => noise.echo(tier) : null, child: const Text('Write')),
            ]),
          ),
        ),
      ],
    );
  }

  bool get _serialFocused => FocusManager.instance.primaryFocus?.context?.widget is EditableText;
}

/// The Loop tab: start and stop the soak loop and watch its counters.
class LoopScreen extends StatelessWidget {
  const LoopScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final vm = context.watch<LoopViewModel>();
    final loop = context.watch<SoakLoop>();
    final selected = context.watch<DeviceSelection>().value;
    final started = loop.started != null;
    String ms(double? value) => value == null ? '—' : '${value.toStringAsFixed(0)} ms';
    String two(int v) => v.toString().padLeft(2, '0');
    final elapsed = loop.elapsed.inSeconds;
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(loop.isRunning ? 'Loop (running)' : 'Loop', style: Theme.of(context).textTheme.titleLarge),
        Row(children: [
          FilledButton(key: const ValueKey('loop-start'), onPressed: !loop.isRunning && selected != null ? vm.start : null, child: const Text('Start')),
          const SizedBox(width: 8),
          OutlinedButton(key: const ValueKey('loop-stop'), onPressed: loop.isRunning ? vm.stop : null, child: const Text('Stop')),
        ]),
        const Text('The loop echoes each cycle, which requires the high-privilege pairing.', style: TextStyle(fontSize: 12)),
        const SizedBox(height: 8),
        _StatRow('Device', loop.deviceName.isEmpty ? '—' : loop.deviceName, 'loop-device'),
        _StatRow('Tests', started ? '${loop.tests}' : '—', 'loop-tests'),
        _StatRow('Connection failures', started ? '${loop.connectionFailures}' : '—', 'loop-connection-failures'),
        _StatRow('Command failures', started ? '${loop.commandFailures}' : '—', 'loop-command-failures'),
        _StatRow('Min connect', ms(loop.minConnectMs), 'loop-min'),
        _StatRow('Average connect', ms(loop.avgConnectMs), 'loop-avg'),
        _StatRow('Max connect', ms(loop.maxConnectMs), 'loop-max'),
        _StatRow('Elapsed', started ? '${two(elapsed ~/ 3600)}:${two(elapsed % 3600 ~/ 60)}:${two(elapsed % 60)}' : '—', 'loop-elapsed'),
      ],
    );
  }
}

class _StatRow extends StatelessWidget {
  const _StatRow(this.label, this.value, this.tag);

  final String label;
  final String value;
  final String tag;

  @override
  Widget build(BuildContext context) => Row(children: [
        Expanded(child: Text(label)),
        Text(value, key: ValueKey(tag), style: const TextStyle(fontFamily: 'monospace')),
      ]);
}
