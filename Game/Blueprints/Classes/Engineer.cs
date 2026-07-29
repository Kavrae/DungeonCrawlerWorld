using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;

namespace Game.Blueprints.Classes;

/// <summary>
/// Engineers act 10% more often than their race baseline
/// </summary>
public sealed class Engineer : IBlueprint
{
    private static readonly Guid ClassId = new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d");
    private const string ClassName = "Engineer";
    private const string Description = "TODO default engineer description";

    private const short BaselineActionCooldownFrames = 60;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ClassComponent(ClassId, ClassName, Description));

        if (componentManager.GetPackedPool<MovementComponent>().Has(entityId))
        {
            componentManager.TryUpdate(entityId, static (ref MovementComponent movementComponent) =>
            {
                movementComponent.ActionCooldownFrames = MathUtility.ClampShort((short)(movementComponent.ActionCooldownFrames * 0.9m), 1, short.MaxValue);
            });
        }
        else
        {
            componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, BaselineActionCooldownFrames, null, null));
            componentManager.Merge(entityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        }

        componentManager.Merge(entityId, new DisplayTextComponent(ClassName, Description));
    }
}