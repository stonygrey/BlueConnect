// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { View } from 'react-native';

import { AppProvider } from '../src/state/container';
import { DeviceHeading } from '../src/ui/DeviceHeading';
import { ToastHost } from '../src/ui/ToastHost';
import { usePalette } from '../src/ui/theme';

/**
 * Navigation: the scan list is the home page, and a device's pages
 * are pushed on top once a device is selected, with the navigation bar's
 * back arrow returning to the list. Toasts are overlaid on every page.
 */
export default function RootLayout() {
  const palette = usePalette();
  return (
    <AppProvider>
      <View style={{ flex: 1 }}>
        <Stack screenOptions={{ headerTintColor: palette.primary }}>
          <Stack.Screen name="index" options={{ headerShown: false }} />
          <Stack.Screen name="device" options={{ headerTitle: () => <DeviceHeading />, headerBackTitle: 'Devices' }} />
        </Stack>
        <ToastHost />
      </View>
      <StatusBar style="auto" />
    </AppProvider>
  );
}
