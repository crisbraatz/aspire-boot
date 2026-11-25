using AspireBoot.Core.Shared;

using SkiaSharp;

namespace AspireBoot.Core.Infrastructure.Images;

/// <summary>
/// Turns an untrusted upload into a 256x256 WebP avatar. Inputs are checked by magic bytes and by the
/// codec header before any pixel is decoded, and the output is re-encoded from pixels, so no metadata survives.
/// See ADR 0004.
/// </summary>
public sealed class AvatarImageProcessor
{
    internal const int OutputSide = 256;
    internal const int MaxSide = 8000;
    internal const long MaxPixels = 24_000_000;

    private const int WebpQuality = 80;

    private readonly SKSamplingOptions _sampling = new(SKCubicResampler.Mitchell);

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private enum SourceFormat
    {
        Jpeg,
        Png,
        Webp,
    }

    public Result<byte[]> Process(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var data = SKData.Create(input);
        if (data is null)
        {
            return Result.Fail<byte[]>(AvatarImageFailures.Corrupt);
        }

        var format = Sniff(data.Span);
        if (format is null)
        {
            return Result.Fail<byte[]>(AvatarImageFailures.UnsupportedType);
        }

        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return Result.Fail<byte[]>(AvatarImageFailures.Corrupt);
        }

        var header = codec.Info;
        if (header.Width <= 0 || header.Height <= 0)
        {
            return Result.Fail<byte[]>(AvatarImageFailures.Corrupt);
        }

        if (header.Width > MaxSide || header.Height > MaxSide || (long)header.Width * header.Height > MaxPixels)
        {
            return Result.Fail<byte[]>(AvatarImageFailures.DimensionsTooLarge);
        }

        using var decoded = Decode(codec, format.Value);
        if (decoded is null)
        {
            return Result.Fail<byte[]>(AvatarImageFailures.Corrupt);
        }

        return Result.Success(Render(decoded, codec.EncodedOrigin, _sampling));
    }

    private static SourceFormat? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return SourceFormat.Jpeg;
        }

        if (bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return SourceFormat.Png;
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return SourceFormat.Webp;
        }

        return null;
    }

    private static SKBitmap? Decode(SKCodec codec, SourceFormat format)
    {
        // PNG has no scaled decode, so it is decoded at full size (already capped by the header check).
        var size = format == SourceFormat.Png ? codec.Info.Size : SmallestScaledSizeAtLeastOutput(codec);

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private static SKSizeI SmallestScaledSizeAtLeastOutput(SKCodec codec)
    {
        foreach (var scale in new[] { 0.125f, 0.25f, 0.5f })
        {
            var size = codec.GetScaledDimensions(scale);
            if (Math.Min(size.Width, size.Height) >= OutputSide)
            {
                return size;
            }
        }

        return codec.Info.Size;
    }

    private static byte[] Render(SKBitmap source, SKEncodedOrigin origin, SKSamplingOptions sampling)
    {
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapsAxes ? source.Height : source.Width;
        var height = swapsAxes ? source.Width : source.Height;

        // Center square in the oriented space, then scale it to the output side.
        var side = Math.Min(width, height);
        var scale = OutputSide / (float)side;
        var offsetX = (width - side) / 2f;
        var offsetY = (height - side) / 2f;
        var toOutput = SKMatrix.CreateScaleTranslation(scale, scale, -offsetX * scale, -offsetY * scale);

        var orientation = OrientationMatrix(origin, source.Width, source.Height);

        using var surface = SKSurface.Create(new SKImageInfo(OutputSide, OutputSide, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(toOutput);
        canvas.Concat(in orientation);

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, sampling);

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        return encoded.ToArray();
    }

    /// <summary>Maps stored pixel coordinates to displayed coordinates for an EXIF orientation.</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}