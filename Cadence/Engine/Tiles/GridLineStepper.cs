using Engine.Math;

namespace Engine.Tiles;

/// <summary>Bresenham's line from a start tile toward a through tile and on past it, one tile per Next.</summary>
internal struct GridLineStepper(Vector3Int start, Vector3Int through)
{
    private readonly int _absoluteDeltaX = System.Math.Abs(through.X - start.X);
    private readonly int _negativeAbsoluteDeltaY = -System.Math.Abs(through.Y - start.Y);
    private readonly int _signX = System.Math.Sign(through.X - start.X);
    private readonly int _signY = System.Math.Sign(through.Y - start.Y);
    private int _error = System.Math.Abs(through.X - start.X) - System.Math.Abs(through.Y - start.Y);
    private int _x = start.X;
    private int _y = start.Y;

    /// <summary>The next tile along the line.</summary>
    public Vector3Int Next()
    {
        var doubledError = 2 * _error;
        if (doubledError >= _negativeAbsoluteDeltaY)
        {
            _error += _negativeAbsoluteDeltaY;
            _x += _signX;
        }

        if (doubledError <= _absoluteDeltaX)
        {
            _error += _absoluteDeltaX;
            _y += _signY;
        }

        return new Vector3Int(_x, _y, start.Z);
    }
}
