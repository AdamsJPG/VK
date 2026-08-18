using System.IO;
using System.Windows.Media.Imaging;

namespace DataCompare.App;

/// <summary>
/// One-off asset build step: renders <see cref="VkIconFactory"/> at standard icon sizes and
/// packs them into a multi-resolution .ico for the exe's embedded icon (Window.Icon at runtime is
/// set directly from the same factory — this covers the Explorer/file-icon case, which needs a
/// real .ico baked into the binary). Invoked once via `dotnet run -- --generate-icon`.
/// </summary>
internal static class IconGenerator
{
    private static readonly int[] Sizes = [16, 32, 48, 256];

    public static void GenerateIcoFile(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var frames = Sizes.Select(size =>
        {
            var bitmap = VkIconFactory.Render(size);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return (Size: size, Bytes: stream.ToArray());
        }).ToList();

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fileStream);

        writer.Write((short)0); // reserved
        writer.Write((short)1); // type = icon
        writer.Write((short)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var (size, bytes) in frames)
        {
            var dimensionByte = (byte)(size >= 256 ? 0 : size); // 0 means 256 in the ICO format
            writer.Write(dimensionByte); // width
            writer.Write(dimensionByte); // height
            writer.Write((byte)0); // color palette
            writer.Write((byte)0); // reserved
            writer.Write((short)1); // color planes
            writer.Write((short)32); // bits per pixel
            writer.Write(bytes.Length);
            writer.Write(offset);
            offset += bytes.Length;
        }

        foreach (var (_, bytes) in frames)
        {
            writer.Write(bytes);
        }
    }
}
