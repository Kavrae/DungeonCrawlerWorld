using Engine.ECS.Systems;
using Game.Blueprints.NPCs;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>Small, green and smart.</summary>
public static class Goblin
{
    public static readonly Guid Id = new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6");
    public const string Name = "Goblin";

    private static readonly string[] PersonalNameOptions = ["TestName1", "TestName2"];

    private const string Description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

    private static readonly string[] DisplayNames = DisplayNameCache.BuildDisplayNames(PersonalNameOptions, Name);

    public static readonly AppearanceFacet Appearance = new() { DisplayNames = DisplayNames, Description = Description, Glyph = "g", GlyphColor = Color.DarkGreen, SpriteName = "Goblin" };

    /// <summary>Head/Torso/Internal are Vital; sums to 200, matching the flat SimpleHealthComponent total this replaced so the split doesn't itself rebalance Goblin's overall toughness. 11 parts (Arm/Leg each split off a Hand/Foot, plus Internal for Poison's own always-hit target) -- not a final balance pass. VerticalPosition: Head 5, Torso/Internal 4, Arm 3, Hand 2, Leg 1, Foot 0.</summary>
    public static readonly BodyPartTemplate[] BodyParts =
    [
        new BodyPartTemplate("Head", BodyPartType.Head, 5, 30, IsVital: true),
        new BodyPartTemplate("Torso", BodyPartType.Torso, 4, 50, IsVital: true),
        new BodyPartTemplate("Internal", BodyPartType.Internal, 4, 10, IsVital: true),
        new BodyPartTemplate("Left Arm", BodyPartType.Arm, 3, 15, IsVital: false),
        new BodyPartTemplate("Right Arm", BodyPartType.Arm, 3, 15, IsVital: false),
        new BodyPartTemplate("Left Hand", BodyPartType.Hand, 2, 5, IsVital: false),
        new BodyPartTemplate("Right Hand", BodyPartType.Hand, 2, 5, IsVital: false),
        new BodyPartTemplate("Left Leg", BodyPartType.Leg, 1, 25, IsVital: false),
        new BodyPartTemplate("Right Leg", BodyPartType.Leg, 1, 25, IsVital: false),
        new BodyPartTemplate("Left Foot", BodyPartType.Foot, 0, 10, IsVital: false),
        new BodyPartTemplate("Right Foot", BodyPartType.Foot, 0, 10, IsVital: false),
    ];

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const ushort QuickAttackDamage = 10;

    /// <summary>Roughly double QuickAttackDamage, matching PowerAttackAction's own catalog ratio (18-22 -> 36-44) -- see TODO.md's Combat Overhaul: Dodge.</summary>
    private const ushort PowerAttackDamage = 20;

    /// <summary>Permanent racial toughness -- reduces all damage this goblin takes by 1, regardless of source (melee, ranged, status effects, contact hazards -- see HealthDamage.Apply, the single chokepoint IncomingDamage is consumed at).</summary>
    private const float DamageReductionAmount = -1f;

    /// <summary>One override shared by every creature of this race: an ActionDefinition is never changed in place, only replaced, and building it per creature was most of what a creature allocated.</summary>
    private static readonly ActionDefinition QuickAttackOverride = ActionOverrideEffects.OverrideFlatDamage(QuickAttackAction.Build(), QuickAttackDamage);

    /// <inheritdoc cref="QuickAttackOverride"/>
    private static readonly ActionDefinition PowerAttackOverride = ActionOverrideEffects.OverrideFlatDamage(PowerAttackAction.Build(), PowerAttackDamage);

    /// <summary>What every creature of this race can do -- held on its race definition, not on each creature (see ActionGrant). Dodge has no override: it rolls its catalog definition unchanged.</summary>
    public static readonly ActionGrant[] ActionGrants =
    [
        new(QuickAttackAction.Id, QuickAttackOverride),
        new(PowerAttackAction.Id, PowerAttackOverride),
        new(DodgeAction.Id),
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
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 54, currentLockTotalFrames: 0, unlockedAtFrame: 0));


        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, context.Rolls);
        StartingCurrencyGrant.GrantRandomStartingGoldAndCredits(componentManager, entityId, context.Rolls);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: DamageReductionAmount, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
    }
}
