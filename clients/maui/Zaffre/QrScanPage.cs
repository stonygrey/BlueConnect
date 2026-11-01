// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using BarcodeScanning;

namespace Zaffre;

/// <summary>
/// Full-screen modal camera page for scanning a credential QR. Completes
/// <see cref="Result"/> with the first QR's text, or null on cancel/dismiss.
/// Native MAUI (not Blazor): the camera preview is a platform view, so it
/// is presented above the BlazorWebView as a modal.
/// </summary>
public sealed class QrScanPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CameraView _camera;

    public QrScanPage()
    {
        _camera = new CameraView
        {
            BarcodeSymbologies = BarcodeFormats.QRCode,
            VibrationOnDetected = false,
        };
        _camera.OnDetectionFinished += OnDetectionFinished;

        var cancel = new Button
        {
            Text = "Cancel",
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 24),
        };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var hint = new Label
        {
            Text = "Point the camera at the credential QR",
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 24, 0, 12),
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(hint, 0, 0);
        grid.Add(_camera, 0, 1);
        grid.Add(cancel, 0, 2);
        Content = grid;
    }

    /// <summary>The scanned text; null when the page is dismissed without one.</summary>
    public Task<string?> Result => _result.Task;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _camera.CameraEnabled = true;
    }

    protected override void OnDisappearing()
    {
        _camera.CameraEnabled = false;
        // Backstop for every dismissal path (cancel button, swipe, back).
        _result.TrySetResult(null);
        base.OnDisappearing();
    }

    private void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        var text = e.BarcodeResults.FirstOrDefault()?.RawValue;
        if (string.IsNullOrEmpty(text) || _result.Task.IsCompleted)
        {
            return;
        }

        _camera.CameraEnabled = false;
        _result.TrySetResult(text);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        });
    }
}
