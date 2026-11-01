// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { router } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';

import { ScanScreen } from '../src/ui/ScanScreen';

export default function Index() {
  return (
    <SafeAreaView style={{ flex: 1 }} edges={['top', 'left', 'right']}>
      <ScanScreen onSelect={() => router.push('/device/noise')} />
    </SafeAreaView>
  );
}
