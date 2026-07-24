using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>
/// The player character: rendered as the ASCII-standard '@', moved by MapWindow's input
/// handling (MovementMode.PlayerControlled -- see MovementSystem.SetNextMapPosition) rather
/// than any algorithmic selection. No RaceComponent/ClassComponent -- nothing today needs the
/// player to have either. No OccupancyComponent -- its absence already means "ordinary
/// Blocking, not Tiny/Phasing" (see OccupancyComponent's own doc comment), exactly right for
/// the player.
/// </summary>
public sealed class PlayerBlueprint(MathUtility mathUtility) : IBlueprint
{
    private const short MaximumEnergy = 100;
    private const short EnergyRecharge = 10;
    private const short EnergyToMove = 10;

    private const short MaximumHealth = 100;
    private const short HealthRegen = 10;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new GlyphComponent("@", Color.White));
        componentManager.Merge(entityId, new EnergyComponent(MaximumEnergy, EnergyRecharge, MaximumEnergy));
        componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, MaximumHealth + 1), HealthRegen, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.PlayerControlled, EnergyToMove, null, null));
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
    }
}
