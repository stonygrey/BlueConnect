// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

const standard = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
const url = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';

function encode(bytes: Uint8Array, alphabet: string, pad: boolean): string {
  let out = '';
  for (let i = 0; i < bytes.length; i += 3) {
    const a = bytes[i];
    const b = i + 1 < bytes.length ? bytes[i + 1] : 0;
    const c = i + 2 < bytes.length ? bytes[i + 2] : 0;
    const triple = (a << 16) | (b << 8) | c;
    out += alphabet[(triple >> 18) & 0x3f] + alphabet[(triple >> 12) & 0x3f];
    out += i + 1 < bytes.length ? alphabet[(triple >> 6) & 0x3f] : pad ? '=' : '';
    out += i + 2 < bytes.length ? alphabet[triple & 0x3f] : pad ? '=' : '';
  }
  return out;
}

function decode(text: string, alphabet: string): Uint8Array | null {
  const trimmed = text.replace(/=+$/, '');
  if (trimmed.length % 4 === 1) return null;
  const out: number[] = [];
  let buffer = 0;
  let bits = 0;
  for (const ch of trimmed) {
    const value = alphabet.indexOf(ch);
    if (value < 0) return null;
    buffer = (buffer << 6) | value;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      out.push((buffer >> bits) & 0xff);
    }
  }
  return Uint8Array.from(out);
}

/** Standard base64 with padding, the encoding react-native-ble-plx and expo-secure-store values use. */
export function base64Encode(bytes: Uint8Array): string {
  return encode(bytes, standard, true);
}

/** Null for text that is not standard base64. Missing padding is accepted. */
export function base64Decode(text: string): Uint8Array | null {
  return decode(text, standard);
}

/** Base64url without padding, as the QR payloads carry it. */
export function base64UrlEncode(bytes: Uint8Array): string {
  return encode(bytes, url, false);
}

/** Null for text that is not base64url. Padded input is accepted. */
export function base64UrlDecode(text: string): Uint8Array | null {
  return decode(text, url);
}
