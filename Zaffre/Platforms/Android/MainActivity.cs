// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Zaffre;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int BluetoothPermissionsRequest = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestBluetoothPermissions();
    }

    private void RequestBluetoothPermissions()
    {
        string[] required = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? [Android.Manifest.Permission.BluetoothScan, Android.Manifest.Permission.BluetoothConnect]
            : [Android.Manifest.Permission.AccessFineLocation];

        var missing = required
            .Where(p => CheckSelfPermission(p) != Permission.Granted)
            .ToArray();

        if (missing.Length > 0)
        {
            RequestPermissions(missing, BluetoothPermissionsRequest);
        }
    }
}
