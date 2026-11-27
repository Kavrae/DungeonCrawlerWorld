using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Auras;
using Game.Views;
using Game.World;

namespace Tests.Fixtures;

/// <summary>An AuraView over a field alone: no pulse source is ever placed, so only the field is read.</summary>
internal static class TestAuraViews
{
    public static AuraView Over(AuraField field, AuraCatalog auras) => new(field, NoPulses(auras), auras);

    /// <summary>A pulse-source set nothing pulses in: its reach stays 0, so it never reads a pool or the map.</summary>
    public static AuraPulseSources NoPulses(AuraCatalog auras) =>
        new(new ComponentManager(1, 1), new EntityKeys(), new BlueprintRegistry(), TestWorlds.Create(new Map(new Vector3Int(1, 1, 1))), auras);
}
