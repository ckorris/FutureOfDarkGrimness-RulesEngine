using System;

namespace FDG
{
    [Serializable]
    public struct Position
    {
        public Float3 Position3D;

        public Float2 Position2D => Position3D.XZ;

        public float x => Position3D.X;
        public float y => Position3D.Y;
        public float z => Position3D.Z;


        public Position(Float2 position2D)
        {
            Position3D = new Float3(position2D.X, 0, position2D.Y);
        }

        public Position(Float3 position3D)
        {
            Position3D = position3D;
        }

        public static float GetDistance3D(Position a, Position b)
        {
            Float3 delta = new Float3(a.Position3D.X - b.Position3D.X, a.Position3D.Y - b.Position3D.Y, a.Position3D.Z - b.Position3D.Z);
            return (float)Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        }

        // 2D distance between two positions (ignores Z)
        public static float GetDistance2D(Position a, Position b)
        {
            Float2 delta = new Float2(a.Position3D.X - b.Position3D.X, a.Position3D.Y - b.Position3D.Y);
            return (float)Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        }

        // Vertical distance (difference in Z)
        public static float GetVerticalDistance(Position a, Position b)
        {
            return Math.Abs(a.Position3D.Z - b.Position3D.Z);
        }

        public static implicit operator Float2(Position a)
        {
            return a.Position2D;
        }

        public static implicit operator Float3(Position a)
        {
            return a.Position3D;
        }
    }
}
