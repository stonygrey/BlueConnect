// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Ionicons from '@expo/vector-icons/Ionicons';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native';

import { tierInfo, type Tier } from '../data/pairingRepository';
import { randomSerial } from '../domain/deviceActions';
import { useApp } from '../state/container';
import { useObservable } from '../state/observable';
import { usePalette } from './theme';
import { Button, Caption, Card, Heading, Title } from './widgets';

/** The Low and High tabs: the serial number, LED 1, and Echo cards. */
export function CommandsScreen({ tier }: { tier: Tier }) {
  const app = useApp();
  useObservable(app.commands);
  useObservable(app.loop);
  useObservable(app.selection);
  useObservable(app.pairings);
  const palette = usePalette();
  const [serial, setSerial] = useState(randomSerial);
  const [echoText, setEchoText] = useState('Hello, Verdigris!');
  const canRun = app.actions.canRun;
  const on = app.commands.ledOn(tier);
  const ledLabel = on === null ? 'LED 1 state not read yet. Tap to read it' : on ? 'LED 1 is on. Tap to switch it off' : 'LED 1 is off. Tap to switch it on';
  const inputStyle = [styles.input, { color: palette.text, borderColor: palette.border }];
  return (
    <ScrollView style={{ backgroundColor: palette.background }} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
      <Title>{tierInfo[tier].title}</Title>
      <Card>
        <Heading>Serial Number</Heading>
        <TextInput style={inputStyle} value={serial} onChangeText={setSerial} keyboardType="number-pad" placeholder="Serial number" placeholderTextColor={palette.muted} testID={`serial-input-${tier}`} />
        <View style={styles.row}>
          <Button label="Read" onPress={() => app.actions.readSerial(tier)} disabled={!canRun} testID={`serial-read-${tier}`} />
          <Button
            label="Write"
            onPress={async () => {
              if (await app.actions.writeSerial(tier, serial)) setSerial(randomSerial());
            }}
            disabled={!canRun}
            testID={`serial-write-${tier}`}
          />
        </View>
      </Card>
      <Card>
        <Heading>LED 1</Heading>
        <View style={styles.row}>
          <Pressable
            accessibilityRole="button"
            disabled={!canRun}
            onPress={() => app.actions.tapLed(tier)}
            testID={`led-${tier}`}
            style={[styles.led, { backgroundColor: on === null ? palette.muted : on ? '#FFC107' : '#9E9E9E' }, canRun ? null : styles.dim]}
          >
            {on === null ? <Ionicons name="refresh" size={22} color="#FFFFFF" /> : null}
          </Pressable>
          <Caption style={styles.rowText} testID={`led-label-${tier}`}>
            {ledLabel}
          </Caption>
          <Pressable accessibilityRole="button" accessibilityLabel="Read LED state" disabled={!canRun} onPress={() => app.actions.readLed(tier)} testID={`led-read-${tier}`} style={canRun ? null : styles.dim}>
            <Ionicons name="refresh" size={22} color={palette.primary} />
          </Pressable>
        </View>
      </Card>
      <Card>
        <Heading>Echo</Heading>
        <TextInput style={inputStyle} value={echoText} onChangeText={setEchoText} placeholder="Payload" placeholderTextColor={palette.muted} testID={`echo-input-${tier}`} />
        <View style={styles.row}>
          <Button label="Write" onPress={() => app.actions.echo(tier, echoText)} disabled={!canRun} testID={`echo-write-${tier}`} />
        </View>
      </Card>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: 16 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, marginTop: 8 },
  rowText: { flex: 1 },
  input: { borderWidth: 1, borderRadius: 8, paddingHorizontal: 12, paddingVertical: 10, fontSize: 15, marginTop: 4 },
  led: { width: 44, height: 44, borderRadius: 22, alignItems: 'center', justifyContent: 'center' },
  dim: { opacity: 0.4 },
});
