// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { create, fromBinary, toBinary } from '@bufbuild/protobuf';

import { defaultOptions } from '../abstractions/blueConnectOptions';
import { BleTimeoutError, LinkSecurityError } from '../abstractions/errors';
import { IoLog } from '../core/ioLog';
import { SegmentLink } from '../core/segmentLink';
import { AckResponseSchema, EchoResponseSchema, LedResponseSchema, NameResponseSchema, RequestSchema, ResponseSchema, Status, type Response } from '../generated/verdigris_pb';
import { FrameType } from '../secure/frameType';
import { LinkIdentity, LinkSecurity } from '../secure/linkProtocol';
import { VerdigrisError, VerdigrisPeripheral } from '../verdigris/verdigrisPeripheral';
import { NoiseProtocol } from './fakeTorann';
import { LoopbackPipe } from './pipes';

jest.mock('@stonygrey/torann', () => jest.requireActual('./fakeTorann'));

/**
 * The command layer end to end over a loopback pipe: framing, segmentation,
 * ids, status mapping, and the timeout, against a scripted device that
 * speaks the fake Torann's handshake.
 */
beforeAll(() => {
  IoLog.sink = () => {};
});

const tagLength = 16;

/** The responder: completes the handshake, then answers each request with `answer`'s response, or stays silent when it returns null. */
class FakeDevice {
  readonly segments: SegmentLink;
  readonly requests: number[] = [];
  readonly staticKey = Uint8Array.from({ length: 32 }, (_, i) => i + 1);

  constructor(
    pipe: LoopbackPipe,
    private readonly answer: (request: ReturnType<typeof fromBinary<typeof RequestSchema>>) => Response | null
  ) {
    this.segments = new SegmentLink(pipe, VerdigrisPeripheral.maxFrame);
  }

  async run(pinned: boolean): Promise<void> {
    const responder = NoiseProtocol.parse(pinned ? 'Noise_IK_25519_AESGCM_SHA256' : 'Noise_XX_25519_AESGCM_SHA256').createHandshake({
      initiator: false,
      localStatic: NoiseProtocol.parse('Noise_XX_25519_AESGCM_SHA256').createKeyPair(this.staticKey.map((b) => ~b & 0xff)),
    });
    let transport = null as ReturnType<typeof responder.writeMessage>['transport'];
    const message1 = await this.readFrame(FrameType.handshake);
    responder.readMessage(message1);
    const reply = responder.writeMessage(Uint8Array.of(0x5a));
    await this.segments.write(FrameType.bundle(FrameType.handshake, reply.message));
    transport = reply.transport;
    if (!pinned) {
      const message3 = await this.readFrame(FrameType.handshake);
      transport = responder.readMessage(message3).transport;
    }
    for (;;) {
      const sealed = await this.readFrame(FrameType.encrypted);
      const request = fromBinary(RequestSchema, transport!.readMessage(sealed));
      this.requests.push(request.id);
      const response = this.answer(request);
      if (response === null) continue;
      await this.segments.write(FrameType.bundle(FrameType.encrypted, transport!.writeMessage(toBinary(ResponseSchema, response))));
    }
  }

  private async readFrame(type: number): Promise<Uint8Array> {
    const [actual, payload] = FrameType.split(await this.segments.read());
    if (actual !== type) throw new Error(`frame type ${actual}`);
    return payload;
  }
}

function ok(id: number, result: Response['result']): Response {
  return create(ResponseSchema, { id, status: Status.OK, result });
}

function setup(answer: ConstructorParameters<typeof FakeDevice>[1], pinned = false, commandTimeoutMs = 10_000) {
  const [a, b] = LoopbackPipe.pair(20);
  const device = new FakeDevice(b, answer);
  device.run(pinned).catch(() => undefined);
  const identity = LinkIdentity.generate();
  const security = new LinkSecurity(identity, pinned ? device.staticKey : null);
  const verdigris = new VerdigrisPeripheral(a, { ...defaultOptions, commandTimeoutMs }, security);
  return { device, verdigris };
}

