using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Terrain;

/// <summary>A patch of ordinary dirt terrain.</summary>
public sealed class Dirt : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new BackgroundComponent(Color.Tan));
        componentManager.Merge(entityId, new DisplayTextComponent("Dirt", "Ordinary dirt. Nothing special."));
        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapHeight.Ground), new Vector3Byte(1, 1, 1)));
    }
}
