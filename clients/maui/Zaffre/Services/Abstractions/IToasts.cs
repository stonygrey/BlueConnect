// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace Zaffre.Services.Abstractions;

public enum ToastKind
{
    Info,
    Warning,
    Error,
}

public sealed record Toast(ToastKind Kind, string Message);

/// <summary>
/// The app-wide toast stream: view models publish outcomes here and the
/// single <c>ToastBridge</c> in the layout renders them, so every page shows
/// toasts without per-component wiring.
/// </summary>
public interface IToasts : IObservable<Toast>
{
    void Info(string message);

    void Warning(string message);

    void Error(string message);
}
