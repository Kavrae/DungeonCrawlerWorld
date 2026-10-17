namespace Engine.Tiles;

/// <summary>Which grid-distance function a shape's own range check is measured against -- see GridDistance for both formulas. Manhattan is the default wherever it is optional.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum DistanceMetric : byte
{
    Manhattan,
    Chebyshev,
}
