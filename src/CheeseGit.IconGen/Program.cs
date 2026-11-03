using SkiaSharp;

// cheesegit-icongen <in.png> <out.ico|out.icns>...
// .ico: sizes below 256 are 32-bit DIBs, 256 is PNG: the layout Windows' own icons use, which every consumer
// (the compiler's Win32 resources, Velopack's Setup.exe, Explorer) reads.
// .icns: PNG entries for 16–512 px and their @2x variants up to the source size (no upscaling).
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: cheesegit-icongen <in.png> <out.ico|out.icns>...");
    return 2;
}

using var source = SKImage.FromEncodedData(args[0]) ?? throw new InvalidDataException($"not an image: {args[0]}");
foreach (var target in args.Skip(1))
{
    var bytes = Path.GetExtension(target).ToLowerInvariant() switch
    {
        ".ico" => WriteIco(source),
        ".icns" => WriteIcns(source),
        var other => throw new ArgumentException($"unknown icon format: {other}"),
    };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
    File.WriteAllBytes(target, bytes);
}
return 0;

static byte[] WriteIco(SKImage source)
{
    // The sizes Explorer, the taskbar and Start ask for at 100–250% scaling.
    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    var images = sizes.Select(s => (Size: s, Data: s == 256 ? EncodePng(source, s) : EncodeDib(source, s))).ToList();

    var output = new MemoryStream();
    using (var w = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        w.Write((ushort)0); // reserved
        w.Write((ushort)1); // type: icon
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); // palette size
            w.Write((byte)0); // reserved
            w.Write((ushort)1); // planes
            w.Write((ushort)32); // bits per pixel
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
    }
    return output.ToArray();
}

static byte[] WriteIcns(SKImage source)
{
    // OSType and pixel size; the @2x types (ic11–ic14) hold twice their point size.
    (string Type, int Size)[] entries =
        [("icp4", 16), ("ic11", 32), ("icp5", 32), ("ic12", 64), ("icp6", 64), ("ic07", 128), ("ic13", 256), ("ic08", 256), ("ic14", 512), ("ic09", 512)];
    var chunks = entries.Where(e => e.Size <= Math.Min(source.Width, source.Height))
        .Select(e => (e.Type, Data: EncodePng(source, e.Size))).ToList();
    var output = new MemoryStream();
    void BigEndian(int value)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b, value);
        output.Write(b);
    }
    output.Write("icns"u8);
    BigEndian(8 + chunks.Sum(c => 8 + c.Data.Length));
    foreach (var (type, data) in chunks)
    {
        output.Write(System.Text.Encoding.ASCII.GetBytes(type));
        BigEndian(8 + data.Length);
        output.Write(data);
    }
    return output.ToArray();
}

// Straight-alpha BGRA, top-down. Mipmapped sampling: a plain bilinear 512→16 skips most source pixels and aliases.
static byte[] Render(SKImage source, int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.DrawImage(source, new SKRect(0, 0, size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    var pixels = new byte[size * size * 4];
    unsafe
    {
        fixed (byte* p = pixels)
        {
            var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            if (!surface.ReadPixels(info, (IntPtr)p, size * 4, 0, 0)) throw new InvalidOperationException("ReadPixels failed");
        }
    }
    return pixels;
}

static byte[] EncodePng(SKImage source, int size)
{
    var pixels = Render(source, size);
    using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul));
    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static byte[] EncodeDib(SKImage source, int size)
{
    var pixels = Render(source, size);
    var maskStride = (size + 31) / 32 * 4;
    var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    // BITMAPINFOHEADER. The height covers the colour bitmap and the AND mask, hence twice the size.
    w.Write(40);
    w.Write(size);
    w.Write(size * 2);
    w.Write((ushort)1);
    w.Write((ushort)32);
    w.Write(0); // BI_RGB
    w.Write(size * size * 4 + maskStride * size);
    w.Write(0);
    w.Write(0);
    w.Write(0);
    w.Write(0);
    for (var y = size - 1; y >= 0; y--) w.Write(pixels, y * size * 4, size * 4); // bottom-up
    // The AND mask is ignored for 32-bit images (alpha wins), but it must be there.
    w.Write(new byte[maskStride * size]);
    w.Flush();
    return ms.ToArray();
}
