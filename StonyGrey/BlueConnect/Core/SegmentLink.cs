// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core;

/// <summary>
/// One-byte GATT segment header: <c>seq</c> in bits 7–4, reserved bit 3,
/// <c>ABORT</c> / <c>END</c> / <c>START</c> in bits 2–0. See
/// <c>documents/ble-framing.md</c>.
/// </summary>
public static class SegmentHeader
{
    public const byte Start = 0x01;
    public const byte End = 0x02;
    public const byte Abort = 0x04;
    public const byte Reserved = 0x08;
    public const int SeqModulus = 16;

    public static byte Pack(int seq, byte flags)
        => (byte)(((seq & 0x0F) << 4) | (flags & 0x07));

    public static int Seq(byte header) => header >> 4;

    public static byte Flags(byte header) => (byte)(header & 0x0F);

    public static bool Has(byte header, byte flag) => (header & flag) != 0;
}

/// <summary>
/// Message pipe over a peripheral: one <see cref="Write"/> is one buffer,
/// one <see cref="Read"/> returns one complete buffer. Each ATT value is a
/// self-describing segment. The caller owns the peripheral.
/// </summary>
public sealed class SegmentLink : IDisposable
{
    public static readonly TimeSpan DefaultIncompleteTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Bound on a best-effort <c>ABORT</c> after a failed <see cref="Write"/>
    /// (which cannot use the caller's already-cancelled token).</summary>
    public static readonly TimeSpan AbortWriteTimeout = TimeSpan.FromSeconds(2);

    private readonly IPeripheral _peripheral;
    private readonly int _maxMessage;
    private readonly TimeSpan _incompleteTimeout;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly List<byte> _acc = [];
    private bool _assembling;
    private int _seqIn;

    public SegmentLink(IPeripheral peripheral, int maxMessage, TimeSpan? incompleteTimeout = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(peripheral);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxMessage, 0);

        _peripheral = peripheral;
        _maxMessage = maxMessage;
        _incompleteTimeout = incompleteTimeout ?? DefaultIncompleteTimeout;
        _logger = logger;
    }

    public async Task Write(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        if (message.Length > _maxMessage)
        {
            throw new InvalidOperationException($"Message is {message.Length} bytes; the maximum is {_maxMessage}.");
        }

        await _send.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payloadMtu = Math.Max(1, _peripheral.MTU);
            var cap = Math.Max(0, payloadMtu - 1);
            if (cap < 1 && message.Length > 0)
            {
                throw new BleTransportException("ATT payload is too small to carry a segment.");
            }

            var seq = Random.Shared.Next(SegmentHeader.SeqModulus);
            var offset = 0;
            var first = true;
            try
            {
                do
                {
                    var n = Math.Min(cap, message.Length - offset);
                    var last = offset + n == message.Length;
                    byte flags = 0;
                    if (first)
                    {
                        flags |= SegmentHeader.Start;
                    }

                    if (last)
                    {
                        flags |= SegmentHeader.End;
                    }

                    var segment = new byte[1 + n];
                    segment[0] = SegmentHeader.Pack(seq, flags);
                    if (n > 0)
                    {
                        message.Span.Slice(offset, n).CopyTo(segment.AsSpan(1));
                    }

                    _logger?.Here()?.LogInformation("{Line}", IoLog.Segment("write", seq, flags, segment));
                    await _peripheral.Write(segment, cancellationToken).ConfigureAwait(false);
                    offset += n;
                    first = false;
                    seq = (seq + 1) % SegmentHeader.SeqModulus;
                }
                while (offset < message.Length);
            }
            catch
            {
                // The caller's token may already be cancelled; do not hang the abort.
                using var abortTimeout = new CancellationTokenSource(AbortWriteTimeout);
                await TrySendAbortCore((seq + 1) % SegmentHeader.SeqModulus, abortTimeout.Token)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _send.Release();
        }
    }

    public async Task<byte[]> Read(CancellationToken cancellationToken)
    {
        ResetRx();

        while (true)
        {
            byte[] pdu;
            if (_assembling)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_incompleteTimeout);
                try
                {
                    pdu = await _peripheral.Read(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    ResetRx();
                    await TrySendAbort(0, cancellationToken).ConfigureAwait(false);
                    throw new BleTransportException("Incomplete message: timed out waiting for the next segment.");
                }
            }
            else
            {
                pdu = await _peripheral.Read(cancellationToken).ConfigureAwait(false);
            }

            LogInbound(pdu);

            if (pdu.Length < 1 || SegmentHeader.Has(pdu[0], SegmentHeader.Reserved))
            {
                ResetRx();
                await TrySendAbort(0, cancellationToken).ConfigureAwait(false);
                throw new BleTransportException("Malformed segment header.");
            }

            var header = pdu[0];
            if (SegmentHeader.Has(header, SegmentHeader.Abort))
            {
                var wasAssembling = _assembling;
                ResetRx();
                if (wasAssembling)
                {
                    throw new BleTransportException("Peer aborted the message.");
                }

                continue;
            }

            var seq = SegmentHeader.Seq(header);
            var data = pdu.AsSpan(1);

            if (SegmentHeader.Has(header, SegmentHeader.Start))
            {
                _assembling = true;
                _seqIn = seq;
                _acc.Clear();
                _acc.AddRange(data.ToArray());
            }
            else
            {
                if (!_assembling)
                {
                    await TrySendAbort(0, cancellationToken).ConfigureAwait(false);
                    throw new BleTransportException("Orphan continuation segment.");
                }

                if (seq != (_seqIn + 1) % SegmentHeader.SeqModulus)
                {
                    ResetRx();
                    await TrySendAbort(0, cancellationToken).ConfigureAwait(false);
                    throw new BleTransportException("Segment sequence gap.");
                }

                _seqIn = seq;
                _acc.AddRange(data.ToArray());
            }

            if (_acc.Count > _maxMessage)
            {
                ResetRx();
                await TrySendAbort(0, cancellationToken).ConfigureAwait(false);
                throw new BleTransportException($"Message exceeds the {_maxMessage}-byte cap.");
            }

            if (SegmentHeader.Has(header, SegmentHeader.End))
            {
                var complete = _acc.ToArray();
                ResetRx();
                return complete;
            }
        }
    }

    private void LogInbound(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length == 0)
        {
            _logger?.Here()?.LogInformation("{Line}", IoLog.Segment("read", 0, 0, pdu));
            return;
        }

        var header = pdu[0];
        _logger?.Here()?.LogInformation("{Line}", IoLog.Segment("read", SegmentHeader.Seq(header), SegmentHeader.Flags(header), pdu));
    }

    private void ResetRx()
    {
        _assembling = false;
        _acc.Clear();
    }

    public void Dispose() => _send.Dispose();

    private async Task TrySendAbort(int seq, CancellationToken cancellationToken)
    {
        try
        {
            await _send.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await TrySendAbortCore(seq, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _send.Release();
            }
        }
        catch (Exception)
        {
            // Best effort: the Read/Write that triggered this already failed.
        }
    }

    private async Task TrySendAbortCore(int seq, CancellationToken cancellationToken)
    {
        try
        {
            var abort = new[] { SegmentHeader.Pack(seq, SegmentHeader.Abort) };
            _logger?.Here()?.LogInformation("{Line}", IoLog.Segment("write", seq, SegmentHeader.Abort, abort));
            await _peripheral.Write(abort, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: the Read/Write that triggered this already failed.
        }
    }
}
