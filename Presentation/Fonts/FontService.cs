using FontStashSharp;

namespace Presentation.Fonts;

/// <summary>Loads and hands out fonts by size, wrapping FontStashSharp's dynamic glyph rasterization.</summary>
public sealed class FontService
{
    private readonly FontSystem _fontSystem;

    /// <param name="fontsDirectory">
    /// Directory containing font files, relative to the exe's output directory (where the
    /// Content project's font files land as "Fonts"). Resolved against
    /// <see cref="AppContext.BaseDirectory"/> rather than the process's current working
    /// directory, since "dotnet run" sets the working directory to the project's source
    /// folder, not its build output.
    /// </param>
    public FontService(string fontsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontsDirectory);

        _fontSystem = new FontSystem();
        var resolvedFontsDirectory = Path.Combine(AppContext.BaseDirectory, fontsDirectory);
        _fontSystem.AddFont(File.ReadAllBytes(Path.Combine(resolvedFontsDirectory, "DroidSans.ttf")));
    }

    public SpriteFontBase GetFont(int fontSize) => _fontSystem.GetFont(fontSize);
}