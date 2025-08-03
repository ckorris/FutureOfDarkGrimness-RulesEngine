
namespace FDG
{
    [Serializable]
    public struct Float2
    {
        public float X { get; private set; }
        public float Y { get; private set; }

        public Float2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Float2 operator +(Float2 a, Float2 b)
        {
            return new Float2(a.X + b.X, a.Y + b.Y);
        }

        public static Float2 operator -(Float2 a, Float2 b)
        {
            return new Float2(a.X - b.X, a.Y - b.Y);
        }

        public static Float2 operator *(Float2 a, float scalar)
        {
            return new Float2(a.X * scalar, a.Y * scalar);
        }

        public static Float2 operator *(Float2 a, Float2 b)
        {
            return new Float2(a.X * b.X, a.Y * b.Y);
        }

        public static bool operator ==(Float2 a, Float2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(Float2 a, Float2 b)
        {
            return !(a == b);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Float2))
                return false;

            Float2 other = (Float2)obj;
            return this == other;
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode();
        }
    }
}