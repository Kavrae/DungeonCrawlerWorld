using Microsoft.Xna.Framework;

namespace Presentation.UI.Menus;

/// <summary>Something the player can bind to a hotkey slot that opens a window or switches a mode: never armed, no cost or cooldown, no target -- a press runs it.</summary>
/// <param name="Id">The Guid a slot binds it by (Game's BuiltInMenuCommandIds for the built-ins).</param>
/// <param name="MenuOptionLabel">What a slot's context menu calls running it ("Open", "Switch").</param>
/// <param name="Run">What a press does.</param>
/// <param name="CanRun">Whether a press would do anything now; a slot holding it greys while it can't.</param>
public sealed record MenuCommand(
    Guid Id,
    string Name,
    string Description,
    string? SpriteName,
    string Glyph,
    Color GlyphColor,
    string MenuOptionLabel,
    Action Run,
    Func<bool> CanRun);
