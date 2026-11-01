// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { ReactNode } from 'react';
import { Pressable, StyleSheet, Text, View, type StyleProp, type ViewStyle } from 'react-native';

import { usePalette } from './theme';

export function Card({ children, style, testID }: { children: ReactNode; style?: StyleProp<ViewStyle>; testID?: string }) {
  const palette = usePalette();
  return (
    <View style={[styles.card, { backgroundColor: palette.card, borderColor: palette.border }, style]} testID={testID}>
      {children}
    </View>
  );
}

export function Title({ children, testID }: { children: ReactNode; testID?: string }) {
  const palette = usePalette();
  return (
    <Text style={[styles.title, { color: palette.text }]} testID={testID}>
      {children}
    </Text>
  );
}

export function Heading({ children, testID }: { children: ReactNode; testID?: string }) {
  const palette = usePalette();
  return (
    <Text style={[styles.heading, { color: palette.text }]} testID={testID}>
      {children}
    </Text>
  );
}

export function Body({ children, testID, color }: { children: ReactNode; testID?: string; color?: string }) {
  const palette = usePalette();
  return (
    <Text style={[styles.body, { color: color ?? palette.text }]} testID={testID}>
      {children}
    </Text>
  );
}

export function Caption({ children, testID, color, style }: { children: ReactNode; testID?: string; color?: string; style?: StyleProp<ViewStyle> }) {
  const palette = usePalette();
  return (
    <Text style={[styles.caption, { color: color ?? palette.muted }, style]} testID={testID}>
      {children}
    </Text>
  );
}

export function Divider() {
  const palette = usePalette();
  return <View style={[styles.divider, { backgroundColor: palette.border }]} />;
}

/** The two button looks: filled for the main action of a card, outlined for the rest. */
export function Button({ label, onPress, disabled, variant = 'outlined', testID }: { label: string; onPress: () => void; disabled?: boolean; variant?: 'filled' | 'outlined'; testID?: string }) {
  const palette = usePalette();
  const filled = variant === 'filled';
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: disabled === true }}
      disabled={disabled}
      onPress={onPress}
      testID={testID}
      style={({ pressed }) => [
        styles.button,
        filled ? { backgroundColor: palette.primary } : { borderWidth: 1, borderColor: palette.primary },
        disabled ? styles.disabled : null,
        pressed ? styles.pressed : null,
      ]}
    >
      <Text style={[styles.buttonLabel, { color: filled ? palette.onPrimary : palette.text }]}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: { borderRadius: 12, borderWidth: StyleSheet.hairlineWidth, padding: 16, marginBottom: 12 },
  title: { fontSize: 22, fontWeight: '600', marginBottom: 8 },
  heading: { fontSize: 16, fontWeight: '600', marginBottom: 4 },
  body: { fontSize: 15 },
  caption: { fontSize: 12 },
  divider: { height: StyleSheet.hairlineWidth, marginVertical: 12 },
  button: { paddingHorizontal: 16, paddingVertical: 10, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  buttonLabel: { fontSize: 14, fontWeight: '600' },
  disabled: { opacity: 0.4 },
  pressed: { opacity: 0.7 },
});