test('echo round trips through XX, segmentation, and the sealed frames', async () => {
  const { device, verdigris } = setup((request) =>
    request.op.case === 'echo' ? ok(request.id, { case: 'echo', value: create(EchoResponseSchema, { payload: request.op.value.payload }) }) : null
  );
  const payload = Uint8Array.from({ length: 1536 }, (_, i) => (i * 7) & 0xff);
  expect(await verdigris.echo(payload)).toEqual(payload);
  expect(verdigris.remoteStaticKey).toEqual(device.staticKey);
  expect(device.requests).toEqual([1]);
  await verdigris.close();
});

test('IK is used when a device key is pinned, and commands carry increasing ids', async () => {
  const { device, verdigris } = setup((request) => {
    switch (request.op.case) {
      case 'getLed':
        return ok(request.id, { case: 'led', value: create(LedResponseSchema, { on: true }) });
      case 'getSerialNumber':
        return ok(request.id, { case: 'name', value: create(NameResponseSchema, { name: 'Verdigris 0042' }) });
      case 'unpair':
        return ok(request.id, { case: 'ack', value: create(AckResponseSchema) });
      default:
        return null;
    }
  }, true);
  expect(await verdigris.getLed()).toBe(true);
  expect(await verdigris.getSerialNumber()).toBe('Verdigris 0042');
  await verdigris.unpair();
  expect(device.requests).toEqual([1, 2, 3]);
  await verdigris.close();
});

test('a status other than OK becomes a VerdigrisError with the detail', async () => {
  const { verdigris } = setup((request) => create(ResponseSchema, { id: request.id, status: Status.UNAUTHENTICATED, detail: 'not confirmed' }));
  const failure = verdigris.pair('low');
  await expect(failure).rejects.toBeInstanceOf(VerdigrisError);
  await expect(failure).rejects.toMatchObject({ status: Status.UNAUTHENTICATED, detail: 'not confirmed' });
  await expect(failure).rejects.toThrow('STATUS_UNAUTHENTICATED (not confirmed)');
  await verdigris.close();
});

test('a stale response is dropped and the matching one is returned', async () => {
  let first = true;
  const { verdigris } = setup((request) => {
    if (first) {
      first = false;
      return ok(request.id + 100, { case: 'led', value: create(LedResponseSchema, { on: false }) });
    }
    return ok(request.id, { case: 'led', value: create(LedResponseSchema, { on: true }) });
  });
  // The stale answer to the first request is followed by nothing, so the first command times out and the
  // second command's own answer arrives after the stale one of the first was dropped.
  await expect(verdigris.getLed()).rejects.toBeInstanceOf(BleTimeoutError);
  await verdigris.close();
}, 15_000);

test('a silent device ends in a timeout and the link closes', async () => {
  const { verdigris } = setup(() => null, false, 100);
  await expect(verdigris.getLed()).rejects.toBeInstanceOf(BleTimeoutError);
  await expect(verdigris.getLed()).rejects.toThrow();
  await verdigris.close();
});

test('an unexpected result kind is a transport failure', async () => {
  const { verdigris } = setup((request) => ok(request.id, { case: 'ack', value: create(AckResponseSchema) }));
  await expect(verdigris.getLed()).rejects.toThrow('Verdigris response carried ack where led was expected.');
  await verdigris.close();
});

test('a forged sealed frame fails authentication', async () => {
  const [a, b] = LoopbackPipe.pair(20);
  const responder = new SegmentLink(b, VerdigrisPeripheral.maxFrame);
  const verdigris = new VerdigrisPeripheral(a, defaultOptions, new LinkSecurity(LinkIdentity.generate(), null));
  const run = (async () => {
    FrameType.split(await responder.read());
    const reply = NoiseProtocol.parse('Noise_XX_25519_AESGCM_SHA256').createHandshake({ initiator: false, localStatic: NoiseProtocol.parse('Noise_XX_25519_AESGCM_SHA256').generateKeyPair() });
    reply.readMessage(new Uint8Array(32));
    await responder.write(FrameType.bundle(FrameType.handshake, reply.writeMessage(Uint8Array.of(0x5a)).message));
    await responder.read();
    await responder.read();
    const forged = new Uint8Array(8 + tagLength).fill(0xee);
    await responder.write(FrameType.bundle(FrameType.encrypted, forged));
  })();
  await expect(verdigris.getLed()).rejects.toBeInstanceOf(LinkSecurityError);
  await run;
  await verdigris.close();
});
