// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:blueconnect/blueconnect.dart';
import 'package:torann/torann.dart';

/// Two cross-wired in-memory peripherals for the loopback tests. The same
/// double as `blueconnect/test/pipes.dart`; integration tests cannot import
/// a package's test folder.
final class LoopbackPipe implements BlePeripheral {
  LoopbackPipe._(this.mtu);

  final _inbound = ReceiveQueue();
  late LoopbackPipe _peer;

  @override
  int mtu;

  @override
  Advertisement get advertisement => Advertisement(
        id: 'pipe',
        localName: 'pipe',
        isConnectable: true,
        isSecured: false,
        rssi: const Rssi.single(-50),
        timestamp: DateTime.now(),
      );

  @override
  bool get isConnected => true;

  @override
  Stream<ConnectionState> get states => const Stream.empty();

  static (LoopbackPipe, LoopbackPipe) pair({int mtu = 20}) {
    final a = LoopbackPipe._(mtu);
    final b = LoopbackPipe._(mtu);
    a._peer = b;
    b._peer = a;
    return (a, b);
  }

  @override
  Future<void> connect() async {}

  @override
  Future<void> disconnect() async {}

  @override
  Future<Uint8List?> read({Duration? timeout}) => _inbound.pop(timeout: timeout);

  @override
  Future<void> write(Uint8List bytes) async => _peer._inbound.push(bytes);

  @override
  Future<void> close() async => _inbound.close(BleTransportException('closed'));
}

typedef Handler = List<Response> Function(Request request);

/// An in-process Verdigris responder on the far end of a [LoopbackPipe]: it
/// runs Torann's responder side of the handshake (XX or IK, selected by the
/// length of message 1 as the firmware does), then answers each sealed
/// request through [handler]. It runs on the device because Torann's Dart
/// API calls the native implementation through Pigeon.
final class FakeDevice {
  FakeDevice(this._pipe, this.identity, {Handler? handler}) : _handler = handler ?? echoHandler;

  final LoopbackPipe _pipe;
  final LinkIdentity identity;
  final Handler _handler;
  final List<int> observedHandshakeLengths = [];
  String observedPattern = '';
  bool _stopped = false;
  Future<void>? _serving;

  static Future<FakeDevice> create(LoopbackPipe pipe, {Handler? handler}) async => FakeDevice(pipe, await LinkIdentity.generate(), handler: handler);

  static List<Response> echoHandler(Request request) {
    final response = Response()
      ..id = request.id
      ..status = Status.STATUS_OK;
    switch (request.whichOp()) {
      case Request_Op.echo:
        response.echo = EchoResponse()..payload = request.echo.payload;
      case Request_Op.getSerialNumber:
        response.name = NameResponse()..name = 'Verdigris 0042';
      case Request_Op.pair:
      case Request_Op.unpair:
      case Request_Op.enroll:
        response.ack = AckResponse();
      case Request_Op.getLed:
        response.led = LedResponse()..on = true;
      case Request_Op.setLed:
        response.led = LedResponse()..on = request.setLed.on;
      default:
        response.status = Status.STATUS_UNKNOWN_OP;
    }
    return [response];
  }

  void start() {
    _serving = _serve().catchError((_) {});
  }

  Future<void> stop() async {
    _stopped = true;
    await _pipe.close();
    await _serving;
  }

  Future<void> _serve() async {
    final segments = SegmentLink(_pipe, VerdigrisPeripheral.maxFrame);
    final keyPair = await (await LinkProtocol.xx()).createKeyPair(identity.privateKey);
    try {
      while (!_stopped) {
        final (type, message1) = FrameType.split(await segments.read());
        if (type != FrameType.handshake) continue;
        observedHandshakeLengths.add(message1.length);
        Transport transport;
        if (message1.length == SecureLink.ikHandshakeMessage1Length) {
          observedPattern = 'IK';
          final handshake = await (await LinkProtocol.ik()).createHandshake(initiator: false, prologue: SecureLink.prologue, localStatic: keyPair);
          try {
            final read1 = await handshake.readMessage(message1);
            if (read1.payload.length != 1 || read1.payload[0] != 0x5A) throw StateError('IK message 1 payload');
            final write2 = await handshake.writeMessage(SecureLink.handshakePayload);
            observedHandshakeLengths.add(write2.message.length);
            await segments.write(FrameType.bundle(FrameType.handshake, write2.message));
            transport = write2.transport!;
          } finally {
            await handshake.close();
          }
        } else {
          observedPattern = 'XX';
          if (message1.length != SecureLink.handshakeMessage1Length) throw StateError('XX message 1 length');
          final handshake = await (await LinkProtocol.xx()).createHandshake(initiator: false, prologue: SecureLink.prologue, localStatic: keyPair);
          try {
            await handshake.readMessage(message1);
            final write2 = await handshake.writeMessage(SecureLink.handshakePayload);
            observedHandshakeLengths.add(write2.message.length);
            await segments.write(FrameType.bundle(FrameType.handshake, write2.message));
            final (type3, message3) = FrameType.split(await segments.read());
            if (type3 != FrameType.handshake) throw StateError('XX message 3 type');
            observedHandshakeLengths.add(message3.length);
            final read3 = await handshake.readMessage(message3);
            if (read3.payload.length != 1 || read3.payload[0] != 0x5A) throw StateError('XX message 3 payload');
            transport = read3.transport!;
          } finally {
            await handshake.close();
          }
        }
        try {
          while (!_stopped) {
            final (sealedType, sealed) = FrameType.split(await segments.read());
            if (sealedType == FrameType.handshake) break;
            if (sealedType != FrameType.encrypted) continue;
            final request = Request.fromBuffer(await transport.readMessage(sealed));
            for (final response in _handler(request)) {
              await segments.write(FrameType.bundle(FrameType.encrypted, await transport.writeMessage(response.writeToBuffer())));
            }
          }
        } finally {
          await transport.close();
        }
      }
    } finally {
      await keyPair.close();
    }
  }
}
