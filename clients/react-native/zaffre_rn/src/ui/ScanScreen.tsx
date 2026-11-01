// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Ionicons from '@expo/vector-icons/Ionicons';
import Constants from 'expo-constants';
import type { Advertisement } from '@stonygrey/blueconnect';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { useApp } from '../state/container';
import { useObservable } from '../state/observable';
import { usePalette } from './theme';
import { Body, Caption, Card, Title } from './widgets';

/**
 * The scan list: name, identifier, padlock, and RSSI,
 * strongest first. Tapping a row selects it and hands it to `onSelect`,
 * which opens the device's pages.
 */
export function ScanScreen({ onSelect }: { onSelect: (device: Advertisement) => void }) {
  const app = useApp();
  useObservable(app.scan);
  useObservable(app.selection);
  const palette = usePalette();
  const insets = useSafeAreaInsets();
  const { devices, scanFailure } = app.scan;
  const selected = app.selection.value;
  return (
    <View style={[styles.screen, { backgroundColor: palette.background }]}>
      <View style={styles.header}>
        <Title>Select a device</Title>
      </View>
      {devices.length === 0 ? (
        <View style={styles.empty}>
          {scanFailure !== null ? (
            <Body color={palette.error}>{scanFailure}</Body>
          ) : (
            <>
              <ActivityIndicator color={palette.primary} />
              <Body>Scanning for Verdigris devices…</Body>
            </>
          )}
        </View>
      ) : (
        <FlatList
          data={devices}
          keyExtractor={(device) => device.id}
          contentContainerStyle={styles.list}
          renderItem={({ item }) => (
            <Pressable
              accessibilityRole="button"
              testID="device-row"
              onPress={() => {
                app.scan.select(item);
                onSelect(item);
              }}
            >
              <Card style={styles.row}>
                <View style={styles.rowText}>
                  <Body>{item.localName}</Body>
                  <Caption>{item.id}</Caption>
                </View>
                <Ionicons name={item.isSecured ? 'lock-closed' : 'lock-open'} size={18} color={item.isSecured ? palette.success : palette.muted} />
                {item.isConnectable ? null : <Caption color={palette.warning}> not connectable</Caption>}
                <Text style={[styles.rssi, { color: palette.text }]}>{item.rssi.valueDbm} dBm</Text>
                {item.id === selected?.id ? <Ionicons name="checkmark" size={18} color={palette.primary} /> : null}
              </Card>
            </Pressable>
          )}
        />
      )}
      <Caption testID="build-label" style={[styles.build, { bottom: insets.bottom + 6 }]}>
        {buildLabel()}
      </Caption>
    </View>
  );
}

/**
 * The version in the corner of the list, so that a phone running the app
 * shows which build it is running: the `version` of app.json, as
 * `expo-constants` carries it.
 */
function buildLabel(): string {
  return `v${Constants.expoConfig?.version ?? '?'}`;
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  header: { padding: 16 },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 8, padding: 16 },
  list: { paddingHorizontal: 16, paddingBottom: 32 },
  build: { position: 'absolute', left: 12 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  rowText: { flex: 1 },
  rssi: { fontSize: 13, marginLeft: 8 },
});
