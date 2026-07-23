using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Terrain;

/// <summary>
/// A patch of hot lava terrain. Purely visual for now -- damage-over-time, burn stacks, and
/// a selection icon are potential future additions, not yet built.
/// </summary>
public sealed class Lava : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new BackgroundComponent(Color.OrangeRed));
        componentManager.Merge(entityId, new DisplayTextComponent("Lava", "Hot lava. I do not recommend stepping on it."));
        componentManager.Merge(entityId, new GlyphComponent("~", Color.Yellow));
        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
    }
}