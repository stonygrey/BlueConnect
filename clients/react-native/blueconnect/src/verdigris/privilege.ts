// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { Privilege } from '../generated/verdigris_pb';

/** The privilege tier a pairing grants. `Privilege` itself is the generated protobuf enum. */
export type PrivilegeLevel = 'low' | 'high';

export function privilegeToProto(level: PrivilegeLevel): Privilege {
  return level === 'high' ? Privilege.HIGH : Privilege.LOW;
}
