// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Zaffre.Components;

/// <summary>
/// Base for ALL Zaffre Razor components. Two jobs:
///
/// 1. Reactive rendering: bridges a ReactiveUI view model to Blazor — any VM
///    property change re-renders the component on the renderer's sync context.
///    Components inject exactly one VM and contain markup + bindings only.
///
/// 2. Navigation-scoped cancellation (à la meziantou.net "canceling background
///    tasks when a user navigates away from a Blazor component"): the lazily
///    created <see cref="CancellationToken"/> is cancelled when the component is
///    disposed, i.e. when the user navigates away. Pass it to every async call
///    started from the component so abandoned work stops immediately. It also
///    feeds the service layer's short-term token tier.
///
/// Disposal follows the DisposeAsyncCore pattern: Blazor prefers
/// IAsyncDisposable when both are present; subclasses override
/// <see cref="DisposeAsyncCore"/> and call base.
/// </summary>
public abstract class ReactiveComponentBase<TViewModel> : ComponentBase, IAsyncDisposable
    where TViewModel : class, INotifyPropertyChanged
{
    private CancellationTokenSource? _cts;
    private readonly HashSet<MethodInfo> _handlersInProgress = [];
    private ILogger? _logger;

    [Inject] public TViewModel ViewModel { get; set; } = null!;

    [Inject] public ILoggerFactory LoggerFactory { get; set; } = null!;

    /// <summary>Categorized by the concrete component type (e.g. "TestPage").</summary>
    protected ILogger Logger => _logger ??= LoggerFactory.CreateLogger(GetType());

    /// <summary>Cancelled when the user navigates away from this component.</summary>
    protected CancellationToken CancellationToken => (_cts ??= new CancellationTokenSource()).Token;

    protected override void OnInitialized()
        => ViewModel.PropertyChanged += OnViewModelChanged;

    protected async ValueTask FireStateHasChangedAsync()
    {
        Logger.Here().LogTrace("FireStateHasChangedAsync [{Component}@{Instance:X8}]", GetType().Name, GetHashCode());

        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.Here().LogWarning(ex, "Re-render failed.");
        }
    }
    
    /// <summary>Synchronous form of <see cref="ClickOnce(Func{Task})"/> for
    /// navigation-style handlers.</summary>
    protected Task ClickOnce(Action handler)
        => ClickOnce(() => { handler(); return Task.CompletedTask; });

    /// <summary>
    /// Run a click handler exactly once. Overlapping clicks get dropped.
    /// </summary>
    protected async Task ClickOnce(Func<Task> handler)
    {
        lock (_handlersInProgress)
        {
            if (!_handlersInProgress.Add(handler.Method))
            {
                Logger.Here().LogDebug("ClickOnce({Handler}): already in flight; click dropped.", handler.Method.Name);
                return;
            }
        }

        try
        {
            Logger.Here().LogDebug("ClickOnce({Handler})...", handler.Method.Name);

            await handler();
        }
        catch (Exception ex)
        {
            // Click handlers are the UI's outermost boundary; nothing above
            // this would observe the failure.
            Logger.Here().LogError(ex, "ClickOnce({Handler}) failed.", handler.Method.Name);
        }
        finally
        {
            lock (_handlersInProgress)
            {
                _handlersInProgress.Remove(handler.Method);
            }
        }
    }
    
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        => _ = FireStateHasChangedAsync();

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }
    }
}
