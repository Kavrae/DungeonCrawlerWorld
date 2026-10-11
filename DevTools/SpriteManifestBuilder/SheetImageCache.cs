namespace SpriteManifestBuilder;

/// <summary>Spritesheets loaded once by their path under Content/Spritesheets, shared by the sheet view, the previews and the slot thumbnails.</summary>
public sealed class SheetImageCache : IDisposable
{
    private readonly Dictionary<string, Image?> _imagesBySheetPath = [];

    /// <summary>The sheet, or null when it can't be read; a failure is remembered rather than retried every paint.</summary>
    public Image? Get(string sheetPath)
    {
        if (_imagesBySheetPath.TryGetValue(sheetPath, out var cached))
        {
            return cached;
        }

        Image? image;
        try
        {
            image = Image.FromFile(Path.Combine(RepoPaths.SpritesheetsRoot, sheetPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OutOfMemoryException or ArgumentException)
        {
            image = null;
        }

        _imagesBySheetPath[sheetPath] = image;
        return image;
    }

    public void Dispose()
    {
        foreach (var image in _imagesBySheetPath.Values)
        {
            image?.Dispose();
        }

        _imagesBySheetPath.Clear();
    }
}
