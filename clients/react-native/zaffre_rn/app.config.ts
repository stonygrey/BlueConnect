// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT
//
// Extends app.json with the account-specific signing value:
// signing.local.json (gitignored, copied from signing.local.json.example)
// carries the Apple team id, and `expo prebuild` writes it into the
// generated Xcode project as DEVELOPMENT_TEAM. Without the file the project
// generates unsigned, and a device build then fails at signing.
import type { ConfigContext, ExpoConfig } from 'expo/config';
import { existsSync, readFileSync } from 'fs';
import { join } from 'path';

interface LocalSigning {
  readonly appleTeamId?: string;
}

export default ({ config }: ConfigContext): ExpoConfig => {
  const path = join(__dirname, 'signing.local.json');
  const signing: LocalSigning = existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : {};
  return {
    ...config,
    name: config.name ?? 'Zaffre React',
    slug: config.slug ?? 'zaffre-react',
    ios: { ...config.ios, appleTeamId: signing.appleTeamId },
  };
};
