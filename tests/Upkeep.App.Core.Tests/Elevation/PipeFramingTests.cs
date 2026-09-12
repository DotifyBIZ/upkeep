using System.Buffers.Binary;
using Upkeep.App.Core.Elevation;

namespace Upkeep.App.Core.Tests.Elevation;

public class PipeFramingTests
{
    [Fact]
    public async Task WriteFrame_ThenReadFrame_RoundTripsTheRequest()
    {
        using var stream = new MemoryStream();
        await PipeFraming.WriteFrameAsync(stream, (HelperRequest)new PingRequest { RequestId = 42 }, CancellationToken.None);
        stream.Position = 0;

        var read = await PipeFraming.ReadFrameAsync<HelperRequest>(stream, CancellationToken.None);

        var ping = Assert.IsType<PingRequest>(read);
        Assert.Equal(42, ping.RequestId);
    }

    [Fact]
    public async Task ReadFrame_ResponsesKeepTheirDerivedType()
    {
        using var stream = new MemoryStream();
        await PipeFraming.WriteFrameAsync(
            stream,
            (HelperResponse)new HelperErrorResponse(HelperErrorCodes.RefusedByPolicy, "nope") { RequestId = 7 },
            CancellationToken.None);
        stream.Position = 0;

        var read = await PipeFraming.ReadFrameAsync<HelperResponse>(stream, CancellationToken.None);

        var error = Assert.IsType<HelperErrorResponse>(read);
        Assert.Equal(HelperErrorCodes.RefusedByPolicy, error.Code);
        Assert.Equal(7, error.RequestId);
    }

    [Fact]
    public async Task ReadFrame_EmptyStream_ReturnsNullForACleanClose()
    {
        // The peer closing the pipe is how a session ends normally, not a failure to report.
        using var stream = new MemoryStream();

        var read = await PipeFraming.ReadFrameAsync<HelperRequest>(stream, CancellationToken.None);

        Assert.Null(read);
    }

    [Fact]
    public async Task ReadFrame_LengthLargerThanTheCap_IsRejectedWithoutAllocating()
    {
        // Both ends read a length the other end controls; an attacker-chosen prefix is the classic
        // way to turn that into an allocation the size of available memory.
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, PipeFraming.MaxFrameBytes + 1);
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => PipeFraming.ReadFrameAsync<HelperRequest>(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadFrame_NonPositiveLength_IsRejected(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => PipeFraming.ReadFrameAsync<HelperRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_StreamEndsMidFrame_IsRejected()
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 64);
        using var stream = new MemoryStream([.. header, .. new byte[10]]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => PipeFraming.ReadFrameAsync<HelperRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_HeaderEndsMidway_IsRejected()
    {
        using var stream = new MemoryStream([1, 2]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => PipeFraming.ReadFrameAsync<HelperRequest>(stream, CancellationToken.None));
    }
}
