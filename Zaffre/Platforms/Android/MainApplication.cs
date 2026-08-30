// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Android.App;
using Android.Runtime;

namespace Zaffre;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
