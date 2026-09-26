using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>
/// Everything the player has that its race and class don't give it: its own name, '@'/Player-sprite
/// look and PlayerControlled movement, its starting actions, hotkey bindings, inventory and permanent
/// modifiers.
/// </summary>
/// <remarks>
/// The last part the Player blueprint includes (Human, then Tank, then this), so it runs after both and can
/// override what they merged -- the overrides-after-parts pattern any part layered on top of a race
/// and a class uses. Tank runs after Human for the same reason: it sees the body parts Human granted
/// and takes its Complex-health path rather than its own standalone Simple baseline (see Tank's own
/// doc comment). The player's CrawlerComponent is its spawn request's (Crawler), not this part's -- see
/// FloorBuilder.CreatePlayer; a crawler's number belongs to the run, not to what it is made of.
/// </remarks>
public static class PlayerKit
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000101");
    public const string Name = "Player";

    private const ushort MagicMissileDamage = 5;

    private const ushort WandOfFireballStartingQuantity = 10;

    /// <summary>Charge count for the TEMPORARY Adjacent-targeting test wand below -- arbitrary, just needs to be a few shots' worth.</summary>
    private const ushort TestAdjacentWandCharges = 5;

    /// <summary>Matches the Expansion group's old fixed slot count -- nobody loses hotkey access just because Expansion now grows past 10. See HotkeyExpansionUnlockComponent's own doc comment.</summary>
    private const byte DefaultUnlockedExpansionSlots = 5;

    /// <summary>PermanentHybridBuffTest -- exercises a permanent modifier granting both a flat and a percentage bonus at once. See StatModifierComponent's own doc comment for why this never mutates SimpleHealthComponent/ActionInstanceComponent directly.</summary>
    private const float PermanentOutgoingDamageBonus = 2f;
    private const float PermanentMaximumHealthMultiplierBonus = 0.5f;

    private const string PlayerName = "Player1";
    private const string PlayerDescription = "This is you. What else did you expect?";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = new() { Name = PlayerName, Description = PlayerDescription, Glyph = "@", GlyphColor = Color.White, SpriteName = "Player" },
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.TryUpdate(entityId, static (ref MovementComponent movement) => movement.MovementMode = MovementMode.PlayerControlled);

        WandGrantEffects.Grant(componentManager, componentManager.GetPackedPool<AbilityScoresComponent>(), entityId, WandOfFireball.Build(), quantity: WandOfFireballStartingQuantity);

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
        ActionGrantEffects.Grant(componentManager, entityId, HealAction.Id, HealAction.ManaCost, overrideDefinition: null);
        ActionGrantEffects.Grant(componentManager, entityId, MagicMissileAction.Id, MagicMissileAction.ManaCost, overrideDefinition: magicMissileOverride);
        ActionGrantEffects.Grant(componentManager, entityId, ToxicStrikeAction.Id, manaCost: 0, overrideDefinition: null);

        // F/Q/R are the defaults for Dodge/PowerAttack/QuickAttack (Combat Overhaul: Dodge,
        // TODO.md) -- hotkey slots are never dedicated, only defaulted when available, so this
        // simply replaces whatever a slot previously defaulted to. Base1 (Q) previously defaulted
        // to HealAction -- still granted to the player above, just no longer hotkey-bound by
        // default until a rebind UI exists.
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.DefaultAttack, DodgeAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, PowerAttackAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base2, MagicMissileAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base3, QuickAttackAction.Id));
        componentManager.Merge(entityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: DefaultUnlockedExpansionSlots));

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
            canModify: true, magnitude: PermanentOutgoingDamageBonus, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
        MaximumHealthShift.ApplyModifier(componentManager, context.Definitions, entityId, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentMaximumHealthMultiplierBonus, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
    }
}
