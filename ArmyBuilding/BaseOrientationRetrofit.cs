using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #225 — a one-off data migration for rectangular base orientation.
    //
    // OprBookImporter used to map OPR's "LxW" base spec positionally (length -> Width, width -> Height),
    // but the engine runs RectangleBase's local +Z (HeightInches) along the facing. Every rectangular
    // model therefore presented its LONG axis as frontage: a 60x35 bike faced across its 35mm axis,
    // inflating the footprint that LoS, overlap and coherency all measure against (#150).
    //
    // The importer is fixed at source, but the 48 bundled books and the armies built from them were
    // emitted under the old mapping and cannot be re-imported (the OPR source JSON is not in the repo).
    // This walks the existing data and swaps them in place.
    //
    // Idempotent by construction: it only swaps when Width > Height, and a corrected base has
    // Height >= Width, so a second run is a no-op. That guard doubles as the correctness rule — a real
    // base is never wider than it is long along the facing axis (a bike, tank or monster is longer than
    // it is broad). Squares (Width == Height) are left alone.
    public static class BaseOrientationRetrofit
    {
        /// <summary>Swaps mis-oriented rectangular bases on every unit in a book.
        /// Returns true when at least one entry changed.</summary>
        public static bool ApplyToBook(BookFile book)
        {
            bool changed = false;
            foreach (RosterUnit unit in book.Units)
                changed |= Fix(unit.Base);
            return changed;
        }

        /// <summary>Swaps mis-oriented rectangular bases on an army's compiled units, and — for a forge
        /// army — on its embedded book snapshot too, so re-opening the list in the builder does not
        /// reintroduce the old orientation. Returns true when at least one entry changed.</summary>
        public static bool ApplyToArmy(ArmyListFile army)
        {
            bool changed = false;
            foreach (UnitFileEntry unit in army.Units)
                changed |= Fix(unit.Base);
            if (army is BuiltArmyFile built && built.Book != null)
                changed |= ApplyToBook(built.Book);
            return changed;
        }

        private static bool Fix(BaseFileEntry entry)
        {
            if (entry.Shape != EBaseShapeKind.Rectangle || entry.WidthInches <= entry.HeightInches)
                return false;
            (entry.WidthInches, entry.HeightInches) = (entry.HeightInches, entry.WidthInches);
            return true;
        }
    }
}
