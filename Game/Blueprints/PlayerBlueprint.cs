using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>
/// The player character: rendered as the ASCII-standard '@', moved by MapWindow's input
/// handling (MovementMode.PlayerControlled -- see MovementSystem.SetNextMapPosition) rather
/// than any algorithmic selection. Always a Crawler -- see CrawlerComponent's own doc comment.
/// </summary>
public sealed class PlayerBlueprint(MathUtility mathUtility, UniqueNumberAllocator crawlerNumberAllocator) : IBlueprint
{
    private const short MaximumHealth = 100;

    private const short MagicMissileDamage = 5;

    /// <summary>Matches the Expansion group's old fixed slot count -- nobody loses hotkey access just because Expansion now grows past 10. See HotkeyExpansionUnlockComponent's own doc comment.</summary>
    private const short DefaultUnlockedExpansionSlots = 5;

    /// <summary>PermanentHybridBuffTest -- exercises a permanent modifier granting both a flat and a percentage bonus at once. See StatModifierComponent's own doc comment for why this never mutates HealthComponent/ActionInstanceComponent directly.</summary>
    private const float PermanentOutgoingDamageBonus = 2f;
    private const float PermanentMaximumHealthMultiplierBonus = 0.5f;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new GlyphComponent("@", Color.White));
        if (SpriteManifest.TryGet("Player", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, MaximumHealth + 1), MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.PlayerControlled, 20, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            AbilityScoreEffects.Grant(componentManager, entityId, abilityScoreType, RollAbilityScoreBaseValue());
        }

        ActionGrantEffects.Grant(componentManager, entityId, HealAction.Id, HealAction.ManaCost, damageAmount: 0, cooldownFramesRemaining: 0);
        // damageAmount: 0 -- no per-instance override, so Punch rolls its catalog DamageEffectEntry's own
        // MinAmount..MaxAmount range (18-22, roughly +-10% of the old flat 20) instead of a fixed number.
        ActionGrantEffects.Grant(componentManager, entityId, PunchAction.Id, manaCost: 0, damageAmount: 0, cooldownFramesRemaining: 0);
        ActionGrantEffects.Grant(componentManager, entityId, MagicMissileAction.Id, MagicMissileAction.ManaCost, damageAmount: MagicMissileDamage, cooldownFramesRemaining: 0);
        ActionGrantEffects.Grant(componentManager, entityId, ToxicStrikeAction.Id, manaCost: 0, damageAmount: 0, cooldownFramesRemaining: 0);

        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.DefaultAttack, PunchAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base1, HealAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base2, MagicMissileAction.Id));
        componentManager.Merge(entityId, new ActionHotkeyBindingComponent(HotkeySlot.Base3, ToxicStrikeAction.Id));
        componentManager.Merge(entityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: DefaultUnlockedExpansionSlots));

        componentManager.Merge(entityId, new CrawlerComponent(crawlerNumberAllocator.Allocate()));

        componentManager.Merge(entityId, new DisplayTextComponent("Player1", "This is you. What else did you expect?"));

        InventoryActions.AddItem(componentManager, entityId, HealthPotion.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, ManaPotion.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, HotkeyExpansionPotion.Id, quantity: 3);
        InventoryActions.AddItem(componentManager, entityId, DamagePotion.Id, quantity: 5);
        InventoryActions.AddItem(componentManager, entityId, ToxicPotion.Id, quantity: 5);

        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, HealthPotion.Id));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot2, ManaPotion.Id));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot3, HotkeyExpansionPotion.Id));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot4, DamagePotion.Id));
        componentManager.Merge(entityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot5, ToxicPotion.Id));

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentOutgoingDamageBonus, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentMaximumHealthMultiplierBonus, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
    }

    /// <summary>Two Next(1,6) rolls summed -- range [2,10] per the spec, clustering around the middle rather than uniform across the whole range. Exact shape isn't load-bearing since level-up moves these later.</summary>
    private short RollAbilityScoreBaseValue() => (short)(mathUtility.Next(1, 6) + mathUtility.Next(1, 6));
}
