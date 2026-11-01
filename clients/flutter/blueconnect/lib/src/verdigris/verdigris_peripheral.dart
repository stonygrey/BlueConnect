// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';
import 'dart:typed_data';

import 'package:fixnum/fixnum.dart';
import 'package:protobuf/protobuf.dart';

import '../abstractions/ble_peripheral.dart';
import '../abstractions/blueconnect_options.dart';
import '../abstractions/exceptions.dart';
import '../core/async_gate.dart';
import '../core/io_log.dart';
import '../generated/verdigris.pb.dart';
import '../secure/link_protocol.dart';
import '../secure/secure_link.dart';

/// The privilege tier a pairing grants. `Privilege` itself is the generated
/// protobuf enum, so the API name carries a suffix.
enum PrivilegeLevel {
  low(Privilege.PRIVILEGE_LOW),
  high(Privilege.PRIVILEGE_HIGH);

  const PrivilegeLevel(this.proto);

  final Privilege proto;
}

/// `VerdigrisException`: the device answered with a status other than OK.
final class VerdigrisException extends BlueConnectException {
  VerdigrisException(this.status, String? detail)
      : detail = (detail == null || detail.isEmpty) ? null : detail,
        super('The device rejected the request: ${status.name}${(detail == null || detail.isEmpty) ? '' : ' ($detail)'}');

  final Status status;
  final String? detail;
}

/// Typed Verdigris commands over a secure link. One command at a time; each
/// request carries a fresh id and the matching response is awaited under
/// [BlueConnectOptions.commandTimeout].
final class VerdigrisPeripheral {
  VerdigrisPeripheral(this.peripheral, BlueConnectOptions options, LinkSecurity security)
      : _commandTimeout = options.commandTimeout,
        _link = SecureLink(peripheral, maxFrame, security);

  /// Cap on a reassembled GATT message. The value is arbitrary and specific
  /// to this implementation; it is not negotiated, so it must equal the
  /// firmware's `SEG_MAX_MESSAGE` and the two should be kept in sync.
  static const maxFrame = 2304;
  static const maxEncodedRequest = maxFrame - 1 - LinkProtocol.tagLength;

  /// Longer than the firmware's 30 s pairing window (`PAIR_WINDOW_SECONDS`),
  /// an arbitrary implementation value. The two should be kept in sync, so
  /// that the app is still waiting when the device answers at the end of the
  /// window.
  static const pairTimeout = Duration(seconds: 40);

  final BlePeripheral peripheral;
  final SecureLink _link;
  final Duration _commandTimeout;
  final _gate = AsyncGate();
  int _nextId = 0;
  bool _closed = false;

  /// The device's proven static key once a session has been established.
  Uint8List? get remoteStaticKey => _link.remoteStaticKey;

  Future<Uint8List> echo(Uint8List payload) async {
    final response = await _invoke(Request()..echo = (EchoRequest()..payload = payload), Response_Result.echo);
    return Uint8List.fromList(response.echo.payload);
  }

  Future<double> squareRoot(double value) async =>
      (await _invoke(Request()..sqrt = (SqrtRequest()..value = value), Response_Result.sqrt)).sqrt.value;

  Future<Int64> fibonacci(int n) async => (await _invoke(Request()..fib = (FibRequest()..n = n), Response_Result.fib)).fib.value;

  Future<DateTime> getTime() async {
    final millis = (await _invoke(Request()..getTime = GetTimeRequest(), Response_Result.time)).time.unixMillis;
    return DateTime.fromMillisecondsSinceEpoch(millis.toInt(), isUtc: true);
  }

  Future<void> setTime(DateTime time) async {
    await _invoke(Request()..setTime = (SetTimeRequest()..unixMillis = Int64(time.millisecondsSinceEpoch)), Response_Result.time);
  }

  Future<bool> getLed() async => (await _invoke(Request()..getLed = GetLedRequest(), Response_Result.led)).led.on;

  Future<bool> setLed(bool on) async => (await _invoke(Request()..setLed = (SetLedRequest()..on = on), Response_Result.led)).led.on;

  Future<String> setSerialNumber(int serialNumber) async {
    if (serialNumber < 1 || serialNumber > 65535) {
      throw ArgumentError('Serial number must be a whole number from 1 to 65535.');
    }
    return (await _invoke(Request()..setSerialNumber = (SetSerialNumberRequest()..serialNumber = serialNumber), Response_Result.name)).name.name;
  }

  Future<String> getSerialNumber() async =>
      (await _invoke(Request()..getSerialNumber = GetSerialNumberRequest(), Response_Result.name)).name.name;

  /// Arms the device's 30 s pairing window for [privilege] and waits for the
  /// button. Returns the device key to pin.
  Future<Uint8List> pair(PrivilegeLevel privilege) async {
    await _invoke(Request()..pair = (PairRequest()..privilege = privilege.proto), Response_Result.ack, timeout: pairTimeout);
    return _link.remoteStaticKey!;
  }

  Future<void> unpair() async {
    await _invoke(Request()..unpair = UnpairRequest(), Response_Result.ack);
  }

  Future<void> enroll(Uint8List clientPublicKey) async {
    await _invoke(Request()..enroll = (EnrollRequest()..clientPublicKey = clientPublicKey), Response_Result.ack);
  }

  /// Closes the link and the peripheral, disconnecting first if connected.
  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    await _link.close();
    await peripheral.close();
  }

  Future<Response> _invoke(Request request, Response_Result expected, {Duration? timeout}) {
    if (_closed) {
      throw StateError('The Verdigris peripheral has been closed.');
    }
    final commandTimeout = timeout ?? _commandTimeout;
    return _gate.withLock(() async {
      final id = ++_nextId;
      request.id = id;
      final body = request.writeToBuffer();
      if (body.length > maxEncodedRequest) {
        throw StateError('Encoded request is ${body.length} bytes; the maximum is $maxEncodedRequest (type + AEAD tag must fit under $maxFrame).');
      }
      try {
        return await _exchange(id, body, expected).timeout(commandTimeout);
      } on TimeoutException {
        // The session is unusable after a timeout: a late reply would arrive on
        // the next command. Close the link so that nothing else reads it.
        await _link.close();
        throw BleTimeoutException('Verdigris command timed out after ${commandTimeout.inSeconds}s.');
      }
    });
  }

  Future<Response> _exchange(int id, Uint8List body, Response_Result expected) async {
    await _link.send(body);
    while (true) {
      final raw = await _link.receive();
      Response response;
      try {
        response = Response.fromBuffer(raw);
      } on InvalidProtocolBufferException catch (e) {
        throw BleTransportException('Undecodable Verdigris frame; the stream is desynchronized.', e);
      }
      if (response.id != id) {
        IoLog.debug('Dropping stale Verdigris response ${response.id} while awaiting $id.');
        continue;
      }
      if (response.status != Status.STATUS_OK) {
        throw VerdigrisException(response.status, response.detail);
      }
      if (response.whichResult() != expected) {
        throw BleTransportException('Verdigris response carried ${response.whichResult().name} where ${expected.name} was expected.');
      }
      return response;
    }
  }
}
