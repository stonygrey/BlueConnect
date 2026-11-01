// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BlePlxScanner, defaultOptions } from '@stonygrey/blueconnect';
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

import { SecureCredentialStore } from '../data/credentialStore';
import { DeviceSelection } from '../data/deviceSelection';
import { PairingRepository } from '../data/pairingRepository';
import { CommandRunner } from '../domain/commandRunner';
import { DeviceActions } from '../domain/deviceActions';
import { SoakLoop } from '../domain/soakLoop';
import { Toasts } from '../domain/toasts';
import { ScanModel } from '../ui/scanModel';

/** The object graph of the app, built once and handed to the screens through React context. */
export interface AppContainer {
  readonly scanner: BlePlxScanner;
  readonly selection: DeviceSelection;
  readonly pairings: PairingRepository;
  readonly toasts: Toasts;
  readonly commands: CommandRunner;
  readonly loop: SoakLoop;
  readonly actions: DeviceActions;
  readonly scan: ScanModel;
}

export function createContainer(): AppContainer {
  const scanner = new BlePlxScanner(defaultOptions);
  const selection = new DeviceSelection();
  const pairings = new PairingRepository(new SecureCredentialStore());
  const toasts = new Toasts();
  const commands = new CommandRunner(scanner, defaultOptions, pairings, toasts);
  const loop = new SoakLoop(scanner, defaultOptions, pairings);
  const actions = new DeviceActions(selection, pairings, commands, loop, toasts);
  const scan = new ScanModel(scanner, selection);
  return { scanner, selection, pairings, toasts, commands, loop, actions, scan };
}

const AppContext = createContext<AppContainer | null>(null);

/** Builds the container on first render, loads the credentials, and starts the scan. */
export function AppProvider({ children }: { children: ReactNode }) {
  const [container] = useState(createContainer);
  useEffect(() => {
    container.pairings.load().catch((e: unknown) => container.toasts.error(`Credentials failed to load: ${String(e)}`));
    container.scan.start();
    return () => container.scan.stop();
  }, [container]);
  return <AppContext.Provider value={container}>{children}</AppContext.Provider>;
}

export function useApp(): AppContainer {
  const container = useContext(AppContext);
  if (container === null) {
    throw new Error('useApp must be used inside AppProvider.');
  }
  return container;
}
