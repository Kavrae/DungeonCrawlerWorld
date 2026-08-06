using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>
/// Small, green and smart. Takes MathUtility by constructor injection rather than creating a
/// fresh, unseeded Random per Build call, which would be both wasteful and untestable.
/// </summary>
public sealed class Goblin(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid RaceId = new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6");
    private const string RaceName = "Goblin";

    private static readonly string[] PersonalNameOptions = ["TestName1", "TestName2"];

    private const string Description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

    private const short MaximumHealth = 200;
    private const short HealthRegen = 2;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const short PunchDamage = 10;

    /// <summary>Permanent racial toughness -- reduces all damage this goblin takes by 1, regardless of source (melee, ranged, status effects, contact hazards -- see HealthDamage.Apply, the single chokepoint IncomingDamage is consumed at).</summary>
    private const float DamageReductionAmount = -1f;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        var personalName = PersonalNameOptions[mathUtility.Next(0, PersonalNameOptions.Length)];
        componentManager.Merge(entityId, new DisplayTextComponent($"{personalName} : {RaceName}", Description));

        componentManager.Merge(entityId, new GlyphComponent("g", Color.DarkGreen));
        if (SpriteManifest.TryGet("Goblin", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, MaximumHealth + 1), HealthRegen, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, 54, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new AbilityInstanceComponent(CoreAbilitiesModule.PunchId, damageAmount: PunchDamage, cooldownFramesRemaining: 0));

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: DamageReductionAmount, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
    }
}