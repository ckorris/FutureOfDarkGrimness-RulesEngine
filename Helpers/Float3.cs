
namespace FDG
{
    [System.Serializable]
    public struct Float3
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }

        public Float2 XY => new Float2(X, Y);

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Float3 operator +(Float3 a, Float3 b)
        {
            return new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Float3 operator -(Float3 a, Float3 b)
        {
            return new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Float3 operator *(Float3 a, float scalar)
        {
            return new Float3(a.X * scalar, a.Y * scalar, a.Z * scalar);
        }

        public static Float3 operator *(Float3 a, Float3 b)
        {
            return new Float3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }

        public static bool operator ==(Float3 a, Float3 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }

        public static bool operator !=(Float3 a, Float3 b)
        {
            return !(a == b);
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Float3))
                return false;

            Float3 other = (Float3)obj;
            return this == other;
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
        }
    }
}
