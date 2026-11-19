using System.Buffers.Binary;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// Reads the pixel size an image file declares, from its header only, without decoding any pixel data.
/// A small file can declare a huge bitmap, so the size is checked before the image is decoded. Only the
/// formats a poster may have are understood (PNG, JPEG, GIF, BMP); anything else is refused.
/// </summary>
internal static class ImageHeader
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The largest width and height <paramref name="data"/> declares: the image size of PNG, JPEG and BMP,
    /// and for GIF the logical screen and every frame. <see langword="false"/> when the header is not one
    /// of the understood formats or is cut short.
    /// </summary>
    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.StartsWith(PngSignature))
        {
            return TryReadPng(data, out width, out height);
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return TryReadJpeg(data, out width, out height);
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return TryReadGif(data, out width, out height);
        }

        if (data.StartsWith("BM"u8))
        {
            return TryReadBmp(data, out width, out height);
        }

        return false;
    }

    // The first chunk is IHDR: length, "IHDR", width and height as big endian 32-bit values.
    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var w = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (w is 0 or > int.MaxValue || h is 0 or > int.MaxValue)
        {
            return false;
        }

        width = (int)w;
        height = (int)h;
        return true;
    }

    // Walks the markers up to the first start of frame (SOF0-SOF15 except DHT, JPG and DAC), which holds
    // the height and the width as big endian 16-bit values.
    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
            {
                return false;
            }

            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++; // Fill byte.
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2; // Markers without a length.
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return false; // End of image or start of scan before any frame header.
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 2, 2));
            if (length < 2)
            {
                return false;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (position + 9 > data.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 7, 2));

                // A height of 0 is given later by a DNL marker; such files are refused.
                return width > 0 && height > 0;
            }

            position += 2 + length;
        }

        return false;
    }

    // The logical screen size, then every image descriptor: a frame may declare more than the screen.
    private static bool TryReadGif(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 13)
        {
            return false;
        }

        var maxWidth = (int)BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
        var maxHeight = (int)BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        var flags = data[10];
        var position = 13 + ((flags & 0x80) != 0 ? 3 << ((flags & 0x07) + 1) : 0);
        var frames = 0;
        while (position < data.Length)
        {
            switch (data[position])
            {
                case 0x3B: // Trailer.
                    position = data.Length;
                    break;
                case 0x21: // Extension: label, then data sub-blocks.
                    if (!TrySkipSubBlocks(data, position + 2, out position))
                    {
                        return false;
                    }

                    break;
                case 0x2C: // Image descriptor.
                    if (position + 10 > data.Length)
                    {
                        return false;
                    }

                    maxWidth = Math.Max(maxWidth, BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(position + 5, 2)));
                    maxHeight = Math.Max(maxHeight, BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(position + 7, 2)));
                    var local = data[position + 9];
                    position += 10 + ((local & 0x80) != 0 ? 3 << ((local & 0x07) + 1) : 0);
                    frames++;

                    // The LZW code size byte, then the image data sub-blocks.
                    if (!TrySkipSubBlocks(data, position + 1, out position))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        if (frames == 0 || maxWidth == 0 || maxHeight == 0)
        {
            return false;
        }

        width = maxWidth;
        height = maxHeight;
        return true;
    }

    private static bool TrySkipSubBlocks(ReadOnlySpan<byte> data, int position, out int next)
    {
        while (position < data.Length)
        {
            var size = data[position];
            position += 1 + size;
            if (size == 0)
            {
                next = position;
                return true;
            }
        }

        next = data.Length;
        return false;
    }

    // BITMAPINFOHEADER and later: width and height as little endian 32-bit values; a negative height
    // means a top-down bitmap. The old BITMAPCOREHEADER has 16-bit values.
    private static bool TryReadBmp(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 26)
        {
            return false;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(14, 4));
        long w;
        long h;
        if (headerSize == 12)
        {
            w = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2));
            h = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(20, 2));
        }
        else
        {
            w = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
            h = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4)));
        }

        if (w <= 0 || h <= 0 || w > int.MaxValue || h > int.MaxValue)
        {
            return false;
        }

        width = (int)w;
        height = (int)h;
        return true;
    }
}
