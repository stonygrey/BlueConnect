// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT
//
// The unit tests run under jest-expo so that the TypeScript is transformed
// the way Metro transforms it. Torann is replaced per test file with
// jest.mock, because its native module exists only in an app on a device.
module.exports = {
  preset: 'jest-expo',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts'],
};
