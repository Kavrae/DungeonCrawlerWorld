using Microsoft.Xna.Framework;

namespace Game.Modules.Core.Components;

/// <summary>
/// The background color for the map tile the entity is on. When multiple entities share
/// an XY position at different Z, the highest one's BackgroundComponent wins.
/// </summary>
public struct BackgroundComponent(Color backgroundColor)
{
    public Color BackgroundColor { get; set; } = backgroundColor;

    public override readonly string ToString() => $"Background : {BackgroundColor}";
}