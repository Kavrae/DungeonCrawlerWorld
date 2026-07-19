using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>A basic wall object.</summary>
public sealed class Wall : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new DisplayTextComponent("Wall", "Basic wall. Default implementation."));
        componentManager.Merge(entityId, new GlyphComponent("[][]", Color.DarkGray, new Point(0, -1)));
        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapHeight.Standing), new Vector3Byte(1, 1, 1)));
    }
}
