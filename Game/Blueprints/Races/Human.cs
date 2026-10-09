using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>Adaptable and unremarkable in any single way -- which is exactly what makes them so widespread.</summary>
/// <remarks>
/// Defaults to a generic NPC shape, same as every other race (a pink 'h' glyph, no sprite, Random
/// movement). The Player blueprint includes PlayerKit after it: PlayerKit's appearance replaces the
/// look, and its step switches movement to PlayerControlled. ActionLock is not overridden -- Human's 30-frame lock is its
/// real default, deliberately looser than Goblin's 54-frame one, used as-is by the player. Ability
/// scores use the same clustered 2d6 roll PlayerKit always has, rather than every other NPC
/// race's flat default-5 -- Human is the one race with genuinely varied starting stats.
/// </remarks>
public static class Human
{
    public static readonly Guid Id = new("43fb5093-962d-4125-bae7-64e81c0b7cdd");
    public const string Name = "Human";
    private const string Description = "Adaptable and unremarkable in any single way -- which is exactly what makes them so widespread.";

    public static readonly AppearanceFacet Appearance = new() { Description = Description, Glyph = "h", GlyphColor = Color.Pink, SpriteName = AppearanceFacet.NoSprite };

    /// <summary>What every Human can do -- held on its race definition, not on each creature (see ActionGrant). No overrides: QuickAttack and PowerAttack roll their catalog DirectDamage range rather than a fixed number.</summary>
    public static readonly ActionGrant[] ActionGrants =
    [
        new(QuickAttackAction.Id),
        new(PowerAttackAction.Id),
        new(DodgeAction.Id),
    ];

    /// <summary>Head/Torso/Internal are Vital; sums to 250, matching the flat SimpleHealthComponent total this replaced so the split doesn't itself rebalance Human's overall toughness. 11 parts (Arm/Leg each split off a Hand/Foot, plus Internal for Poison's own always-hit target) -- not a final balance pass. VerticalPosition: Head 5, Torso/Internal 4, Arm 3, Hand 2, Leg 1, Foot 0.</summary>
    public static readonly BodyPartTemplate[] BodyParts =
    [
        new BodyPartTemplate("Head", BodyPartType.Head, 5, 40, IsVital: true),
        new BodyPartTemplate("Torso", BodyPartType.Torso, 4, 65, IsVital: true),
        new BodyPartTemplate("Internal", BodyPartType.Internal, 4, 15, IsVital: true),
        new BodyPartTemplate("Left Arm", BodyPartType.Arm, 3, 20, IsVital: false),
        new BodyPartTemplate("Right Arm", BodyPartType.Arm, 3, 20, IsVital: false),
        new BodyPartTemplate("Left Hand", BodyPartType.Hand, 2, 5, IsVital: false),
        new BodyPartTemplate("Right Hand", BodyPartType.Hand, 2, 5, IsVital: false),
        new BodyPartTemplate("Left Leg", BodyPartType.Leg, 1, 30, IsVital: false),
        new BodyPartTemplate("Right Leg", BodyPartType.Leg, 1, 30, IsVital: false),
        new BodyPartTemplate("Left Foot", BodyPartType.Foot, 0, 10, IsVital: false),
        new BodyPartTemplate("Right Foot", BodyPartType.Foot, 0, 10, IsVital: false),
    ];

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = Appearance,
        Race = new RaceFacet(BodyParts),
        Actions = ActionGrants
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            AbilityScoreEffects.Grant(componentManager, entityId, abilityScoreType, RollAbilityScoreBaseValue(context.Rolls));
        }
    }

    /// <summary>Two Next(1,6) rolls summed -- range [2,10] per the spec, clustering around the middle rather than uniform across the whole range. Exact shape isn't load-bearing since level-up moves these later.</summary>
    private static ushort RollAbilityScoreBaseValue(MathUtility rolls) => (ushort)(rolls.Next(1, 6) + rolls.Next(1, 6));
}
