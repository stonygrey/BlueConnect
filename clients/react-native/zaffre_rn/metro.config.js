// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT
//
// Metro for the workspace: the app and the blueconnect package are under
// clients/react-native, and npm hoists the workspace's dependencies,
// @stonygrey/torann among them, into clients/react-native/node_modules.
// Metro bundles files only from its watch folders, so the workspace root is
// added, and modules resolve from the app's node_modules first and the
// workspace's second.
const { getDefaultConfig } = require('expo/metro-config');
const path = require('path');

const projectRoot = __dirname;
const workspaceRoot = path.resolve(projectRoot, '..');

const config = getDefaultConfig(projectRoot);

config.watchFolders = [workspaceRoot];
config.resolver.nodeModulesPaths = [path.join(projectRoot, 'node_modules'), path.join(workspaceRoot, 'node_modules')];

module.exports = config;
