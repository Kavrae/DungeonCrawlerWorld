using Engine.ECS.Components;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Core.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Terrain;

/// <summary>
/// A patch of hot lava terrain: deals contact damage immediately upon stepping onto it and
/// again at a set interval while an entity remains (DamageOnContactComponent), and radiates a
/// Burning aura directly on top, halving at each tile away using Manhattan distance
/// Also glows orange onto nearby tiles at that same falloff.
/// </summary>
public sealed class Lava : IBlueprint
{
    private const int AuraAndGlowStrength = 8;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new BackgroundComponent(Color.OrangeRed));
        componentManager.Merge(entityId, new DisplayTextComponent("Lava", "Hot lava. I do not recommend stepping on it."));
        componentManager.Merge(entityId, new GlyphComponent("~", Color.Yellow));
        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: GameTiming.FramesPerSecond));
        componentManager.Merge(entityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, AuraAndGlowStrength, Color.DarkOrange));
    }
}