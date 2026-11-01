// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { useColorScheme } from 'react-native';

export interface Palette {
  readonly primary: string;
  readonly onPrimary: string;
  readonly background: string;
  readonly card: string;
  readonly text: string;
  readonly muted: string;
  readonly border: string;
  readonly warning: string;
  readonly error: string;
  readonly success: string;
}

const light: Palette = { primary: '#0014A8', onPrimary: '#FFFFFF', background: '#F4F5F9', card: '#FFFFFF', text: '#111111', muted: '#5F6368', border: '#D8DBE3', warning: '#B26A00', error: '#B3261E', success: '#2E7D32' };
const dark: Palette = { primary: '#B9C3FF', onPrimary: '#0014A8', background: '#111318', card: '#1D2026', text: '#EDEEF2', muted: '#A8ACB8', border: '#343843', warning: '#FFB74D', error: '#F28B82', success: '#81C995' };

export function usePalette(): Palette {
  return useColorScheme() === 'dark' ? dark : light;
}
