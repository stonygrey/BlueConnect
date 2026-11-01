// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { create, enumToJson, fromBinary, toBinary } from '@bufbuild/protobuf';

import type { BlePeripheral } from '../abstractions/blePeripheral';
import type { BlueConnectOptions } from '../abstractions/blueConnectOptions';
import { BleTimeoutError, BleTransportError, BlueConnectError, InvalidStateError } from '../abstractions/errors';
import { AsyncGate } from '../core/asyncGate';
import { IoLog } from '../core/ioLog';
import { DeadlineExceeded, withTimeout } from '../core/timeout';
import {
  EchoRequestSchema,
  EnrollRequestSchema,
  FibRequestSchema,
  GetLedRequestSchema,
  GetSerialNumberRequestSchema,
  GetTimeRequestSchema,
  PairRequestSchema,
  RequestSchema,
  ResponseSchema,
  SetLedRequestSchema,
  SetSerialNumberRequestSchema,
  SetTimeRequestSchema,
  SqrtRequestSchema,
  Status,
  StatusSchema,
  UnpairRequestSchema,
  type Request,
  type Response,
} from '../generated/verdigris_pb';
import { LinkProtocol, type LinkSecurity } from '../secure/linkProtocol';
import { SecureLink } from '../secure/secureLink';
import { privilegeToProto, type PrivilegeLevel } from './privilege';

type ResultCase = NonNullable<Response['result']['case']>;

/** `VerdigrisException`: the device answered with a status other than OK. */
export class VerdigrisError extends BlueConnectError {
  readonly detail: string | null;

  constructor(
    readonly status: Status,
    detail: string | null | undefined
  ) {
    const text = detail ? detail : null;
    super(`The device rejected the request: ${String(enumToJson(StatusSchema, status))}${text ? ` (${text})` : ''}`);
    this.name = 'VerdigrisError';
    this.detail = text;
  }
}

/**
 * Typed Verdigris commands over a secure link. One command at a time; each
 * request carries a fresh id and the matching response is awaited under
 * `BlueConnectOptions.commandTimeoutMs`.
 */
export class VerdigrisPeripheral {
  /**
   * Cap on a reassembled GATT message. The value is arbitrary and specific to
   * this implementation; it is not negotiated, so it must equal the firmware's
   * `SEG_MAX_MESSAGE` and the two should be kept in sync.
   */
  static readonly maxFrame = 2304;
  static readonly maxEncodedRequest = VerdigrisPeripheral.maxFrame - 1 - LinkProtocol.tagLength;

  /**
   * Longer than the firmware's 30 s pairing window (`PAIR_WINDOW_SECONDS`), an
   * arbitrary implementation value. The two should be kept in sync, so that the
   * app is still waiting when the device answers at the end of the window.
   */
  static readonly pairTimeoutMs = 40_000;

  private readonly link: SecureLink;
  private readonly commandTimeoutMs: number;
  private readonly gate = new AsyncGate();
  private nextId = 0;
  private closed = false;

  constructor(
    readonly peripheral: BlePeripheral,
    options: BlueConnectOptions,
    security: LinkSecurity
  ) {
    this.commandTimeoutMs = options.commandTimeoutMs;
    this.link = new SecureLink(peripheral, VerdigrisPeripheral.maxFrame, security);
  }

  /** The device's proven static key once a session has been established. */
  get remoteStaticKey(): Uint8Array | null {
    return this.link.remoteStaticKey;
  }

  async echo(payload: Uint8Array): Promise<Uint8Array> {
    const response = await this.invoke({ case: 'echo', value: create(EchoRequestSchema, { payload }) }, 'echo');
    return response.result.case === 'echo' ? new Uint8Array(response.result.value.payload) : new Uint8Array(0);
  }

  async squareRoot(value: number): Promise<number> {
    const response = await this.invoke({ case: 'sqrt', value: create(SqrtRequestSchema, { value }) }, 'sqrt');
    return response.result.case === 'sqrt' ? response.result.value.value : NaN;
  }

  async fibonacci(n: number): Promise<bigint> {
    const response = await this.invoke({ case: 'fib', value: create(FibRequestSchema, { n }) }, 'fib');
    return response.result.case === 'fib' ? response.result.value.value : 0n;
  }

  async getTime(): Promise<Date> {
    const response = await this.invoke({ case: 'getTime', value: create(GetTimeRequestSchema) }, 'time');
    return new Date(response.result.case === 'time' ? Number(response.result.value.unixMillis) : 0);
  }

