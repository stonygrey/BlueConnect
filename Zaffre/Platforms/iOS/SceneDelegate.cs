// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Foundation;

namespace Zaffre;

/// <summary>
/// Apps built with the iOS 27 SDK must adopt the UIScene lifecycle; iOS 27
/// terminates them at launch otherwise. The Info.plist scene manifest points
/// here. MAUI's base class does the work; this type exists to give it an
/// exported Objective-C class name that UIKit can find.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
