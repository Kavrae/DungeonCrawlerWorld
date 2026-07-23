using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Terrain;

/// <summary>A patch of roughly shaped stone floor terrain.</summary>
public sealed class StoneFloor : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new BackgroundComponent(Color.LightGray));
        componentManager.Merge(entityId, new DisplayTextComponent("Stone floor", "Roughly shaped stone floor."));
        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
    }
}