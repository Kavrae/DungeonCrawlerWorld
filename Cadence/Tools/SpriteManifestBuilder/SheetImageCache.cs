namespace SpriteManifestBuilder;

/// <summary>Spritesheets loaded once by their path under Content/Spritesheets, shared by the sheet view, the previews and the slot thumbnails.</summary>
/// <remarks>
/// Each sheet is read into memory and the file closed at once, so an art tool can overwrite a sheet while the builder has it open.
/// GDI+ reads an image from its stream for as long as the image lives, so the cache keeps each in-memory stream beside its image.
/// </remarks>
public sealed class SheetImageCache : IDisposable
{
    private readonly Dictionary<string, (Image Image, MemoryStream Bytes)?> _imagesBySheetPath = [];

    /// <summary>The sheet, or null when it can't be read; a failure is remembered rather than retried every paint.</summary>
    public Image? Get(string sheetPath)
    {
        if (_imagesBySheetPath.TryGetValue(sheetPath, out var cached))
        {
            return cached?.Image;
        }

        (Image Image, MemoryStream Bytes)? loaded;
        try
        {
            var bytes = new MemoryStream(File.ReadAllBytes(Path.Combine(RepoPaths.SpritesheetsRoot, sheetPath)));
            loaded = (Image.FromStream(bytes), bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OutOfMemoryException or ArgumentException)
        {
            loaded = null;
        }

        _imagesBySheetPath[sheetPath] = loaded;
        return loaded?.Image;
    }

    public void Dispose()
    {
        foreach (var loaded in _imagesBySheetPath.Values)
        {
            loaded?.Image.Dispose();
            loaded?.Bytes.Dispose();
        }

        _imagesBySheetPath.Clear();
    }
}
