namespace Engine.Math;

/// <summary>Which grid-distance function a shape's own range check is measured against -- see GridDistance for both formulas. Defaults to Manhattan everywhere it's optional, matching every distance check in this codebase before this type existed.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum DistanceMetric : byte
{
    Manhattan,
    Chebyshev,
}
