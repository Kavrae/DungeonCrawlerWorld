using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>Adaptable and unremarkable in any single way -- which is exactly what makes them so widespread.</summary>
/// <remarks>
/// Takes MathUtility by constructor injection for the same reason Goblin does -- see Goblin's own
/// doc comment. Defaults to a generic NPC shape, same as every other race (a pink 'h' glyph, no
/// Sprite, Random movement) -- PlayerBlueprint, the one entity composing this race in today,
/// overrides Glyph/Sprite/Movement to its own '@'/Player-sprite/PlayerControlled shape immediately
/// after calling Build, the same overrides-after-parts pattern GoblinEngineerBlueprint uses for
/// Goblin's own ActionLock. ActionLock itself is not overridden -- Human's 30-frame lock is its
/// real default, deliberately looser than Goblin's 54-frame one, used as-is by the player. Ability
/// scores use the same clustered 2d6 roll PlayerBlueprint always has, rather than every other NPC
/// race's flat default-5 -- Human is the one race with genuinely varied starting stats.
/// </remarks>
public sealed class Human(MathUtility mathUtility) : IBlueprint
{
    public static readonly Guid RaceId = new("43fb5093-962d-4125-bae7-64e81c0b7cdd");
    private const string RaceName = "Human";
    private const string Description = "Adaptable and unremarkable in any single way -- which is exactly what makes them so widespread.";

    /// <summary>Head/Torso/Internal are Vital; sums to 250, matching the flat SimpleHealthComponent total this replaced so the split doesn't itself rebalance Human's overall toughness. 11 parts (Arm/Leg each split off a Hand/Foot, plus Internal for Poison's own always-hit target) -- not a final balance pass, see PLAN-targeted-body-part-damage.md/PLAN-per-body-part-status-effects.md. VerticalPosition: Head 5, Torso/Internal 4, Arm 3, Hand 2, Leg 1, Foot 0.</summary>
    private static readonly BodyPartTemplate[] BodyParts =
    [
        new BodyPartTemplate("Head", BodyPartType.Head, 5, 40, 40, IsVital: true),
        new BodyPartTemplate("Torso", BodyPartType.Torso, 4, 65, 65, IsVital: true),
        new BodyPartTemplate("Internal", BodyPartType.Internal, 4, 15, 15, IsVital: true),
        new BodyPartTemplate("Left Arm", BodyPartType.Arm, 3, 20, 20, IsVital: false),
        new BodyPartTemplate("Right Arm", BodyPartType.Arm, 3, 20, 20, IsVital: false),
        new BodyPartTemplate("Left Hand", BodyPartType.Hand, 2, 5, 5, IsVital: false),
        new BodyPartTemplate("Right Hand", BodyPartType.Hand, 2, 5, 5, IsVital: false),
        new BodyPartTemplate("Left Leg", BodyPartType.Leg, 1, 30, 30, IsVital: false),
        new BodyPartTemplate("Right Leg", BodyPartType.Leg, 1, 30, 30, IsVital: false),
        new BodyPartTemplate("Left Foot", BodyPartType.Foot, 0, 10, 10, IsVital: false),
        new BodyPartTemplate("Right Foot", BodyPartType.Foot, 0, 10, 10, IsVital: false),
    ];

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        componentManager.Merge(entityId, new GlyphComponent("h", Color.Pink));

        ComplexHealthEffects.GrantBodyParts(componentManager, entityId, mathUtility, BodyParts);

        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 30, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            AbilityScoreEffects.Grant(componentManager, entityId, abilityScoreType, RollAbilityScoreBaseValue());
        }

        // damageAmount: 0 -- no per-instance override, so Punch rolls its catalog DirectDamage's own
        // MinAmount..MaxAmount range (18-22, roughly +-10% of the old flat 20) instead of a fixed number.
        ActionGrantEffects.Grant(componentManager, entityId, PunchAction.Id, manaCost: 0, damageAmount: 0, cooldownFramesRemaining: 0);
    }

    /// <summary>Two Next(1,6) rolls summed -- range [2,10] per the spec, clustering around the middle rather than uniform across the whole range. Exact shape isn't load-bearing since level-up moves these later.</summary>
    private ushort RollAbilityScoreBaseValue() => (ushort)(mathUtility.Next(1, 6) + mathUtility.Next(1, 6));
}
