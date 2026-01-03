using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Utilities
{
    /// <summary>
    /// A structure for holding 3D byte vector values as opposed to XNA's built in Vector3 which uses floats.
    /// Used primarily for transform sizes.
    /// </summary>
    public struct Vector3Byte
    {
        /// <summary>
        /// The horizontal X component of the vector.
        /// </summary>
        /// <value>A positive Byteeger value</value>
        public byte X;

        /// <summary>
        /// The vertical Y component of the vector.
        /// </summary>
        /// <value>A positive Byteeger value</value>
        public byte Y;

        /// <summary>
        /// The depth Z component of the vector.
        /// </summary>
        /// <value>A positive Byteeger value bounded by the MapHeight enumeration</value>
        public byte Z;

        public Vector3Byte()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }

        public Vector3Byte(byte value)
        {
            X = value;
            Y = value;
            Z = value;
        }

        public Vector3Byte(byte x, byte y, byte z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vector3Byte)
            {
                return Equals((Vector3Byte)obj);
            }
            else
            {
                return false;
            }
        }

        public bool Equals(Vector3Byte other)
        {
            return (X == other.X) && (Y == other.Y) && (Z == other.Z);
        }

        public static bool operator ==(Vector3Byte value1, Vector3Byte value2)
        {
            return (value1.X == value2.X) && (value1.Y == value2.Y) && (value1.Z == value2.Z);
        }

        public static bool operator !=(Vector3Byte value1, Vector3Byte value2)
        {
            return (value1.X != value2.X) || (value1.Y != value2.Y) || (value1.Z != value2.Z);
        }

        public static Vector3Byte operator +(Vector3Byte value1, Vector3Byte value2)
        {
            return new Vector3Byte((byte)(value1.X + value2.X), (byte)(value1.Y + value2.Y), (byte)(value1.Z + value2.Z));
        }

        public static Vector3Byte operator -(Vector3Byte value1, Vector3Byte value2)
        {
            return new Vector3Byte((byte)(value1.X - value2.X), (byte)(value1.Y - value2.Y), (byte)(value1.Z - value2.Z));
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
