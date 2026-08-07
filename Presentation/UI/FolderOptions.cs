using Microsoft.Xna.Framework;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>Folder-specific configuration -- same "independent option group" pattern as TextOptions for TextWindow (see WindowOptions).</summary>
public sealed class FolderOptions
{
    /// <summary>Looked up via Game.Blueprints.SpriteManifest. Falls back to FallbackGlyph if the name isn't found there.</summary>
    public string? SpriteName { get; set; }

    public string? FallbackGlyph { get; set; }

    /// <summary>Defaults to Folder.DefaultIconSize if unset.</summary>
    public Vector2? IconSize { get; set; }

    /// <summary>Defaults to WindowPalette.HeaderColor if unset -- matches Window's own title-bar default, since Folder's header reads as the same kind of chrome.</summary>
    public Color? BackgroundColor { get; set; }
}
