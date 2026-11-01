// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { ScrollView, StyleSheet, Text, View } from 'react-native';

import { useApp } from '../state/container';
import { useObservable } from '../state/observable';
import { usePalette } from './theme';
import { Body, Button, Caption, Title } from './widgets';

/** The Loop tab: start and stop the soak loop and watch its counters. */
export function LoopScreen() {
  const app = useApp();
  useObservable(app.loop);
  useObservable(app.selection);
  const palette = usePalette();
  const loop = app.loop;
  const selected = app.selection.value;
  const started = loop.started !== null;
  const ms = (value: number | null) => (value === null ? '—' : `${value.toFixed(0)} ms`);
  const two = (v: number) => String(v).padStart(2, '0');
  const elapsed = Math.floor(loop.elapsedMs / 1000);
  const rows: [string, string, string][] = [
    ['Device', loop.deviceName.length === 0 ? '—' : loop.deviceName, 'loop-device'],
    ['Tests', started ? String(loop.tests) : '—', 'loop-tests'],
    ['Connection failures', started ? String(loop.connectionFailures) : '—', 'loop-connection-failures'],
    ['Command failures', started ? String(loop.commandFailures) : '—', 'loop-command-failures'],
    ['Min connect', ms(loop.minConnectMs), 'loop-min'],
    ['Average connect', ms(loop.avgConnectMs), 'loop-avg'],
    ['Max connect', ms(loop.maxConnectMs), 'loop-max'],
    ['Elapsed', started ? `${two(Math.floor(elapsed / 3600))}:${two(Math.floor((elapsed % 3600) / 60))}:${two(elapsed % 60)}` : '—', 'loop-elapsed'],
  ];
  return (
    <ScrollView style={{ backgroundColor: palette.background }} contentContainerStyle={styles.content}>
      <Title>{loop.isRunning ? 'Loop (running)' : 'Loop'}</Title>
      <View style={styles.row}>
        <Button
          label="Start"
          variant="filled"
          disabled={loop.isRunning || selected === null}
          onPress={() => {
            if (selected !== null) loop.start(selected);
          }}
          testID="loop-start"
        />
        <Button label="Stop" disabled={!loop.isRunning} onPress={() => loop.stop()} testID="loop-stop" />
      </View>
      <Caption>The loop echoes each cycle, which requires the high-privilege pairing.</Caption>
      <View style={styles.gap} />
      {rows.map(([label, value, tag]) => (
        <View key={tag} style={styles.stat}>
          <Body>{label}</Body>
          <Text style={[styles.value, { color: palette.text }]} testID={tag}>
            {value}
          </Text>
        </View>
      ))}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: 16 },
  row: { flexDirection: 'row', gap: 8, marginBottom: 8 },
  gap: { height: 8 },
  stat: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 4 },
  value: { fontFamily: 'Menlo', fontSize: 14 },
});
