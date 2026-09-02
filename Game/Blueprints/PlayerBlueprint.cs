using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>
/// The player character: given body parts, ActionLock, and ability scores via the Human race it
/// composes in (Human's own default shape, unmodified -- see Human's own doc comment), with its
/// Glyph/Sprite/Movement overridden immediately after to the player's own '@'/Player-sprite/
/// PlayerControlled shape, the same overrides-after-parts pattern GoblinEngineerBlueprint uses.
/// Always a Crawler -- see CrawlerComponent's own doc comment.
/// </summary>
public sealed class PlayerBlueprint(MathUtility mathUtility, UniqueNumberAllocator crawlerNumberAllocator) : IBlueprint
{
    private const ushort MagicMissileDamage = 5;

    private const ushort WandOfFireballStartingQuantity = 10;

    /// <summary>Charge count for the TEMPORARY Adjacent-targeting test wand below -- arbitrary, just needs to be a few shots' worth.</summary>
    private const ushort TestAdjacentWandCharges = 5;

    /// <summary>Matches the Expansion group's old fixed slot count -- nobody loses hotkey access just because Expansion now grows past 10. See HotkeyExpansionUnlockComponent's own doc comment.</summary>
    private const byte DefaultUnlockedExpansionSlots = 5;

    /// <summary>PermanentHybridBuffTest -- exercises a permanent modifier granting both a flat and a percentage bonus at once. See StatModifierComponent's own doc comment for why this never mutates SimpleHealthComponent/ActionInstanceComponent directly.</summary>
    private const float PermanentOutgoingDamageBonus = 2f;
    private const float PermanentMaximumHealthMultiplierBonus = 0.5f;

    private readonly Human _human = new(mathUtility);

    public void Build(ComponentManager componentManager, int entityId)
    {
        _human.Build(componentManager, entityId);

        // TryUpdate, not Merge -- GlyphComponent's merge policy only Lerps GlyphColor and never
        // overwrites Glyph itself (see CoreModule's own registration), and MovementComponent's
        // merge policy takes the numerically higher MovementMode rather than the latest one (see
        // MovementModule's own registration) -- neither is a "last write wins" replace, so a
        // second Merge call can't actually override Human's own Random/pink-'h' defaults the way
        // GoblinEngineerBlueprint's own override step already has to sidestep the same issue for
        // Goblin's ActionLockComponent.
        componentManager.TryUpdate(entityId, static (ref GlyphComponent glyph) =>
        {
            glyph.Glyph = "@";
            glyph.GlyphColor = Color.White;
        });
        if (SpriteManifest.TryGet("Player", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        componentManager.TryUpdate(entityId, static (ref MovementComponent movement) => movement.MovementMode = MovementMode.PlayerControlled);

        WandGrantEffects.Grant(componentManager, componentManager.GetMultiPool<AbilityScoreComponent>(), entityId, WandOfFireball.Build(), quantity: WandOfFireballStartingQuantity);

        // TEMPORARY -- exercises divergence in a field other than charges (see the per-slot item
        // divergence work): a single Wand of Fireball with Adjacent targeting instead of the
        // normal Burst, built directly via AddDivergentItem rather than WandGrantEffects.Grant
        // since this is a synthetic, already-divergent single unit, not a fresh batch. Remove once
        // a real, player-driven way to diverge a non-charge field exists.
        var baseWand = WandOfFireball.Build();
        var adjacentTargetingWand = baseWand with
        {
            Activator = ((WandActivator)baseWand.Activator!) with { Targeting = new TargetingSpec(TargetShape.Adjacent, Range: 0), Charges = TestAdjacentWandCharges, MaxCharges = TestAdjacentWandCharges },
        };
        InventoryActions.AddDivergentItem(componentManager, entityId, adjacentTargetingWand);

        var magicMissileOverride = ActionOverrideEffects.OverrideFlatDamage(MagicMissileAction.Build(), MagicMissileDamage);
        ActionGrantEffects.Grant(componentManager, entityId, HealAction.Id, HealAction.ManaCost, overrideDefinition: null, cooldownFramesRemaining: 0);
        ActionGrantEffects.Grant(componentManager, entityId, MagicMissileAction.Id, MagicMissileAction.ManaCost, overrideDefinition: magicMissileOverride, cooldownFramesRemaining: 0);
        ActionGrantEffects.Grant(componentManager, entityId, ToxicStrikeAction.Id, manaCost: 0, overrideDefinition: null, cooldownFramesRemaining: 0);

        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.DefaultAttack, PunchAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, HealAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base2, MagicMissileAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base3, ToxicStrikeAction.Id));
        componentManager.Merge(entityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: DefaultUnlockedExpansionSlots));

        componentManager.Merge(entityId, new CrawlerComponent(crawlerNumberAllocator.Allocate()));

        componentManager.Merge(entityId, new DisplayTextComponent("Player1", "This is you. What else did you expect?"));

        StartingCurrencyGrant.GrantFixedStartingGold(componentManager, entityId);

        // ItemHotkeyBindingComponent binds by StackInstanceId, not ItemDefinitionId (see its own
        // doc comment) -- AddItem's return value is the exact stack each of these starting grants
        // landed in, which is what gets bound below.
        var healthPotionStackId = InventoryActions.AddItem(componentManager, entityId, HealthPotion.Id, quantity: 5);
        var manaPotionStackId = InventoryActions.AddItem(componentManager, entityId, ManaPotion.Id, quantity: 5);
        var hotkeyExpansionPotionStackId = InventoryActions.AddItem(componentManager, entityId, HotkeyExpansionPotion.Id, quantity: 3);
        var damagePotionStackId = InventoryActions.AddItem(componentManager, entityId, DamagePotion.Id, quantity: 5);
        var toxicPotionStackId = InventoryActions.AddItem(componentManager, entityId, ToxicPotion.Id, quantity: 5);
        var toxicIdolStackId = InventoryActions.AddItem(componentManager, entityId, ToxicIdol.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, ScrollOfHealing.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, ScrollOfTorch.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, ImmunityTestPotion.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, ResistanceTestPotion.Id, quantity: 5);

        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, healthPotionStackId));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot2, manaPotionStackId));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot3, hotkeyExpansionPotionStackId));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot4, damagePotionStackId));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot5, toxicPotionStackId));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot6, toxicIdolStackId));

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentOutgoingDamageBonus, durationFrames: null, StatusEffectSource.Admin);
        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentMaximumHealthMultiplierBonus, durationFrames: null, StatusEffectSource.Admin);
    }
}
