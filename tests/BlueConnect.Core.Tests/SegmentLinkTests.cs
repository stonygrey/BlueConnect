// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core.Tests;

public sealed class SegmentLinkTests
{
    private const int Cap = 2304;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(38)]
    [InlineData(323)]
    public async Task Round_trip_at_default_mtu(int length)
    {
        var payload = Payload(length);
        Assert.Equal(payload, await Echo(payload, mtu: 20));
    }

    [Fact]
    public async Task Round_trip_exactly_one_segment()
        => Assert.Equal(Payload(19), await Echo(Payload(19), mtu: 20));

    [Fact]
    public async Task Round_trip_one_byte_into_a_second_segment()
        => Assert.Equal(Payload(20), await Echo(Payload(20), mtu: 20));

    [Fact]
    public async Task Round_trip_wraps_seq_modulo_16()
    {
        // 17 segments at P=20 (cap 19) forces seq to wrap.
        var payload = Payload(17 * 19);
        Assert.Equal(payload, await Echo(payload, mtu: 20));
    }

    [Fact]
    public async Task Two_messages_on_the_same_link()
    {
        var (a, b) = LoopbackPipe.Pair(mtu: 20);
        using var send = new SegmentLink(a, Cap);
        using var recv = new SegmentLink(b, Cap);

        var first = await SendAndReceive(send, recv, Payload(40));
        var second = await SendAndReceive(send, recv, Payload(7));
        Assert.Equal(Payload(40), first);
        Assert.Equal(Payload(7), second);
    }

    [Fact]
    public async Task Write_emits_start_end_and_consecutive_seq()
    {
        using var pipe = new ScriptedPipe { MTU = 20 };
        using var link = new SegmentLink(pipe, Cap);

        await link.Write(Payload(40), CancellationToken.None);

        Assert.Equal(3, pipe.Written.Count);
        Assert.True(SegmentHeader.Has(pipe.Written[0][0], SegmentHeader.Start));
        Assert.False(SegmentHeader.Has(pipe.Written[0][0], SegmentHeader.End));
        Assert.False(SegmentHeader.Has(pipe.Written[1][0], SegmentHeader.Start));
        Assert.False(SegmentHeader.Has(pipe.Written[1][0], SegmentHeader.End));
        Assert.True(SegmentHeader.Has(pipe.Written[2][0], SegmentHeader.End));
        Assert.False(SegmentHeader.Has(pipe.Written[2][0], SegmentHeader.Start));

        var seq0 = SegmentHeader.Seq(pipe.Written[0][0]);
        Assert.Equal((seq0 + 1) % 16, SegmentHeader.Seq(pipe.Written[1][0]));
        Assert.Equal((seq0 + 2) % 16, SegmentHeader.Seq(pipe.Written[2][0]));
        Assert.Equal(19, pipe.Written[0].Length - 1);
        Assert.Equal(19, pipe.Written[1].Length - 1);
        Assert.Equal(2, pipe.Written[2].Length - 1);
    }

    [Fact]
    public async Task Single_segment_sets_start_and_end()
    {
        using var pipe = new ScriptedPipe { MTU = 20 };
        using var link = new SegmentLink(pipe, Cap);

        await link.Write(Payload(3), CancellationToken.None);

        Assert.Single(pipe.Written);
        Assert.Equal(SegmentHeader.Start | SegmentHeader.End, SegmentHeader.Flags(pipe.Written[0][0]));
    }

    [Fact]
    public async Task Empty_message_is_start_end_with_no_data()
    {
        using var pipe = new ScriptedPipe { MTU = 20 };
        using var link = new SegmentLink(pipe, Cap);

        await link.Write(ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        Assert.Single(pipe.Written);
        Assert.Single(pipe.Written[0]);
        Assert.Equal(SegmentHeader.Start | SegmentHeader.End, SegmentHeader.Flags(pipe.Written[0][0]));
    }

    [Fact]
    public async Task Write_rejects_oversize_message()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, maxMessage: 8);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => link.Write(Payload(9), CancellationToken.None));
        Assert.Empty(pipe.Written);
    }

    [Fact]
    public async Task Write_rejects_non_empty_message_when_mtu_is_1()
    {
        using var pipe = new ScriptedPipe { MTU = 1 };
        using var link = new SegmentLink(pipe, Cap);

        await Assert.ThrowsAsync<BleTransportException>(
            () => link.Write(Payload(1), CancellationToken.None));
    }

    [Fact]
    public async Task Write_sends_abort_if_a_later_segment_fails()
    {
        using var pipe = new ScriptedPipe { MTU = 20 };
        pipe.FailOnWrite(1);
        using var link = new SegmentLink(pipe, Cap);

        await Assert.ThrowsAsync<BleTransportException>(
            () => link.Write(Payload(40), CancellationToken.None));

        Assert.Equal(2, pipe.Written.Count);
        Assert.True(SegmentHeader.Has(pipe.Written[0][0], SegmentHeader.Start));
        Assert.True(SegmentHeader.Has(pipe.Written[1][0], SegmentHeader.Abort));
        var startSeq = SegmentHeader.Seq(pipe.Written[0][0]);
        // Failed segment used startSeq+1; abort occupies the next slot.
        Assert.Equal((startSeq + 2) % SegmentHeader.SeqModulus, SegmentHeader.Seq(pipe.Written[1][0]));
    }

    [Fact]
    public async Task Read_empty_pdu_is_malformed()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue();

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("Malformed", ex.Message);
        Assert.True(SegmentHeader.Has(pipe.Written.Single()[0], SegmentHeader.Abort));
    }

    [Fact]
    public async Task Read_reserved_bit_is_malformed()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue((byte)(SegmentHeader.Pack(0, SegmentHeader.Start | SegmentHeader.End) | SegmentHeader.Reserved), 0xAA);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("Malformed", ex.Message);
        Assert.True(SegmentHeader.Has(pipe.Written.Single()[0], SegmentHeader.Abort));
    }

    [Fact]
    public async Task Read_orphan_continuation_fails()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue(SegmentHeader.Pack(1, SegmentHeader.End), 0xAA);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("Orphan", ex.Message);
        Assert.True(SegmentHeader.Has(pipe.Written.Single()[0], SegmentHeader.Abort));
    }

    [Fact]
    public async Task Read_sequence_gap_fails()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue(SegmentHeader.Pack(3, SegmentHeader.Start), 0xAA);
        pipe.Enqueue(SegmentHeader.Pack(5, SegmentHeader.End), 0xBB);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("gap", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(SegmentHeader.Has(pipe.Written.Single()[0], SegmentHeader.Abort));
    }

    [Fact]
    public async Task Read_abort_while_assembling_fails()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue(SegmentHeader.Pack(0, SegmentHeader.Start), 0xAA);
        pipe.Enqueue(SegmentHeader.Pack(1, SegmentHeader.Abort));

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("aborted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_abort_while_idle_is_ignored()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue(SegmentHeader.Pack(0, SegmentHeader.Abort));
        pipe.Enqueue(SegmentHeader.Pack(2, SegmentHeader.Start | SegmentHeader.End), 0x42);

        var message = await link.Read(CancellationToken.None);
        Assert.Equal(new byte[] { 0x42 }, message);
        Assert.Empty(pipe.Written);
    }

    [Fact]
    public async Task Read_start_while_assembling_discards_the_partial()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue(SegmentHeader.Pack(0, SegmentHeader.Start), 0xAA, 0xBB);
        pipe.Enqueue(SegmentHeader.Pack(9, SegmentHeader.Start | SegmentHeader.End), 0xCC);

        var message = await link.Read(CancellationToken.None);
        Assert.Equal(new byte[] { 0xCC }, message);
    }

    [Fact]
    public async Task Read_rejects_message_over_the_cap()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, maxMessage: 2);
        pipe.Enqueue(SegmentHeader.Pack(0, SegmentHeader.Start | SegmentHeader.End), 0x01, 0x02, 0x03);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("exceeds", ex.Message);
        Assert.True(SegmentHeader.Has(pipe.Written.Single()[0], SegmentHeader.Abort));
    }

    [Fact]
    public async Task Read_times_out_an_incomplete_message()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap, incompleteTimeout: TimeSpan.FromMilliseconds(40));
        pipe.Enqueue(SegmentHeader.Pack(0, SegmentHeader.Start), 0xAA);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => link.Read(CancellationToken.None));
        Assert.Contains("timed out", ex.Message);
        Assert.True(SegmentHeader.Has(pipe.Written.Single()[0], SegmentHeader.Abort));
    }

    [Fact]
    public async Task After_a_gap_the_next_read_accepts_a_fresh_start()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        pipe.Enqueue(SegmentHeader.Pack(0, SegmentHeader.Start), 0xAA);
        pipe.Enqueue(SegmentHeader.Pack(2, SegmentHeader.End), 0xBB);

        await Assert.ThrowsAsync<BleTransportException>(() => link.Read(CancellationToken.None));

        pipe.Enqueue(SegmentHeader.Pack(7, SegmentHeader.Start | SegmentHeader.End), 0xCC);
        Assert.Equal(new byte[] { 0xCC }, await link.Read(CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_an_incomplete_timeout()
    {
        using var pipe = new ScriptedPipe();
        using var link = new SegmentLink(pipe, Cap);
        using var cts = new CancellationTokenSource();
        var read = link.Read(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Empty(pipe.Written);
    }

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)i;
        }

        return bytes;
    }

    private static async Task<byte[]> Echo(byte[] payload, int mtu)
    {
        var (a, b) = LoopbackPipe.Pair(mtu);
        using var send = new SegmentLink(a, Cap);
        using var recv = new SegmentLink(b, Cap);
        return await SendAndReceive(send, recv, payload);
    }

    private static async Task<byte[]> SendAndReceive(SegmentLink send, SegmentLink recv, byte[] payload)
    {
        var receive = recv.Read(CancellationToken.None);
        await send.Write(payload, CancellationToken.None);
        return await receive;
    }
}
