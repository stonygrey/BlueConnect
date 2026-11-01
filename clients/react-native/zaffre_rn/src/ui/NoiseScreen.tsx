// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement } from '@stonygrey/blueconnect';
import { useState } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';

import { isPaired, originLabels, tierInfo, type Tier, type TierCredential } from '../data/pairingRepository';
import { useApp } from '../state/container';
import { useObservable } from '../state/observable';
import { QrScanModal } from './QrScanModal';
import { usePalette } from './theme';
import { Body, Button, Caption, Card, Divider, Heading } from './widgets';

/** The trust tab: one pairing card per tier, High first. */
export function NoiseScreen() {
  const palette = usePalette();
  return (
    <ScrollView style={{ backgroundColor: palette.background }} contentContainerStyle={styles.content}>
      <Body>Noise XX link security; each tier is enabled independently.</Body>
      <Caption>Pair: Mutual key pinning, confirmed with the board's button.</Caption>
      <View style={styles.gap} />
      <PairingCard tier="high" />
      <PairingCard tier="low" />
    </ScrollView>
  );
}

export function PairingCard({ tier }: { tier: Tier }) {
  const app = useApp();
  useObservable(app.pairings);
  useObservable(app.commands);
  useObservable(app.loop);
  useObservable(app.selection);
  const [scanning, setScanning] = useState(false);
  const palette = usePalette();
  const credential = app.pairings.credential(tier);
  if (credential === null) {
    return (
      <Card>
        <Body>Loading credentials…</Body>
      </Card>
    );
  }
  const info = tierInfo[tier];
  const canRun = app.actions.canRun;
  const state = isPaired(credential) ? originLabels[credential.origin ?? 'paired'] : 'Unsecured';
  const result = app.commands.pairResult(tier);
  return (
    <Card>
      <Heading testID={`title-${tier}`}>
        {info.title}: {state}
      </Heading>
      <Caption testID={`status-${tier}`}>{status(tier, credential, app.selection.value)}</Caption>
      <View style={styles.gap} />
      {isPaired(credential) ? (
        <View style={styles.row}>
          <Button label="Unpair" onPress={() => app.actions.unpair(tier)} disabled={!canRun} testID={`unpair-${tier}`} />
        </View>
      ) : (
        <View style={styles.row}>
          <Button label="Pair" variant="filled" onPress={() => app.actions.pair(tier)} disabled={!canRun} testID={`pair-${tier}`} />
          <Caption style={styles.rowText}>Confirm with {info.button}</Caption>
        </View>
      )}
      <Divider />
      <View style={styles.row}>
        <Button label="Scan QR" onPress={() => setScanning(true)} disabled={!canRun} testID={`scan-${tier}`} />
        <Caption style={styles.rowText}>Provision from the QR created over USB, or an enrollment card.</Caption>
      </View>
      {result.length > 0 ? (
        <Caption testID={`result-${tier}`} color={palette.text}>
          {result}
        </Caption>
      ) : null}
      <QrScanModal
        visible={scanning}
        onClose={() => setScanning(false)}
        onResult={(text) => {
          setScanning(false);
          app.actions.provision(tier, text);
        }}
      />
    </Card>
  );
}

function status(tier: Tier, credential: TierCredential, selected: Advertisement | null): string {
  if (!isPaired(credential)) return `Not paired (${tier}). Pair to run this page's commands`;
  if (selected?.isSecured) return 'Sessions are encrypted.';
  const withName = credential.deviceName !== null ? ` with '${credential.deviceName}'` : '';
  return `Paired (${tier})${withName}, but the device reports no pairings. It may have been reset; unpair to start over.`;
}

const styles = StyleSheet.create({
  content: { padding: 16 },
  gap: { height: 8 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  rowText: { flex: 1 },
});
