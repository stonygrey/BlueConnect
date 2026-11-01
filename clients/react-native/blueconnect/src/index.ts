// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/**
 * The TypeScript client library for Verdigris devices: segments, frames, the
 * Noise link over Torann, Verdigris commands, and the react-native-ble-plx
 * backend. The protocol code depends on no platform API; the backend is the
 * only part that communicates with a radio.
 */

export type { Advertisement, Rssi } from './abstractions/advertisement';
export { singleRssi } from './abstractions/advertisement';
export type { BlePeripheral, ConnectionState } from './abstractions/blePeripheral';
export type { BleScanner, ScanEvent, ScanEventType } from './abstractions/bleScanner';
export { BlueConnectConstants, defaultOptions } from './abstractions/blueConnectOptions';
export type { BlueConnectOptions } from './abstractions/blueConnectOptions';
export { BleTimeoutError, BleTransportError, BlueConnectError, InvalidStateError, LinkSecurityError } from './abstractions/errors';
export { BlePlxPeripheral } from './ble/blePlxPeripheral';
export { BlePlxScanner } from './ble/blePlxScanner';
export { AdvertisementRegistry } from './core/advertisementRegistry';
export { AsyncGate } from './core/asyncGate';
export { base64Decode, base64Encode, base64UrlDecode, base64UrlEncode } from './core/base64';
export { bytesEqual, concatBytes, isZero, utf8Decode, utf8Encode } from './core/bytes';
export { runConnectRetry } from './core/connectRetry';
export { IoLog } from './core/ioLog';
export { ReceiveQueue } from './core/receiveQueue';
export { SegmentHeader } from './core/segmentHeader';
export { SegmentLink } from './core/segmentLink';
export { DeadlineExceeded, delay, withTimeout } from './core/timeout';
export { Privilege, Status } from './generated/verdigris_pb';
export { FrameType } from './secure/frameType';
export { LinkIdentity, LinkProtocol, LinkSecurity } from './secure/linkProtocol';
export { NoiseEnrollmentPayload, NoiseQrPayload } from './secure/noisePayloads';
export { SecureLink } from './secure/secureLink';
export { privilegeToProto } from './verdigris/privilege';
export type { PrivilegeLevel } from './verdigris/privilege';
export { VerdigrisError, VerdigrisPeripheral } from './verdigris/verdigrisPeripheral';
