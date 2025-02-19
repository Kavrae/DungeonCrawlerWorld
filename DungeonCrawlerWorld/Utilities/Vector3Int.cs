using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Utilities
{
    public struct Vector3Int
    {
        public int X;
        public int Y;
        public int Z;

        public Vector3Int(int x, int y, int z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vector3Int) return this.Equals((Vector3Int)obj);
            else return false;
        }

        public bool Equals(Vector3Int other)
        {
            return ((this.X == other.X) && (this.Y == other.Y) && (this.Z == other.Z));
        }

        public static bool operator ==(Vector3Int value1, Vector3Int value2)
        {
            return ((value1.X == value2.X) && (value1.Y == value2.Y) && (value1.Z == value2.Z));
        }

        public static bool operator !=(Vector3Int value1, Vector3Int value2)
        {
            return value1.X != value2.X || value1.Y != value2.Y || value1.Z != value2.Z;
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
            return (this.X.GetHashCode() + this.Y.GetHashCode() + this.Z.GetHashCode());
        }

        public override string ToString()
        {
            return string.Format("{{X:{0} Y:{1} Z:{2}}}", this.X, this.Y, this.Z);
        }

        public readonly Point ToPointXY()
        {
            return new Point(X, Y);
        }
    }
}
