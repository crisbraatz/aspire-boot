using System.Buffers.Binary;

using SkiaSharp;

namespace AspireBoot.Tests.Integration.Identity.Account.Avatar;

/// <summary>Builds the upload payloads used by the avatar endpoint tests, entirely in memory.</summary>
internal static class AvatarTestImages
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Jpeg() => Encode(SKEncodedImageFormat.Jpeg);

    public static byte[] Png() => Encode(SKEncodedImageFormat.Png);

    public static byte[] Webp() => Encode(SKEncodedImageFormat.Webp);

    /// <summary>Plain text with an image extension: the type check must look at the bytes, not the name.</summary>
    public static byte[] SpoofedText() => "this is not a picture, just text named .png"u8.ToArray();

    /// <summary>Starts like a JPEG but has no decodable content.</summary>
    public static byte[] CorruptJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

    /// <summary>A few hundred bytes that declare a huge image: the decompression-bomb shape.</summary>
    public static byte[] DecompressionBomb(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write(PngSignature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(stream, "IHDR"u8, header);
        WriteChunk(stream, "IDAT"u8, [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(stream, "IEND"u8, []);
        return stream.ToArray();
    }

    /// <summary>A valid JPEG signature followed by filler, so the size checks (not the decoder) decide the outcome.</summary>
    public static byte[] Oversized(int length)
    {
        var bytes = new byte[length];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return bytes;
    }

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(320, 240);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = new SKColor(40, 120, 200) };
        canvas.Clear(SKColors.White);
        canvas.DrawCircle(160, 120, 80, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var crcInput = new byte[4 + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput.AsSpan(4));
        stream.Write(type);
        stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}