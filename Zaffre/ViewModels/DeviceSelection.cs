// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using ReactiveUI.Reactive;

using StonyGrey.BlueConnect.Abstractions;

namespace Zaffre.ViewModels;

/// <summary>The device chosen on the scan page, shared across view models.</summary>
public sealed class DeviceSelection : ReactiveObject
{
    private IAdvertisement? _current;

    public IAdvertisement? Current
    {
        get => _current;
        set => this.RaiseAndSetIfChanged(ref _current, value);
    }
}
