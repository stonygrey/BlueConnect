// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using MetroLog.Maui;

namespace Zaffre;

public partial class App : Application
{
	// Held for the app's lifetime: it owns the accelerometer subscription
	// behind shake-to-view.
	private LogController? _logController;

	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var mainPage = new MainPage();

		// MetroLog's log viewer is pushed modally over the Blazor host page;
		// shake the device to open it (where there is an accelerometer —
		// Catalyst and Windows keep the page reachable but not the gesture).
		LogController.InitializeNavigation(
			page => mainPage.Navigation.PushModalAsync(page),
			() => mainPage.Navigation.PopModalAsync());
		_logController = new LogController
		{
			IsShakeEnabled = Accelerometer.Default.IsSupported,
		};

		return new Window(mainPage) { Title = "Zaffre" };
	}
}
