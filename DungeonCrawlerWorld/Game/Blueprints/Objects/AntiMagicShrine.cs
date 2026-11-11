using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>A stationary shrine whose null zone disables spells -- spell actions and scrolls -- for whoever stands in it, and breaks any spell they are winding up or holding.</summary>
/// <remarks>A proof of the Disabled status (ActivationDisableGrant), the forerunner of anti-magic zones. Its aura (BuiltInAuraIds.AntiMagic) is the same at every tile in reach. As durable as every shrine (ShrineDurability).</remarks>
public static class AntiMagicShrine
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000128");

    public const string Name = "Anti-Magic Shrine";

    private const string Description = "A squat shrine of dull grey stone. Magic thins and fails in the air around it.";

    private const byte AntiMagicAuraSize = 4;

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = ShrineDurability.Build,
        Auras = [new AuraGrant(BuiltInAuraIds.AntiMagic, Power: 1, AntiMagicAuraSize)],
        Appearance = new() { Name = Name, Description = Description, Glyph = "+", GlyphColor = Color.MediumPurple, SpriteName = "Shrine-AntiMagic" }
    };
}
