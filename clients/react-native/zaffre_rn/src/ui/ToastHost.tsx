// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import type { Toast } from '../domain/toasts';
import { useApp } from '../state/container';
import { usePalette } from './theme';

/** The snackbar: the latest toast, at the bottom, for four seconds or until tapped. */
export function ToastHost() {
  const app = useApp();
  const palette = usePalette();
  const insets = useSafeAreaInsets();
  const [toast, setToast] = useState<Toast | null>(null);

  useEffect(() => app.toasts.onMessage(setToast), [app]);

  useEffect(() => {
    if (toast === null) return;
    const timer = setTimeout(() => setToast(null), 4_000);
    return () => clearTimeout(timer);
  }, [toast]);

  if (toast === null) return null;
  const background = toast.severity === 'error' ? palette.error : toast.severity === 'warning' ? palette.warning : '#323232';
  return (
    <Pressable onPress={() => setToast(null)} style={[styles.toast, { backgroundColor: background, bottom: insets.bottom + 72 }]} testID="toast">
      <Text style={styles.text}>{toast.text}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  toast: { position: 'absolute', left: 16, right: 16, borderRadius: 8, padding: 14 },
  text: { color: '#FFFFFF', fontSize: 14 },
});
