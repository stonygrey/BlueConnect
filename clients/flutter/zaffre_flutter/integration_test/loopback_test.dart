// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'fake_device.dart';

Uint8List payload(int length) => Uint8List.fromList(List.generate(length, (i) => i & 0xFF));

/// The initiator against Torann's responder over the loopback pipe, on the
/// device, where the Torann plugin is available.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  IoLog.sink = (_) {};

  testWidgets('unpinned initiator runs XX and learns the device key', (_) async {
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev);
    device.start();
    final link = SecureLink(app, VerdigrisPeripheral.maxFrame, LinkSecurity(await LinkIdentity.generate(), null));
    await link.establish();
    expect(link.remoteStaticKey, device.identity.publicKey);
    await link.send((Request()
          ..id = 1
          ..getSerialNumber = GetSerialNumberRequest())
        .writeToBuffer());
    await link.receive();
    expect(device.observedPattern, 'XX');
    expect(device.observedHandshakeLengths, [32, 97, 65]);
    await link.close();
    await device.stop();
  });

  testWidgets('pinned initiator runs IK', (_) async {
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev);
    device.start();
    final link = SecureLink(app, VerdigrisPeripheral.maxFrame, LinkSecurity(await LinkIdentity.generate(), device.identity.publicKey));
    await link.establish();
    expect(link.remoteStaticKey, device.identity.publicKey);
    expect(device.observedPattern, 'IK');
    expect(device.observedHandshakeLengths, [97, 49]);
    await link.close();
    await device.stop();
  });

  testWidgets('echo round trips through the secure link', (_) async {
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev);
    device.start();
    final verdigris = VerdigrisPeripheral(app, const BlueConnectOptions(), LinkSecurity(await LinkIdentity.generate(), null));
    expect(await verdigris.echo(payload(1536)), payload(1536));
    expect(verdigris.remoteStaticKey, device.identity.publicKey);
    await verdigris.close();
    await device.stop();
  });

  testWidgets('status other than OK becomes VerdigrisException', (_) async {
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev, handler: (request) => [
          Response()
            ..id = request.id
            ..status = Status.STATUS_PERMISSION_DENIED
            ..detail = 'high privilege required',
        ]);
    device.start();
    final verdigris = VerdigrisPeripheral(app, const BlueConnectOptions(), LinkSecurity(await LinkIdentity.generate(), null));
    await expectLater(
      verdigris.echo(Uint8List.fromList([1])),
      throwsA(isA<VerdigrisException>()
          .having((e) => e.status, 'status', Status.STATUS_PERMISSION_DENIED)
          .having((e) => e.message, 'message', 'The device rejected the request: STATUS_PERMISSION_DENIED (high privilege required)')),
    );
    await verdigris.close();
    await device.stop();
  });

  testWidgets('stale responses are dropped and ids increment', (_) async {
    final seen = <int>[];
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev, handler: (request) {
      seen.add(request.id);
      return [
        Response()
          ..id = request.id + 100
          ..status = Status.STATUS_OK
          ..name = (NameResponse()..name = 'stale'),
        Response()
          ..id = request.id
          ..status = Status.STATUS_OK
          ..name = (NameResponse()..name = 'Verdigris 0042'),
      ];
    });
    device.start();
    final verdigris = VerdigrisPeripheral(app, const BlueConnectOptions(), LinkSecurity(await LinkIdentity.generate(), null));
    expect(await verdigris.getSerialNumber(), 'Verdigris 0042');
    expect(await verdigris.getSerialNumber(), 'Verdigris 0042');
    expect(seen, [1, 2]);
    await verdigris.close();
    await device.stop();
  });

  testWidgets('a silent device times the command out', (_) async {
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev, handler: (_) => []);
    device.start();
    final verdigris = VerdigrisPeripheral(app, const BlueConnectOptions(commandTimeout: Duration(milliseconds: 300)), LinkSecurity(await LinkIdentity.generate(), null));
    await expectLater(verdigris.getLed(), throwsA(isA<BleTimeoutException>().having((e) => e.message, 'message', contains('timed out after 0s'))));
    await verdigris.close();
    await device.stop();
  });

  testWidgets('pair returns the device key', (_) async {
    final (app, dev) = LoopbackPipe.pair();
    final device = await FakeDevice.create(dev);
    device.start();
    final verdigris = VerdigrisPeripheral(app, const BlueConnectOptions(), LinkSecurity(await LinkIdentity.generate(), null));
    expect(await verdigris.pair(PrivilegeLevel.high), device.identity.publicKey);
    await verdigris.close();
    await device.stop();
  });
}
