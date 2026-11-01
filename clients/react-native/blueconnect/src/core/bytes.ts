// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

export function bytesEqual(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) return false;
  }
  return true;
}

export function isZero(bytes: Uint8Array): boolean {
  return bytes.every((b) => b === 0);
}

export function concatBytes(...parts: Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
}

/**
 * UTF-8 without `TextEncoder`, which Hermes provides but `TextDecoder` is not
 * guaranteed on every React Native release the clients support. Only echo
 * text and device names pass through here.
 */
export function utf8Encode(text: string): Uint8Array {
  const out: number[] = [];
  for (let i = 0; i < text.length; i++) {
    let c = text.charCodeAt(i);
    if (c >= 0xd800 && c <= 0xdbff && i + 1 < text.length) {
      const next = text.charCodeAt(i + 1);
      if (next >= 0xdc00 && next <= 0xdfff) {
        c = 0x10000 + ((c - 0xd800) << 10) + (next - 0xdc00);
        i++;
      }
    }
    if (c < 0x80) out.push(c);
    else if (c < 0x800) out.push(0xc0 | (c >> 6), 0x80 | (c & 0x3f));
    else if (c < 0x10000) out.push(0xe0 | (c >> 12), 0x80 | ((c >> 6) & 0x3f), 0x80 | (c & 0x3f));
    else out.push(0xf0 | (c >> 18), 0x80 | ((c >> 12) & 0x3f), 0x80 | ((c >> 6) & 0x3f), 0x80 | (c & 0x3f));
  }
  return Uint8Array.from(out);
}

/** Lenient: a malformed sequence becomes U+FFFD, as `allowMalformed` does in Dart. */
export function utf8Decode(bytes: Uint8Array): string {
  let out = '';
  let i = 0;
  while (i < bytes.length) {
    const b = bytes[i];
    let cp: number;
    let n: number;
    if (b < 0x80) {
      cp = b;
      n = 1;
    } else if ((b & 0xe0) === 0xc0) {
      cp = b & 0x1f;
      n = 2;
    } else if ((b & 0xf0) === 0xe0) {
      cp = b & 0x0f;
      n = 3;
    } else if ((b & 0xf8) === 0xf0) {
      cp = b & 0x07;
      n = 4;
    } else {
      out += '�';
      i++;
      continue;
    }
    if (i + n > bytes.length) {
      out += '�';
      break;
    }
    let valid = true;
    for (let k = 1; k < n; k++) {
      const c = bytes[i + k];
      if ((c & 0xc0) !== 0x80) {
        valid = false;
        break;
      }
      cp = (cp << 6) | (c & 0x3f);
    }
    if (!valid) {
      out += '�';
      i++;
      continue;
    }
    out += String.fromCodePoint(cp);
    i += n;
  }
  return out;
}
