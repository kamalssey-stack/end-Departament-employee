namespace DepartmentEmployeeManagement.Api.Services;

public static class ImageUploadValidator
{
    public const long MaximumFileSize = 20 * 1024 * 1024;

    public static bool TryGetStoredExtension(IFormFile file, out string extension)
    {
        extension = string.Empty;
        if (file.Length is <= 0 or > MaximumFileSize) return false;

        var sourceExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        Span<byte> header = stackalloc byte[12];
        using var stream = file.OpenReadStream();
        var read = 0;
        while (read < header.Length)
        {
            var count = stream.Read(header[read..]);
            if (count == 0) break;
            read += count;
        }

        var supported = sourceExtension switch
        {
            ".jpg" or ".jpeg" or ".jfif" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".gif" => read >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)),
            ".bmp" => read >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M',
            ".webp" => read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
            ".avif" => read >= 12 && header[4..8].SequenceEqual("ftyp"u8) && (header[8..12].SequenceEqual("avif"u8) || header[8..12].SequenceEqual("avis"u8)),
            _ => false
        };

        if (!supported) return false;
        extension = sourceExtension == ".jfif" ? ".jpg" : sourceExtension;
        return true;
    }
}
