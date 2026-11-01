// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { StyleSheet, Text } from 'react-native';

import { useApp } from '../state/container';
import { useObservable } from '../state/observable';
import { usePalette } from './theme';

/** The navigation bar title of the device pages: the device's name. */
export function DeviceHeading() {
  const app = useApp();
  useObservable(app.selection);
  const palette = usePalette();
  const selected = app.selection.value;
  return (
    <Text style={[styles.name, { color: palette.text }]} testID="device-heading" numberOfLines={1}>
      {selected?.localName ?? ''}
    </Text>
  );
}

const styles = StyleSheet.create({
  name: { fontSize: 17, fontWeight: '600' },
});
