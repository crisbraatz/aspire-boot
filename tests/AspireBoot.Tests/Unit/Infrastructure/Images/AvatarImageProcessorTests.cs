using System.Buffers.Binary;
using System.Text;

using AspireBoot.Core.Infrastructure.Images;
using AspireBoot.Core.Shared;
using AspireBoot.Tests.Unit.Infrastructure.Images.Fixtures;

using SkiaSharp;

namespace AspireBoot.Tests.Unit.Infrastructure.Images;

[Trait("Category", "Unit")]
public sealed class AvatarImageProcessorTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly AvatarImageProcessor _processor = new();

    [Fact]
    public void Run_Jpeg_ReturnsA256SquareWebp()
    {
        var input = Encode(SKEncodedImageFormat.Jpeg, 640, 480);

        var result = Run(input);

        Assert.True(result.IsSuccess);
        AssertAvatarWebp(result.Value);
    }

    [Fact]
    public void Run_Png_ReturnsA256SquareWebp()
    {
        var input = Encode(SKEncodedImageFormat.Png, 320, 320);

        var result = Run(input);

        Assert.True(result.IsSuccess);
        AssertAvatarWebp(result.Value);
    }

    [Fact]
    public void Run_Webp_ReturnsA256SquareWebp()
    {
        var input = Encode(SKEncodedImageFormat.Webp, 300, 200);

        var result = Run(input);

        Assert.True(result.IsSuccess);
        AssertAvatarWebp(result.Value);
    }

    [Fact]
    public void Run_LargeJpeg_DecodesAndReturnsAnAvatar()
    {
        var input = Encode(SKEncodedImageFormat.Jpeg, 2400, 1600);

        var result = Run(input);

        Assert.True(result.IsSuccess);
        AssertAvatarWebp(result.Value);
    }

    [Fact]
    public void Run_TextRenamedToPng_ReturnsUnsupportedType()
    {
        var input = "this is not an image, only text"u8.ToArray();

        var result = Run(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(AvatarImageFailures.UnsupportedType, result.Failure);
    }

    [Fact]
    public void Run_Gif_ReturnsUnsupportedType()
    {
        var input = "GIF89a\u0001\0\u0001\0\u0080\0\0"u8.ToArray();

        var result = Run(input);

        Assert.Equal(AvatarImageFailures.UnsupportedType, result.Failure);
    }

    [Fact]
    public void Run_JpegHeaderWithGarbageBody_ReturnsCorrupt()
    {
        var input = new byte[] { 0xFF, 0xD8, 0xFF }.Concat(new byte[64]).ToArray();

        var result = Run(input);

        Assert.Equal(AvatarImageFailures.Corrupt, result.Failure);
    }

    [Fact]
    public void Run_PngDeclaringHugeDimensionsWithTinyBody_ReturnsDimensionsTooLargeBeforeDecoding()
    {
        var input = BuildPng(9000, 9000);

        var result = Run(input);

        Assert.Equal(AvatarImageFailures.DimensionsTooLarge, result.Failure);
    }

    [Fact]
    public void Run_PngOverTwentyFourMegapixelsUnderTheSideCap_ReturnsDimensionsTooLarge()
    {
        var input = BuildPng(6000, 5000);

        var result = Run(input);

        Assert.Equal(AvatarImageFailures.DimensionsTooLarge, result.Failure);
    }

    [Fact]
    public void Run_ExifAndGpsMetadata_AreAbsentFromTheOutput()
    {
        using var source = SplitBitmap(200, 100);
        var input = ExifJpeg.Create(source, orientation: 1);
        Assert.True(Contains(input, ExifJpeg.GpsMarker), "The fixture must carry the GPS marker.");
        Assert.True(Contains(input, "Exif"), "The fixture must carry an EXIF segment.");

        var result = Run(input);

        Assert.True(result.IsSuccess);
        AssertAvatarWebp(result.Value);
        Assert.False(Contains(result.Value, ExifJpeg.GpsMarker));
        Assert.False(Contains(result.Value, "EXIF"));
        Assert.False(Contains(result.Value, "Exif"));
        Assert.False(Contains(result.Value, "XMP "));
    }

    [Fact]
    public void Run_ExifOrientationSix_RotatesBeforeCropping()
    {
        // Stored as 200x100: the left half is red. Orientation 6 turns it into the top half.
        using var source = SplitBitmap(200, 100);
        var input = ExifJpeg.Create(source, orientation: 6);

        var result = Run(input);

        Assert.True(result.IsSuccess);
        using var avatar = SKBitmap.Decode(result.Value);
        var top = avatar.GetPixel(128, 32);
        var bottom = avatar.GetPixel(128, 220);
        Assert.True(top.Red > 180 && top.Blue < 90, $"Top should be red, was {top}.");
        Assert.True(bottom.Blue > 180 && bottom.Red < 90, $"Bottom should be blue, was {bottom}.");
    }

    [Fact]
    public void Failures_UseCamelCaseCodesMatchingTheFrontend()
    {
        Assert.Equal("avatar.unsupportedType", AvatarImageFailures.UnsupportedType.Code);
        Assert.Equal("avatar.corrupt", AvatarImageFailures.Corrupt.Code);
        Assert.Equal("avatar.dimensionsTooLarge", AvatarImageFailures.DimensionsTooLarge.Code);
    }

    private Result<byte[]> Run(byte[] input) => _processor.Process(new MemoryStream(input));

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = new SKColor(40, 120, 200) };
        canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static SKBitmap SplitBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var blue = new SKPaint { Color = SKColors.Blue };
        using var red = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(0, 0, width, height, blue);
        canvas.DrawRect(0, 0, width / 2f, height, red);
        return bitmap;
    }

    private static void AssertAvatarWebp(byte[] bytes)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(256, decoded.Width);
        Assert.Equal(256, decoded.Height);
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, string needle) =>
        haystack.IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    private static byte[] BuildPng(int width, int height)
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