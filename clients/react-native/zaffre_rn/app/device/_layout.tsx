// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Ionicons from '@expo/vector-icons/Ionicons';
import { Tabs } from 'expo-router';

import { usePalette } from '../../src/ui/theme';

/** The selected device's pages: the Noise trust tab, the Low and High command tabs, and the Loop tab. */
export default function DeviceLayout() {
  const palette = usePalette();
  return (
    <Tabs screenOptions={{ headerShown: false, tabBarActiveTintColor: palette.primary }}>
      <Tabs.Screen name="noise" options={{ title: 'Noise', tabBarButtonTestID: 'tab-noise', tabBarIcon: ({ color, size }) => <Ionicons name="lock-closed" color={color} size={size} /> }} />
      <Tabs.Screen name="low" options={{ title: 'Low', tabBarButtonTestID: 'tab-low', tabBarIcon: ({ color, size }) => <Ionicons name="arrow-down" color={color} size={size} /> }} />
      <Tabs.Screen name="high" options={{ title: 'High', tabBarButtonTestID: 'tab-high', tabBarIcon: ({ color, size }) => <Ionicons name="arrow-up" color={color} size={size} /> }} />
      <Tabs.Screen name="loop" options={{ title: 'Loop', tabBarButtonTestID: 'tab-loop', tabBarIcon: ({ color, size }) => <Ionicons name="repeat" color={color} size={size} /> }} />
    </Tabs>
  );
}
