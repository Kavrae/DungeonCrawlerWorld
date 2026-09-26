using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;

namespace Game.Blueprints.Classes;

/// <summary>
/// Engineers act 10% more often than their race baseline
/// </summary>
public static class Engineer
{
    public static readonly Guid Id = new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d");
    public const string Name = "Engineer";

    public static readonly BlueprintDefinition Definition = new(Id, Name) { Build = Build, Class = new ClassFacet() };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        if (componentManager.GetPackedPool<ActionLockComponent>().Has(entityId))
        {
            componentManager.TryUpdate(entityId, static (ref ActionLockComponent actionLock) =>
            {
                actionLock.StandardLockFrames = MathUtility.ClampUShort((ushort)(actionLock.StandardLockFrames * 0.9m), 1, ushort.MaxValue);
            });
        }
        else
        {
            componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
            componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        }
    }
}
