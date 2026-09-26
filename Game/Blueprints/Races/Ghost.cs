using Game.Blueprints.NPCs;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>
/// A test fixture race for exercising melee status effects, deliberately with no
/// SimpleHealthComponent.
/// </summary>
public static class Ghost
{
    public static readonly Guid Id = new("7e6d6a3a-6b8f-4f0a-9f2a-7c9b1e6f2a3d");
    public const string Name = "Ghost";

    private static readonly string[] PersonalNameOptions = ["Ghost1", "Ghost2"];

    private const string Description = "A wandering spirit with no physical form. Used to test melee status effects against a target with no Health to damage.";

    private static readonly string[] DisplayNames = DisplayNameCache.BuildDisplayNames(PersonalNameOptions, Name);

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const ushort QuickAttackDamage = 5;

    /// <summary>Roughly double QuickAttackDamage, matching PowerAttackAction's own catalog ratio -- see TODO.md's Combat Overhaul: Dodge.</summary>
    private const ushort PowerAttackDamage = 10;

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>ᗣ (U+15A3, Canadian Aboriginal Syllabics). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService)</summary>
    private const string Glyph = "G";

    public static readonly AppearanceFacet Appearance = new() { DisplayNames = DisplayNames, Description = Description, Glyph = Glyph, GlyphColor = Color.Blue, SpriteName = AppearanceFacet.NoSprite };

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
        Race = new RaceFacet(),
        NonBlocking = NonBlockingKind.Phasing,
        Actions = ActionGrants,
        Layer = MapLayer.UnderGround
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 48, currentLockTotalFrames: 0, unlockedAtFrame: 0));

        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, context.Rolls);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);
    }
}
