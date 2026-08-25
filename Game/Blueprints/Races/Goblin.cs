using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Blueprints.NPCs;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>
/// Small, green and smart. Takes MathUtility by constructor injection rather than creating a
/// fresh, unseeded Random per Build call, which would be both wasteful and untestable.
/// </summary>
public sealed class Goblin(MathUtility mathUtility) : IBlueprint
{
    public static readonly Guid RaceId = new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6");
    private const string RaceName = "Goblin";

    private static readonly string[] PersonalNameOptions = ["TestName1", "TestName2"];

    private const string Description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

    private static readonly string[] DisplayNames = DisplayNameCache.BuildDisplayNames(PersonalNameOptions, RaceName);

    /// <summary>Head/Torso are Vital; sums to 200, matching the flat SimpleHealthComponent total this replaced so the split doesn't itself rebalance Goblin's overall toughness. Not a final balance pass -- see PLAN-body-parts.md's Phase 3 proof case.</summary>
    private static readonly BodyPartTemplate[] BodyParts =
    [
        new BodyPartTemplate("Head", BodyPartType.Head, 30, 30, IsVital: true),
        new BodyPartTemplate("Torso", BodyPartType.Torso, 60, 60, IsVital: true),
        new BodyPartTemplate("Left Arm", BodyPartType.Arm, 20, 20, IsVital: false),
        new BodyPartTemplate("Right Arm", BodyPartType.Arm, 20, 20, IsVital: false),
        new BodyPartTemplate("Left Leg", BodyPartType.Leg, 35, 35, IsVital: false),
        new BodyPartTemplate("Right Leg", BodyPartType.Leg, 35, 35, IsVital: false),
    ];

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const ushort PunchDamage = 10;

    /// <summary>Permanent racial toughness -- reduces all damage this goblin takes by 1, regardless of source (melee, ranged, status effects, contact hazards -- see HealthDamage.Apply, the single chokepoint IncomingDamage is consumed at).</summary>
    private const float DamageReductionAmount = -1f;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        componentManager.Merge(entityId, new DisplayTextComponent(DisplayNames[mathUtility.Next(0, DisplayNames.Length)], Description));

        componentManager.Merge(entityId, new GlyphComponent("g", Color.DarkGreen));
        if (SpriteManifest.TryGet("Goblin", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        ComplexHealthEffects.GrantBodyParts(componentManager, entityId, mathUtility, BodyParts);
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 54, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new ActionInstanceComponent(PunchAction.Id, damageAmount: PunchDamage, cooldownFramesRemaining: 0));

        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, mathUtility);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: DamageReductionAmount, durationFrames: null, StatusEffectSource.Admin);
    }
}