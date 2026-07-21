using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Terrain;

/// <summary>A patch of ordinary grass terrain.</summary>
public sealed class Grass : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new BackgroundComponent(Color.ForestGreen));
        componentManager.Merge(entityId, new DisplayTextComponent("Grass", "Ordinary grass. Nothing special."));
        componentManager.Merge(entityId, new GlyphComponent(",", Color.LawnGreen));
        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
    }
}
