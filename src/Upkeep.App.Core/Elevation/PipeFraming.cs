using System.Buffers.Binary;
using System.Text.Json;

namespace Upkeep.App.Core.Elevation;

/// <summary>
/// Length-prefixed JSON frames over the helper pipe: a 4-byte little-endian length followed by
/// that many UTF-8 bytes. Pure stream work, so it is tested against a MemoryStream rather than a
/// real pipe.
/// </summary>
public static class PipeFraming
{
    /// <summary>
    /// Hard ceiling on a single frame. Both ends read from a stream the other end controls, and an
    /// attacker-chosen length prefix is the classic way to turn that into an allocation the size
    /// of available memory.
    /// </summary>
    public const int MaxFrameBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public static async Task WriteFrameAsync<T>(Stream stream, T message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        if (payload.Length > MaxFrameBytes)
        {
            throw new InvalidOperationException($"Frame of {payload.Length} bytes exceeds the {MaxFrameBytes}-byte limit.");
        }

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads one frame. Returns <see langword="null"/> when the peer closed the pipe cleanly —
    /// the normal way a session ends, not an error.
    /// </summary>
    public static async Task<T?> ReadFrameAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken))
        {
            return null;
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxFrameBytes)
        {
            throw new InvalidDataException($"Frame length {length} is outside the accepted range.");
        }

        byte[] payload = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, payload, cancellationToken))
        {
            throw new InvalidDataException("The pipe closed part-way through a frame.");
        }

        return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (chunk == 0)
            {
                return read != 0
                    ? throw new InvalidDataException("The pipe closed part-way through a frame.")
                    : false;
            }

            read += chunk;
        }

        return true;
    }
}
