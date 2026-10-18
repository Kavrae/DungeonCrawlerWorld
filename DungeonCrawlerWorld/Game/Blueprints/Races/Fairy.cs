using Engine.Utilities;
using Game.Blueprints.NPCs;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>Their magic is stored in their wings.</summary>
public static class Fairy
{
    public static readonly Guid Id = new("c22f6339-0a56-4528-b818-10052a831dc5");
    public const string Name = "Fairy";

    private static readonly string[] PersonalNameOptions = ["Fairy1", "Fairy2"];

    private const string Description = "TODO fairy description. Their magic is stored in their wings.";

    private static readonly string[] DisplayNames = DisplayNameCache.BuildDisplayNames(PersonalNameOptions, Name);

    public static readonly AppearanceFacet Appearance = new() { DisplayNames = DisplayNames, Description = Description, Glyph = "f", GlyphColor = Color.DeepPink, SpriteName = AppearanceFacet.NoSprite };

    private const ushort MaximumHealth = 100;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const ushort QuickAttackDamage = 3;

    /// <summary>Roughly double QuickAttackDamage, matching PowerAttackAction's own catalog ratio -- see TODO.md's Combat Overhaul: Dodge.</summary>
    private const ushort PowerAttackDamage = 6;

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    private const int MagicMissileRange = 8;

    private const ushort MagicMissileDamage = 8;

    private static readonly ushort MagicMissileWindupFrames = GameTiming.FramesForSeconds(1f);

    /// <summary>Enough mana for three Magic Missiles, regenerating, so a Fairy runs dry and waits like a caster.</summary>
    private const float MaximumMana = 15;

    /// <summary>What every creature of this race can do -- held on its race definition, not on each creature (see ActionGrant). Dodge has no override: it rolls its catalog definition unchanged.</summary>
    /// <remarks>
    /// Each override is made from the session's own definition and shared by every creature of this race (ActionGrant.Derived):
    /// an ActionDefinition is never changed in place, only replaced, and building it per creature was most of what a creature
    /// allocated. Magic Missile is TEMPORARY: the first NPC ranged attack, to see targeting modes in play -- wound up for a
    /// second, over a shorter range, for less damage. The catalog spell, and the player's, stay Immediate with range 20.
    /// </remarks>
    public static readonly ActionGrant[] ActionGrants =
    [
        ActionGrant.Derived(BuiltInActionIds.QuickAttack, "Fairy.QuickAttack", static action => ActionOverrideEffects.OverrideFlatDamage(action, QuickAttackDamage)),
        ActionGrant.Derived(BuiltInActionIds.PowerAttack, "Fairy.PowerAttack", static action => ActionOverrideEffects.OverrideFlatDamage(action, PowerAttackDamage)),
        new(BuiltInActionIds.Dodge),
        ActionGrant.Derived(BuiltInActionIds.MagicMissile, "Fairy.MagicMissile", BuildMagicMissileOverride),
    ];

    private static ActionDefinition BuildMagicMissileOverride(ActionDefinition magicMissile)
    {
        var catalogSpell = ActionOverrideEffects.OverrideFlatDamage(magicMissile, MagicMissileDamage);
        var spell = (SpellActivator)catalogSpell.Activator;
        return catalogSpell with
        {
            Activator = spell with
            {
                Targeting = spell.Targeting with { Range = MagicMissileRange },
                Timing = new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: MagicMissileWindupFrames, CooldownFrames: null),
            },
        };
    }

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = Appearance,
        Race = new RaceFacet(),
        Actions = ActionGrants,
        Layer = MapLayer.Flying
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(entityId, new ManaComponent(MaximumMana, MaximumMana));


        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, context.Rolls, context.Items);
        StartingCurrencyGrant.GrantRandomStartingGoldAndCredits(componentManager, entityId, context.Rolls);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);
    }
}
