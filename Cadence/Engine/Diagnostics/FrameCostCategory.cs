namespace Engine.Diagnostics;

/// <summary>Which half of the frame a recorded cost belongs to.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum FrameCostCategory
{
    Update,
    Draw,
}
