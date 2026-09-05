namespace Presentation.UI;

/// <summary>Button-specific configuration -- same "independent option group" pattern as TextOptions/FolderOptions. Optional: a plain text-only Button (a title-bar "X", a context-menu row) never sets this.</summary>
public sealed class ButtonOptions
{
    /// <summary>Looked up via Game.Blueprints.SpriteManifest, drawn centered in the content area in place of Text.Text -- falls back to drawing Text.Text as a glyph (the same sprite-or-glyph degrade Folder's own icon uses) if the name isn't found there.</summary>
    public string? SpriteName { get; set; }
}
