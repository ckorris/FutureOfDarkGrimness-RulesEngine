using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Data
{
    /// <summary>
    /// Holds the ID for a given type, which is pre-assigned in some way.
    /// </summary><remarks>
    /// As of writing, this is more of a placeholder, as it's easy to remove if int ends up being fine,
    /// but it's also much easier to expand if it's already pre-defined. But even if it only holds an int,
    /// it makes the intention of the int more clear.
    /// </remarks>
    public readonly struct TypeID : IEquatable<TypeID>
    {
        public readonly int ID;

        public TypeID(int id)
        {
            ID = id;
        }

        public bool Equals(TypeID other)
        {
            return ID == other.ID;
        }
        public static bool operator ==(TypeID a, TypeID b)
        {
            return a.ID == b.ID;
        }

        public static bool operator != (TypeID a, TypeID b)
        {
            return a.ID != b.ID;
        }

        public override bool Equals(object obj)
        {
            return obj is TypeID && Equals((TypeID)obj);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }
    }
}
