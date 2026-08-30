// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using ReactiveUI.Reactive;

using StonyGrey.BlueConnect.Abstractions;

namespace Zaffre.ViewModels;

public sealed class ScanViewModel : ViewModelBase
{
    private readonly IScanner _scanner;
    private readonly DeviceSelection _selection;
    private IReadOnlyList<IAdvertisement> _devices = [];

    public ScanViewModel(IScanner scanner, DeviceSelection selection)
    {
        _scanner = scanner;
        _selection = selection;
        _scanner
            .Sample(TimeSpan.FromMilliseconds(500))
            .Select(_ => Snapshot())
            .Subscribe(devices => Devices = devices)
            .DisposeWith(Disposables);
        Devices = Snapshot();

        // Fire-and-forget: the device list populates through the subscription
        // above once the central powers on and scanning begins.
        _ = _scanner.Start();
    }

    public IReadOnlyList<IAdvertisement> Devices
    {
        get => _devices;
        private set => this.RaiseAndSetIfChanged(ref _devices, value);
    }

    public bool IsScanning => _scanner.IsScanning;

    public void Select(IAdvertisement advertisement) => _selection.Current = advertisement;

    private IReadOnlyList<IAdvertisement> Snapshot()
        => [.. _scanner.Peripherals.OrderByDescending(a => a.Rssi.ValueDbm)];
}
