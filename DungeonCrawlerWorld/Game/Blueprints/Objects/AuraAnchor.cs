using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>An invisible marker an aura is placed on when it is cast at a tile rather than an entity.</summary>
/// <remarks>
/// Non-blocking, with a blank glyph and no sprite, so only its aura's glow shows. Spawned by AuraAnchors,
/// which names it after its aura (DisplayTextComponent) and gives it the aura's source; it is destroyed
/// when that source ends.
/// </remarks>
public static class AuraAnchor
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000126");

    public const string Name = "Aura";

    private const string Description = "An aura cast on this spot.";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        NonBlocking = NonBlockingKind.None,
        Appearance = new() { Name = Name, Description = Description, Glyph = string.Empty, GlyphColor = Color.Transparent, SpriteName = AppearanceFacet.NoSprite },
    };
}
