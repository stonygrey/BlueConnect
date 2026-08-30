// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Subjects;

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

public sealed class ToastService : IToasts, IDisposable
{
    private readonly Subject<Toast> _toasts = new();

    public void Info(string message) => _toasts.OnNext(new Toast(ToastKind.Info, message));

    public void Warning(string message) => _toasts.OnNext(new Toast(ToastKind.Warning, message));

    public void Error(string message) => _toasts.OnNext(new Toast(ToastKind.Error, message));

    public IDisposable Subscribe(IObserver<Toast> observer) => _toasts.Subscribe(observer);

    public void Dispose() => _toasts.Dispose();
}
