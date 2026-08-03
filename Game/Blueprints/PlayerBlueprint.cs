using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Health.Components;
using Game.Modules.Melee;
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
    private const short HealthRegen = 10;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const short DefaultAttackDamage = 20;

    /// <summary>TEMPORARY -- see PlayerTestAbilitiesModule's own doc comment.</summary>
    private const short RangedTestAbilityDamage = 10;

    /// <summary>TEMPORARY -- see QuickCastTestModule's own doc comment.</summary>
    private const short RangedTestDebuffDamage = 20;

    /// <summary>PermanentHybridBuffTest -- exercises a permanent modifier granting both a flat and a percentage bonus at once. See StatModifierComponent's own doc comment for why this never mutates HealthComponent/AbilityInstanceComponent directly.</summary>
    private const float PermanentOutgoingDamageBonus = 2f;
    private const float PermanentMaximumHealthMultiplierBonus = 0.5f;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new GlyphComponent("@", Color.White));
        if (SpriteManifest.TryGet("Player", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, MaximumHealth + 1), HealthRegen, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.PlayerControlled, 20, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new AbilityInstanceComponent(MeleeModule.DefaultAttackId, damageAmount: DefaultAttackDamage, cooldownFramesRemaining: 0));
        componentManager.Merge(entityId, new AbilityInstanceComponent(PlayerTestAbilitiesModule.RangedTestAbilityId, damageAmount: RangedTestAbilityDamage, cooldownFramesRemaining: 0));
        componentManager.Merge(entityId, new AbilityInstanceComponent(QuickCastTestModule.QuickCastAbilityId, damageAmount: 0, cooldownFramesRemaining: 0));
        componentManager.Merge(entityId, new AbilityInstanceComponent(QuickCastTestModule.RangedTestDebuffAbilityId, damageAmount: RangedTestDebuffDamage, cooldownFramesRemaining: 0));

        componentManager.Merge(entityId, new HotkeyBindingComponent(HotkeySlot.Slot4, MeleeModule.DefaultAttackId));
        componentManager.Merge(entityId, new HotkeyBindingComponent(HotkeySlot.Slot5, PlayerTestAbilitiesModule.RangedTestAbilityId));
        componentManager.Merge(entityId, new HotkeyBindingComponent(HotkeySlot.Slot6, QuickCastTestModule.QuickCastAbilityId));
        componentManager.Merge(entityId, new HotkeyBindingComponent(HotkeySlot.Slot7, QuickCastTestModule.RangedTestDebuffAbilityId));

        componentManager.Merge(entityId, new CrawlerComponent(crawlerNumberAllocator.Allocate()));

        componentManager.Merge(entityId, new DisplayTextComponent("Player1", "This is you. What else did you expect?"));

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentOutgoingDamageBonus, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: PermanentMaximumHealthMultiplierBonus, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);
    }
}
