using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Blueprints.Classes;

/// <summary>
/// Engineers act 10% more often than their Dexterity alone would allow
/// </summary>
public static class Engineer
{
    public static readonly Guid Id = new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d");
    public const string Name = "Engineer";

    /// <summary>The ActionLockFrames multiplier an Engineer grants -- 10% off its standard lock.</summary>
    private const float ActionLockMultiplier = -0.1f;

    public static readonly BlueprintDefinition Definition = new(Id, Name) { Build = Build, Class = new ClassFacet() };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        if (!componentManager.GetPackedPool<ActionLockComponent>().Has(entityId))
        {
            componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
            componentManager.Merge(entityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        }

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.ActionLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: ActionLockMultiplier, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
    }
}
