using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Utilities
{
    /// <summary>
    /// A structure for holding 3D integer vector values as opposed to XNA's built in Vector3 which uses floats.
    /// Used for positioning entities within the game world grid.
    /// </summary>
    public struct Vector3Int
    {
        /// <summary>
        /// The horizontal X component of the vector. 0 represents the left border of the world map.
        /// </summary>
        /// <value>A positive integer value</value>
        public int X;

        /// <summary>
        /// The vertical Y component of the vector. 0 represents the top border of the world map.
        /// </summary>
        /// <value>A positive integer value</value>
        public int Y;
        
        /// <summary>
        /// The depth Z component of the vector. 0 represents the lowest level of the world map.
        /// The value is used as an integer rather than an explicit enum to avoid boxing issues when used as a key or index.
        /// </summary>
        /// <value>A positive integer value bounded by the MapHeight enumeration</value>
        public int Z;

        public Vector3Int()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }

        public Vector3Int(int value)
        {
            X = value;
            Y = value;
            Z = value;
        }

        public Vector3Int(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vector3Int)
            {
                return Equals((Vector3Int)obj);
            }
            else
            {
                return false;
            }
        }

        public bool Equals(Vector3Int other)
        {
            return (X == other.X) && (Y == other.Y) && (Z == other.Z);
        }

        public static bool operator ==(Vector3Int value1, Vector3Int value2)
        {
            return (value1.X == value2.X) && (value1.Y == value2.Y) && (value1.Z == value2.Z);
        }

        public static bool operator !=(Vector3Int value1, Vector3Int value2)
        {
            return (value1.X != value2.X) || (value1.Y != value2.Y) || (value1.Z != value2.Z);
        }

        public static Vector3Int operator +(Vector3Int value1, Vector3Int value2)
        {
            return new Vector3Int(value1.X + value2.X, value1.Y + value2.Y, value1.Z + value2.Z);
        }

        public static Vector3Int operator -(Vector3Int value1, Vector3Int value2)
        {
            return new Vector3Int(value1.X - value2.X, value1.Y - value2.Y, value1.Z - value2.Z);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() + Y.GetHashCode() + Z.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{{X:{0} Y:{1} Z:{2}}}", X, Y, Z);
        }

        public readonly Point ToPointXY()
        {
            return new Point(X, Y);
        }
    }
}
