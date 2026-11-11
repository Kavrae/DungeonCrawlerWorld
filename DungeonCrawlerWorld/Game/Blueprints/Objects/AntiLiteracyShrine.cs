using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>A stationary shrine whose null zone keeps whoever stands in it from reading scrolls and tomes.</summary>
/// <remarks>A proof of the Disabled status (ActivationDisableGrant), the forerunner of null zones. Its aura (BuiltInAuraIds.AntiLiteracy) is the same at every tile in reach. As durable as every shrine (ShrineDurability).</remarks>
public static class AntiLiteracyShrine
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000129");

    public const string Name = "Anti-Literacy Shrine";

    private const string Description = "A shrine carved all over with letters that never stay still. Near it, written words refuse to be read.";

    private const byte AntiLiteracyAuraSize = 3;

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = ShrineDurability.Build,
        Auras = [new AuraGrant(BuiltInAuraIds.AntiLiteracy, Power: 1, AntiLiteracyAuraSize)],
        Appearance = new() { Name = Name, Description = Description, Glyph = "+", GlyphColor = Color.Tan, SpriteName = "Shrine-AntiLiteracy" }
    };
}
