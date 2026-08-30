// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

// Compile-time caller info for ILogger via the Here() pattern:
//
//     logger.Here().LogInformation("user {Name} logged in", name);
//
// Here() captures [CallerFilePath]/[CallerMemberName]/[CallerLineNumber] at the call
// site (compile-time, zero runtime cost to capture) and returns a lightweight ILogger
// wrapper that prepends "File.Member:Line" to the rendered message and exposes it as
// a structured {Caller} property. Everything after .Here() is the unmodified MEL API —
// every overload, EventId, params, LoggerMessage delegates, scopes all work.
using System.Collections;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace StonyGrey.Logging;

public static class LoggerExtensions
{
    public static ILogger Here(this ILogger logger,
        [CallerFilePath] string file = "",
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
        // Trim compiler mangling, e.g. "<Main>$" for top-level statements.
        => new CallerLogger(logger, CallerTag.Format(file, member, line));

    private sealed class CallerLogger(ILogger inner, string caller) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => inner.Log(logLevel, eventId,
                new CallerState<TState>(caller, state, formatter), exception,
                CallerState<TState>.Format);
    }

    // Wraps the original state so structured sinks still see its properties,
    // plus a "Caller" property. The cached Format delegate avoids a per-call
    // closure allocation.
    private readonly struct CallerState<TState>(
        string caller, TState inner, Func<TState, Exception?, string> formatter)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public static readonly Func<CallerState<TState>, Exception?, string> Format =
            static (s, e) => $"{s.caller} {s.formatter(s.inner, e)}";

        private readonly string caller = caller;
        private readonly TState inner = inner;
        private readonly Func<TState, Exception?, string> formatter = formatter;

        private IReadOnlyList<KeyValuePair<string, object?>>? InnerList
            => inner as IReadOnlyList<KeyValuePair<string, object?>>;

        public int Count => (InnerList?.Count ?? 0) + 1;

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                var list = InnerList;
                if (list is not null && index < list.Count)
                    return list[index];
                if (index == (list?.Count ?? 0))
                    return new("Caller", caller);
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{caller} {inner}";
    }
}
