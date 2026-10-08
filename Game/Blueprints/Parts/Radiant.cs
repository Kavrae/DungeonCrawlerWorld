using Engine.Math;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Parts;

/// <summary>Admin testing trait: makes whatever it is applied to radiate two large glow-only auras, one with each falloff, to watch big moving sources and what they cost.</summary>
/// <remarks>Applied through Admin Mode's "Apply >" only; nothing spawns or includes it. Neither aura has effects, so nothing is exposed to them.</remarks>
public static class Radiant
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000125");

    public const string Name = "Radiant";

    private const ushort AuraPower = 8;

    private const byte AuraSize = 30;

    /// <summary>Full power to its edge: a move rewrites only the cells that changed sides.</summary>
    public static readonly AuraDefinition SteadyAura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000307"), "Radiant Steady", Color.Gold, Falloff: AuraFalloff.None);

    /// <summary>Fades to its edge: a move rewrites both diamonds.</summary>
    public static readonly AuraDefinition FadingAura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000308"), "Radiant Fading", Color.Cyan);

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Auras = [new AuraGrant(SteadyAura, AuraPower, AuraSize), new AuraGrant(FadingAura, AuraPower, AuraSize)],
    };
}
