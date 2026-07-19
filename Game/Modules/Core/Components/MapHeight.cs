namespace Game.Modules.Core.Components;

/// <summary>The Z height/layer of a map node or entity on the map.</summary>
public enum MapHeight : byte
{
    UnderGround = 0,
    Ground = 1,
    Standing = 2,
    Riding = 3,
    Floating = 4,
    Flying = 5,
}
