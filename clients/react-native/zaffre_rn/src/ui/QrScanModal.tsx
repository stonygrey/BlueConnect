// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { CameraView, useCameraPermissions } from 'expo-camera';
import { useEffect, useRef } from 'react';
import { Modal, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { usePalette } from './theme';
import { Body, Button } from './widgets';

/**
 * A full-screen camera that delivers the first QR code it sees, once, then
 * closes. expo-camera decodes with AVFoundation on iOS and ML Kit on
 * Android, and asks for the camera permission through its own hook.
 */
export function QrScanModal({ visible, onResult, onClose }: { visible: boolean; onResult: (text: string) => void; onClose: () => void }) {
  const [permission, requestPermission] = useCameraPermissions();
  const delivered = useRef(false);
  const palette = usePalette();

  useEffect(() => {
    if (visible) {
      delivered.current = false;
      if (permission && !permission.granted && permission.canAskAgain) {
        requestPermission();
      }
    }
  }, [visible, permission, requestPermission]);

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <SafeAreaView style={[styles.screen, { backgroundColor: palette.background }]}>
        <View style={styles.bar}>
          <Body>Scan QR</Body>
          <Button label="Close" onPress={onClose} testID="qr-close" />
        </View>
        {permission?.granted ? (
          <CameraView
            style={styles.camera}
            facing="back"
            barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
            onBarcodeScanned={({ data }) => {
              if (delivered.current || !data) return;
              delivered.current = true;
              onResult(data);
            }}
          />
        ) : (
          <View style={styles.message}>
            <Body>{permission === null ? 'Checking the camera permission…' : 'The camera permission was not granted. Allow it in Settings to scan a QR.'}</Body>
          </View>
        )}
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  bar: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', padding: 16 },
  camera: { flex: 1 },
  message: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
});
