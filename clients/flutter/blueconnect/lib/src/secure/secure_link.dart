// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:torann/torann.dart';

import '../abstractions/ble_peripheral.dart';
import '../abstractions/exceptions.dart';
import '../core/io_log.dart';
import '../core/segment_link.dart';
import 'frame_type.dart';
import 'link_protocol.dart';

/// Encrypted, mutually authenticated link over a peripheral, as initiator.
/// An initiator holding a pinned peer key opens with Noise IK; otherwise
/// Noise XX runs. After the handshake every message is AEAD-sealed as a
/// `0x02` frame. This is the initiator half only; the responder half is the
/// firmware, and the tests use Torann's responder.
final class SecureLink {
  SecureLink(BlePeripheral peripheral, int maxFrame, this._security) : _segments = SegmentLink(peripheral, maxFrame);

  /// Bound into the handshake transcript. Framing is below this layer.
  static final prologue = Uint8List.fromList('BlueConnectLink/1'.codeUnits);

  /// Messages 2 and 3 carry this single ignored byte instead of an empty
  /// payload: Android's native AES-GCM rejects zero-length inputs.
  static final handshakePayload = Uint8List.fromList([0x5A]);
  static const handshakeMessage1Length = LinkProtocol.keyLength;
  static const handshakeMessage2Length = LinkProtocol.keyLength + LinkProtocol.keyLength + LinkProtocol.tagLength + 1 + LinkProtocol.tagLength;
  static const handshakeMessage3Length = LinkProtocol.keyLength + LinkProtocol.tagLength + 1 + LinkProtocol.tagLength;
  static const ikHandshakeMessage1Length = LinkProtocol.keyLength + LinkProtocol.keyLength + LinkProtocol.tagLength + 1 + LinkProtocol.tagLength;
  static const ikHandshakeMessage2Length = LinkProtocol.keyLength + 1 + LinkProtocol.tagLength;

  final SegmentLink _segments;
  final LinkSecurity _security;
  Transport? _transport;
  bool _closed = false;

  /// The peer's proven static key; available once the handshake has completed.
  Uint8List? remoteStaticKey;

  bool get isEstablished => _transport != null;

  /// Runs the handshake, IK when a peer key is pinned and XX otherwise,
  /// before any traffic flows.
  Future<void> establish() async {
    if (_closed) {
      throw StateError('The secure link has been closed.');
    }
    if (isEstablished) return;
    final staticKeyPair = await (await LinkProtocol.xx()).createKeyPair(_security.identity.privateKey);
    HandshakeState? handshake;
    try {
      final pin = _security.pinnedPeerKey;
      Transport? split;
      if (pin != null) {
        // IK: message 1 both proves this identity and shows we already hold
        // the peer's key; the reply completes the session. A stale pin fails
        // on the device and yields silence.
        final state = await _step(() async =>
            (await LinkProtocol.ik()).createHandshake(initiator: true, prologue: prologue, localStatic: staticKeyPair, remoteStatic: pin));
        handshake = state;
        final message1 = await _step(() => state.writeMessage(handshakePayload));
        await _writeTyped(FrameType.handshake, message1.message);
        final message2 = await _readHandshakeFrame();
        _expectHandshakeLength(message2, ikHandshakeMessage2Length, 2);
        final read2 = await _step(() => state.readMessage(message2));
        _expectHandshakePayload(read2.payload);
        split = read2.transport;
      } else {
        final state = await _step(
            () async => (await LinkProtocol.xx()).createHandshake(initiator: true, prologue: prologue, localStatic: staticKeyPair));
        handshake = state;
        final message1 = await _step(() => state.writeMessage());
        await _writeTyped(FrameType.handshake, message1.message);
        final message2 = await _readHandshakeFrame();
        _expectHandshakeLength(message2, handshakeMessage2Length, 2);
        final read2 = await _step(() => state.readMessage(message2));
        _expectHandshakePayload(read2.payload);
        _verifyPeer(await state.remoteStaticPublicKey());
        final message3 = await _step(() => state.writeMessage(handshakePayload));
        await _writeTyped(FrameType.handshake, message3.message);
        split = message3.transport;
      }
      remoteStaticKey = await handshake.remoteStaticPublicKey();
      if (split == null) {
        throw LinkSecurityException('The handshake completed without producing a transport.');
      }
      _transport = split;
    } finally {
      await handshake?.close();
      await staticKeyPair.close();
    }
    IoLog.info('Secure link established; peer static key ${IoLog.hex(remoteStaticKey ?? Uint8List(0))}.');
  }

  Future<void> send(Uint8List message) async {
    await establish();
    final sealed = await _step(() => _transport!.writeMessage(message));
    await _writeTyped(FrameType.encrypted, sealed);
  }

  Future<Uint8List> receive() async {
    await establish();
    while (true) {
      final (type, payload) = await _readTyped();
      if (type == FrameType.encrypted) {
        try {
          return await _transport!.readMessage(payload);
        } on TorannException catch (e) {
          throw LinkSecurityException('AEAD authentication failed; the frame was forged, corrupted, or replayed.', e);
        }
      }
      if (type == FrameType.plaintext) {
        throw LinkSecurityException('Plaintext frame on a secure link.');
      }
      IoLog.debug('Ignoring frame type $type on an established secure link.');
    }
  }

  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    await _transport?.close();
    _transport = null;
  }

  void _verifyPeer(Uint8List? remoteStatic) {
    // Torann leaves non-contributory-key checks to the application (Noise
    // spec §12.1); an all-zero static is never a legitimate identity.
    if (remoteStatic == null || remoteStatic.every((b) => b == 0)) {
      throw LinkSecurityException('Peer presented an all-zero static key.');
    }
    final pin = _security.pinnedPeerKey;
    if (pin != null && !_equal(remoteStatic, pin)) {
      throw LinkSecurityException("Peer's static key does not match the paired key.");
    }
  }

  /// Runs one Torann step, mapping its failures into the link taxonomy.
  Future<T> _step<T>(Future<T> Function() body) async {
    try {
      return await body();
    } on TorannException catch (e) {
      throw LinkSecurityException('The handshake failed: ${e.message}', e);
    }
  }

  static void _expectHandshakeLength(Uint8List message, int expected, int number) {
    if (message.length != expected) {
      throw LinkSecurityException('Handshake message $number is ${message.length} bytes; expected $expected.');
    }
  }

  static void _expectHandshakePayload(Uint8List payload) {
    if (!_equal(payload, handshakePayload)) {
      throw LinkSecurityException('Handshake payload does not match the protocol constant.');
    }
  }

  static bool _equal(List<int> a, List<int> b) {
    if (a.length != b.length) return false;
    for (var i = 0; i < a.length; i++) {
      if (a[i] != b[i]) return false;
    }
    return true;
  }

  Future<void> _writeTyped(int type, Uint8List payload) async {
    final bundled = FrameType.bundle(type, payload);
    IoLog.info(IoLog.message('write', type, bundled));
    await _segments.write(bundled);
  }

  Future<(int, Uint8List)> _readTyped() async {
    final bundled = await _segments.read();
    final (type, payload) = FrameType.split(bundled);
    IoLog.info(IoLog.message('read', type, bundled));
    return (type, payload);
  }

  Future<Uint8List> _readHandshakeFrame() async {
    final (type, payload) = await _readTyped();
    if (type != FrameType.handshake) {
      throw LinkSecurityException('Expected a handshake frame but received type $type.');
    }
    return payload;
  }
}
