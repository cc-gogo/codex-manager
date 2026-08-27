using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: IconCompiler <source-png> <destination-ico>");
}

var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var source = LoadBitmap(args[0]);
var frames = sizes.Select(size => CreateFrame(source, size)).ToArray();

using var output = File.Create(args[1]);
using var writer = new BinaryWriter(output);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)frames.Length);

var imageOffset = 6 + (16 * frames.Length);
foreach (var frame in frames)
{
    writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
    writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(frame.Data.Length);
    writer.Write(imageOffset);
    imageOffset += frame.Data.Length;
}

foreach (var frame in frames)
{
    writer.Write(frame.Data);
}

static BitmapImage LoadBitmap(string path)
{
    var bitmap = new BitmapImage();
    bitmap.BeginInit();
    bitmap.CacheOption = BitmapCacheOption.OnLoad;
    bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
    bitmap.EndInit();
    bitmap.Freeze();
    return bitmap;
}

static IconFrame CreateFrame(BitmapSource source, int size)
{
    var visual = new DrawingVisual();
    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
    using (var context = visual.RenderOpen())
    {
        context.DrawImage(source, new Rect(0, 0, size, size));
    }

    var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    target.Render(visual);
    var sourcePixels = new byte[size * size * 4];
    target.CopyPixels(sourcePixels, size * 4, 0);

    // WPF renders to premultiplied alpha. ICO uses ordinary BGRA pixels.
    for (var pixel = 0; pixel < sourcePixels.Length; pixel += 4)
    {
        var alpha = sourcePixels[pixel + 3];
        if (alpha is 0 or 255)
        {
            continue;
        }

        sourcePixels[pixel] = (byte)Math.Min(255, (sourcePixels[pixel] * 255 + (alpha / 2)) / alpha);
        sourcePixels[pixel + 1] = (byte)Math.Min(255, (sourcePixels[pixel + 1] * 255 + (alpha / 2)) / alpha);
        sourcePixels[pixel + 2] = (byte)Math.Min(255, (sourcePixels[pixel + 2] * 255 + (alpha / 2)) / alpha);
    }

    var dibPixels = new byte[sourcePixels.Length];
    for (var row = 0; row < size; row++)
    {
        Buffer.BlockCopy(sourcePixels, row * size * 4, dibPixels, (size - row - 1) * size * 4, size * 4);
    }

    using var stream = new MemoryStream(40 + dibPixels.Length);
    using var writer = new BinaryWriter(stream);
    writer.Write(40);
    writer.Write(size);
    writer.Write(size * 2);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(0);
    writer.Write(dibPixels.Length);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(dibPixels);
    return new IconFrame(size, stream.ToArray());
}

internal sealed record IconFrame(int Size, byte[] Data);
