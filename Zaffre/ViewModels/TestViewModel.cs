// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using ReactiveUI.Reactive;

using StonyGrey.BlueConnect.Abstractions;

using Zaffre.Services.Abstractions;

namespace Zaffre.ViewModels;

public sealed class TestViewModel : ViewModelBase
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly DeviceSelection _selection;
    private readonly ObservableAsPropertyHelper<ConnectionTestState> _state;
    private readonly ObservableAsPropertyHelper<string> _deviceName;
    private readonly ObservableAsPropertyHelper<bool> _canStart;
    private readonly ObservableAsPropertyHelper<bool> _canStop;

    public TestViewModel(IDiagnosticsService diagnostics, IScanner scanner, DeviceSelection selection)
    {
        _diagnostics = diagnostics;
        _selection = selection;

        _state = _diagnostics
            .ToProperty(this, x => x.State, _diagnostics.State)
            .DisposeWith(Disposables);

        // The selection is a snapshot from the moment the device was tapped;
        // follow the live scan stream so a renamed device (SetSerialNumber)
        // updates the header.
        _deviceName = _selection
            .WhenAnyValue(x => x.Current)
            .Select(current => current is null
                ? Observable.Return(string.Empty)
                : scanner
                    .Where(s => s.Advertisement.Id == current.Id)
                    .Select(s => s.Advertisement.LocalName)
                    .StartWith(current.LocalName))
            .Switch()
            .DistinctUntilChanged()
            .ToProperty(this, x => x.DeviceName, _selection.Current?.LocalName ?? string.Empty)
            .DisposeWith(Disposables);

        var isRunning = _diagnostics
            .Select(s => s.IsRunning)
            .DistinctUntilChanged();

        var hasDevice = _selection
            .WhenAnyValue(x => x.Current)
            .Select(d => d is not null);

        _canStart = isRunning
            .CombineLatest(hasDevice, (running, has) => !running && has)
            .ToProperty(this, x => x.CanStart)
            .DisposeWith(Disposables);

        _canStop = isRunning
            .ToProperty(this, x => x.CanStop)
            .DisposeWith(Disposables);

        Start = ReactiveCommand
            .CreateFromTask(ct => _diagnostics.StartConnectTest(_selection.Current!, ct))
            .DisposeWith(Disposables);
        Stop = ReactiveCommand
            .CreateFromTask(ct => _diagnostics.StopConnectTest(ct))
            .DisposeWith(Disposables);
    }

    public ConnectionTestState State => _state.Value;

    public string DeviceName => _deviceName.Value;

    public bool CanStart => _canStart.Value;

    public bool CanStop => _canStop.Value;

    public ReactiveCommand<Unit, Unit> Start { get; }

    public ReactiveCommand<Unit, Unit> Stop { get; }
}