  async setTime(time: Date): Promise<void> {
    await this.invoke({ case: 'setTime', value: create(SetTimeRequestSchema, { unixMillis: BigInt(time.getTime()) }) }, 'time');
  }

  async getLed(): Promise<boolean> {
    const response = await this.invoke({ case: 'getLed', value: create(GetLedRequestSchema) }, 'led');
    return response.result.case === 'led' && response.result.value.on;
  }

  async setLed(on: boolean): Promise<boolean> {
    const response = await this.invoke({ case: 'setLed', value: create(SetLedRequestSchema, { on }) }, 'led');
    return response.result.case === 'led' && response.result.value.on;
  }

  async setSerialNumber(serialNumber: number): Promise<string> {
    if (!Number.isInteger(serialNumber) || serialNumber < 1 || serialNumber > 65535) {
      throw new RangeError('Serial number must be a whole number from 1 to 65535.');
    }
    const response = await this.invoke({ case: 'setSerialNumber', value: create(SetSerialNumberRequestSchema, { serialNumber }) }, 'name');
    return response.result.case === 'name' ? response.result.value.name : '';
  }

  async getSerialNumber(): Promise<string> {
    const response = await this.invoke({ case: 'getSerialNumber', value: create(GetSerialNumberRequestSchema) }, 'name');
    return response.result.case === 'name' ? response.result.value.name : '';
  }

  /** Arms the device's 30 s pairing window for `privilege` and waits for the button. Returns the device key to pin. */
  async pair(privilege: PrivilegeLevel): Promise<Uint8Array> {
    await this.invoke({ case: 'pair', value: create(PairRequestSchema, { privilege: privilegeToProto(privilege) }) }, 'ack', VerdigrisPeripheral.pairTimeoutMs);
    return this.link.remoteStaticKey!;
  }

  async unpair(): Promise<void> {
    await this.invoke({ case: 'unpair', value: create(UnpairRequestSchema) }, 'ack');
  }

  async enroll(clientPublicKey: Uint8Array): Promise<void> {
    await this.invoke({ case: 'enroll', value: create(EnrollRequestSchema, { clientPublicKey }) }, 'ack');
  }

  /** Closes the link and the peripheral, disconnecting first if connected. */
  async close(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    this.link.close();
    await this.peripheral.close();
  }

  private invoke(op: Request['op'], expected: ResultCase, timeoutMs?: number): Promise<Response> {
    if (this.closed) {
      throw new InvalidStateError('The Verdigris peripheral has been closed.');
    }
    const commandTimeoutMs = timeoutMs ?? this.commandTimeoutMs;
    return this.gate.withLock(async () => {
      const id = ++this.nextId;
      const body = toBinary(RequestSchema, create(RequestSchema, { id, op }));
      if (body.length > VerdigrisPeripheral.maxEncodedRequest) {
        throw new InvalidStateError(
          `Encoded request is ${body.length} bytes; the maximum is ${VerdigrisPeripheral.maxEncodedRequest} (type + AEAD tag must fit under ${VerdigrisPeripheral.maxFrame}).`
        );
      }
      try {
        return await withTimeout(this.exchange(id, body, expected), commandTimeoutMs);
      } catch (e) {
        if (e instanceof DeadlineExceeded) {
          // The session is unusable after a timeout: a late reply would arrive
          // on the next command. Close the link so that nothing else reads it.
          this.link.close();
          throw new BleTimeoutError(`Verdigris command timed out after ${Math.round(commandTimeoutMs / 1000)}s.`);
        }
        throw e;
      }
    });
  }

  private async exchange(id: number, body: Uint8Array, expected: ResultCase): Promise<Response> {
    await this.link.send(body);
    for (;;) {
      const raw = await this.link.receive();
      let response: Response;
      try {
        response = fromBinary(ResponseSchema, raw);
      } catch (e) {
        throw new BleTransportError('Undecodable Verdigris frame; the stream is desynchronized.', e);
      }
      if (response.id !== id) {
        IoLog.debug(`Dropping stale Verdigris response ${response.id} while awaiting ${id}.`);
        continue;
      }
      if (response.status !== Status.OK) {
        throw new VerdigrisError(response.status, response.detail);
      }
      if (response.result.case !== expected) {
        throw new BleTransportError(`Verdigris response carried ${response.result.case ?? 'nothing'} where ${expected} was expected.`);
      }
      return response;
    }
  }
}
