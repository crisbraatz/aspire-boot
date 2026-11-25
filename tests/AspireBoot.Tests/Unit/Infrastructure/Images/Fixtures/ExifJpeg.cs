using System.Text;

using SkiaSharp;

namespace AspireBoot.Tests.Unit.Infrastructure.Images.Fixtures;

/// <summary>Builds JPEG inputs that carry an Orientation tag and a GPS IFD with a unique marker string.</summary>
internal static class ExifJpeg
{
    /// <summary>ASCII marker stored in the GPS ProcessingMethod field. It must never reach the output.</summary>
    public const string GpsMarker = "GPS-TEST-MARKER";

    private const uint GpsIfdOffset = 38;
    private const uint GpsDataOffset = 68;

    public static byte[] Create(SKBitmap source, ushort orientation)
    {
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        var jpeg = encoded.ToArray();
        var app1 = BuildApp1(orientation);

        var result = new byte[jpeg.Length + app1.Length];
        jpeg.AsSpan(0, 2).CopyTo(result);
        app1.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + app1.Length));
        return result;
    }

    private static byte[] BuildApp1(ushort orientation)
    {
        var payload = new MemoryStream();
        payload.Write("Exif\0\0"u8);
        WriteTiff(payload, orientation);
        var body = payload.ToArray();

        var segment = new MemoryStream();
        segment.WriteByte(0xFF);
        segment.WriteByte(0xE1);
        var length = body.Length + 2;
        segment.WriteByte((byte)(length >> 8));
        segment.WriteByte((byte)length);
        segment.Write(body);
        return segment.ToArray();
    }

    private static void WriteTiff(Stream stream, ushort orientation)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write((uint)8);

        // IFD0: Orientation and a pointer to the GPS IFD.
        writer.Write((ushort)2);
        writer.Write((ushort)0x0112);
        writer.Write((ushort)3);
        writer.Write((uint)1);
        writer.Write((uint)orientation);
        writer.Write((ushort)0x8825);
        writer.Write((ushort)4);
        writer.Write((uint)1);
        writer.Write(GpsIfdOffset);
        writer.Write((uint)0);

        // GPS IFD: latitude reference and a ProcessingMethod string stored out of line.
        var gpsData = Encoding.ASCII.GetBytes("ASCII\0\0\0" + GpsMarker);
        writer.Write((ushort)2);
        writer.Write((ushort)0x0001);
        writer.Write((ushort)2);
        writer.Write((uint)2);
        writer.Write((uint)'N');
        writer.Write((ushort)0x001B);
        writer.Write((ushort)7);
        writer.Write((uint)gpsData.Length);
        writer.Write(GpsDataOffset);
        writer.Write((uint)0);
        writer.Write(gpsData);
    }
}