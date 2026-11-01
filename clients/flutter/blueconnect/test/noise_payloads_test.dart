// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Uint8List key(int fill) => Uint8List.fromList(List.filled(32, fill));

  test('credential round trip preserves keys and tier', () {
    final random = Random.secure();
    final app = Uint8List.fromList(List.generate(32, (_) => random.nextInt(256)))..[0] |= 1;
    final device = Uint8List.fromList(List.generate(32, (_) => random.nextInt(256)))..[0] |= 1;
    final original = NoiseQrPayload(tier: PrivilegeLevel.high, appPrivateKey: app, devicePublicKey: device, deviceName: 'Verdigris 1');
    final parsed = NoiseQrPayload.parse(original.encode())!;
    expect(parsed.tier, PrivilegeLevel.high);
    expect(parsed.appPrivateKey, app);
    expect(parsed.devicePublicKey, device);
    expect(parsed.deviceName, 'Verdigris 1');
  });

  test('credential parse rejects all-zero keys', () {
    final zeros = base64Url.encode(Uint8List(64)).replaceAll('=', '');
    expect(NoiseQrPayload.parse('SGNK1:L:$zeros'), isNull);
  });

  test('enrollment round trips with and without a name', () {
    final named = NoiseEnrollmentPayload(enrollmentPrivateKey: key(1), devicePublicKey: key(2), deviceName: 'Verdigris 0001');
    final parsed = NoiseEnrollmentPayload.parse(named.encode())!;
    expect(parsed.enrollmentPrivateKey, named.enrollmentPrivateKey);
    expect(parsed.devicePublicKey, named.devicePublicKey);
    expect(parsed.deviceName, named.deviceName);
    expect(named.encode(), startsWith('SGNE1:'));
    final anonymous = NoiseEnrollmentPayload(enrollmentPrivateKey: key(3), devicePublicKey: key(4));
    final parsed2 = NoiseEnrollmentPayload.parse(anonymous.encode())!;
    expect(parsed2.deviceName, isNull);
    expect(parsed2.enrollmentPrivateKey, key(3));
    expect(parsed2.devicePublicKey, key(4));
  });

  test('each parser rejects the other payload and bad blobs', () {
    final credential = NoiseQrPayload(tier: PrivilegeLevel.low, appPrivateKey: key(1), devicePublicKey: key(2)).encode();
    expect(NoiseEnrollmentPayload.parse(credential), isNull);
    expect(NoiseQrPayload.parse(NoiseEnrollmentPayload(enrollmentPrivateKey: key(1), devicePublicKey: key(2)).encode()), isNull);
    expect(NoiseEnrollmentPayload.parse('SGNE1:not-base64!'), isNull);
    expect(NoiseEnrollmentPayload.parse('SGNE1:${base64Url.encode(Uint8List(63)).replaceAll('=', '')}'), isNull);
    expect(() => NoiseEnrollmentPayload(enrollmentPrivateKey: Uint8List(32), devicePublicKey: key(2)).encode(), throwsA(isA<LinkSecurityException>()));
  });

  test('a name containing colons survives the split', () {
    final payload = NoiseEnrollmentPayload(enrollmentPrivateKey: key(1), devicePublicKey: key(2), deviceName: 'a:b');
    expect(NoiseEnrollmentPayload.parse(payload.encode())!.deviceName, 'a:b');
  });
}
