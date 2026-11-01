// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT
//
// Expo config plugin that makes the generated iOS app adopt the UIScene
// lifecycle. UIKit in the iOS 27 SDK stops an app at launch when it has no
// scene manifest and no scene delegate (the trap is named
// `UIApplicationEvaluateRuntimeIssueForNoSceneLifecycleAdoption`), and the
// app delegate that `expo prebuild` writes for Expo SDK 57 creates its
// window the old way. `ios/` is generated, so the change is made here on
// every prebuild rather than edited into the generated files: the manifest
// goes into Info.plist, SceneDelegate.swift is written next to
// AppDelegate.swift and added to the target, and the window creation is
// removed from AppDelegate.swift, because the scene delegate creates the
// window for its scene and starts React Native in it.
const { withDangerousMod, withInfoPlist, withXcodeProject, IOSConfig } = require('expo/config-plugins');
const fs = require('fs');
const path = require('path');

const sceneDelegate = `// Written by plugins/withSceneLifecycle.js on every prebuild; do not edit here.
internal import Expo
import React
import UIKit

/// The UIScene lifecycle, which apps built with the iOS 27 SDK must adopt.
/// The app delegate creates the React Native factory in
/// didFinishLaunchingWithOptions; this delegate creates the window for the
/// scene and starts React Native in it, and forwards URLs and user
/// activities, which UIKit delivers to the scene once scenes are adopted.
class SceneDelegate: UIResponder, UIWindowSceneDelegate {
  var window: UIWindow?

  func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
    guard let windowScene = scene as? UIWindowScene,
          let appDelegate = UIApplication.shared.delegate as? AppDelegate,
          let factory = appDelegate.reactNativeFactory else {
      return
    }
    let window = UIWindow(windowScene: windowScene)
    self.window = window
    appDelegate.window = window
    factory.startReactNative(withModuleName: "main", in: window, launchOptions: nil)
    for context in connectionOptions.urlContexts {
      _ = RCTLinkingManager.application(UIApplication.shared, open: context.url, options: [:])
    }
  }

  func scene(_ scene: UIScene, openURLContexts URLContexts: Set<UIOpenURLContext>) {
    for context in URLContexts {
      _ = RCTLinkingManager.application(UIApplication.shared, open: context.url, options: [:])
    }
  }

  func scene(_ scene: UIScene, continue userActivity: NSUserActivity) {
    _ = RCTLinkingManager.application(UIApplication.shared, continue: userActivity, restorationHandler: { _ in })
  }
}
`;

const windowCreation = /#if os\(iOS\) \|\| os\(tvOS\)\n\s*window = UIWindow\(frame: UIScreen\.main\.bounds\)\n\s*factory\.startReactNative\(\n\s*withModuleName: "main",\n\s*in: window,\n\s*launchOptions: launchOptions\)\n#endif\n/;

module.exports = function withSceneLifecycle(config) {
  config = withInfoPlist(config, (c) => {
    c.modResults.UIApplicationSceneManifest = {
      UIApplicationSupportsMultipleScenes: false,
      UISceneConfigurations: {
        UIWindowSceneSessionRoleApplication: [
          {
            UISceneConfigurationName: 'Default',
            UISceneDelegateClassName: '$(PRODUCT_MODULE_NAME).SceneDelegate',
          },
        ],
      },
    };
    return c;
  });
  config = withDangerousMod(config, [
    'ios',
    async (c) => {
      const projectName = c.modRequest.projectName ?? IOSConfig.XcodeUtils.getProjectName(c.modRequest.projectRoot);
      const sources = path.join(c.modRequest.platformProjectRoot, projectName);
      fs.writeFileSync(path.join(sources, 'SceneDelegate.swift'), sceneDelegate);
      const appDelegatePath = path.join(sources, 'AppDelegate.swift');
      const original = fs.readFileSync(appDelegatePath, 'utf8');
      if (!original.includes('SceneDelegate') && !windowCreation.test(original)) {
        throw new Error(`withSceneLifecycle: the window creation block was not found in ${appDelegatePath}; the Expo template has changed.`);
      }
      fs.writeFileSync(appDelegatePath, original.replace(windowCreation, '    // The window is created by SceneDelegate for its scene.\n'));
      return c;
    },
  ]);
  config = withXcodeProject(config, (c) => {
    const projectName = c.modRequest.projectName ?? IOSConfig.XcodeUtils.getProjectName(c.modRequest.projectRoot);
    const filepath = `${projectName}/SceneDelegate.swift`;
    if (!c.modResults.hasFile(filepath)) {
      IOSConfig.XcodeUtils.addBuildSourceFileToGroup({ filepath, groupName: projectName, project: c.modResults });
    }
    return c;
  });
  return config;
};
