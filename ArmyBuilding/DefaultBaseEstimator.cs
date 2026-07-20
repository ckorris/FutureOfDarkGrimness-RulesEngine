using System.Collections.Generic;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #225 defect B — a plausible base for a unit OPR declares no base for.
    //
    // OPR emits bases {round:"none", square:""} for vehicles and superheavies: the model's own hull IS its
    // footprint, so there is nothing to import. The importer used to fall through to a bare
    // BaseFileEntry() — the 28mm circle default — which landed on precisely the LARGEST models in the
    // game. A Tough(24) titan collided, blocked line of sight and measured as a 28mm dot.
    //
    // There is no right answer to recover here, only a plausible one, so this estimates from what the unit
    // data does tell us. Two axes, both taken from the affected population (102 units across the bundled
    // books, every one of which carries a Tough rule, in six discrete buckets):
    //
    //   * Hero  -> the unit is a named character or monstrous creature, not a vehicle. All six Tough(3)
    //             units in the affected set are Hero+Unique named characters; the four larger Heroes are
    //             big creatures (a flying Tough(12) hive lord, Tough(6) named beasts). These get CIRCLES.
    //   * Tough -> a size proxy within each class. Vehicles get RECTANGLES, since a tank hull is longer
    //             than it is wide.
    //
    // Every size below is a tuple that already occurs in the bundled corpus, so an estimated base never
    // looks alien next to an imported one. Rectangles are authored LENGTH x WIDTH and stored with length
    // on HeightInches (the facing axis) — see #225 defect A.
    //
    // These are estimates, deliberately tunable: adjust the two tables and re-run the retrofit.
    public static class DefaultBaseEstimator
    {
        private const float MmPerInch = 25.4f;

        // The pre-#149 hardcoded default the importer used to fall through to. A unit still carrying it
        // has no real base data.
        private const float UnsizedDefaultDiameterInches = 1.1023622f; // 28mm

        /// <summary>
        /// True when <paramref name="entry"/> is the untouched 28mm-circle default rather than a real
        /// imported base — the marker for "OPR declared no base here".
        ///
        /// <para>Gates on <see cref="EBaseShapeKind.Circle"/> first, deliberately: every Rectangle in the
        /// data also carries a leftover 28mm <c>DiameterInches</c> in its unused field, so a diameter test
        /// alone would flag correctly-sized rectangles. 28mm is safe as a marker because OPR's round bases
        /// are 25/32/40/50/60mm — no real unit resolves to 28mm.</para>
        /// </summary>
        public static bool IsUnsizedDefault(BaseFileEntry entry) =>
            entry.Shape == EBaseShapeKind.Circle
            && entry.DiameterInches > UnsizedDefaultDiameterInches - 1e-4f
            && entry.DiameterInches < UnsizedDefaultDiameterInches + 1e-4f;

        /// <summary>
        /// A plausible base for a unit with no declared one, from its special rules. See the class remarks
        /// for the reasoning; <paramref name="describe"/> receives a one-line summary of what was chosen.
        /// </summary>
        public static BaseFileEntry Estimate(IEnumerable<SpecialRuleEntry> rules, out string describe)
        {
            bool isHero = HasRule(rules, "Hero");
            int tough = ToughValue(rules);

            if (isHero)
            {
                // Character or monstrous creature: round base, sized by bulk.
                float mm = tough <= 3 ? 40f
                         : tough <= 6 ? 50f
                         : 60f;
                describe = $"Hero, Tough({tough}) -> {mm:0}mm circle";
                return new BaseFileEntry
                {
                    Shape = EBaseShapeKind.Circle,
                    DiameterInches = mm / MmPerInch,
                };
            }

            // Vehicle: rectangular hull, length along the facing.
            (float lengthMm, float widthMm) = tough <= 6 ? (90f, 52f)
                                            : tough <= 9 ? (105f, 70f)
                                            : tough <= 12 ? (120f, 92f)
                                            : tough <= 18 ? (160f, 122f)
                                            : (175f, 125f);
            describe = $"Tough({tough}) -> {lengthMm:0}x{widthMm:0}mm rectangle";
            return new BaseFileEntry
            {
                Shape = EBaseShapeKind.Rectangle,
                HeightInches = lengthMm / MmPerInch,  // length runs along the facing (#225 defect A)
                WidthInches = widthMm / MmPerInch,
            };
        }

        private static bool HasRule(IEnumerable<SpecialRuleEntry> rules, string name)
        {
            foreach (SpecialRuleEntry entry in rules)
                if (Matches(entry, name))
                    return true;
            return false;
        }

        // Alias-aware, mirroring ListValidator's rule lookups: a renamed rule resolves to what it renames.
        private static bool Matches(SpecialRuleEntry entry, string name) => entry switch
        {
            SpecialRuleEntry_Core core => string.Equals(core.Name, name, System.StringComparison.OrdinalIgnoreCase),
            SpecialRuleEntry_CoreNumeric num => string.Equals(num.Name, name, System.StringComparison.OrdinalIgnoreCase),
            SpecialRuleEntry_Alias alias => string.Equals(alias.Name, name, System.StringComparison.OrdinalIgnoreCase)
                || Matches(alias.AliasedRule, name),
            _ => false,
        };

        private static int ToughValue(IEnumerable<SpecialRuleEntry> rules)
        {
            foreach (SpecialRuleEntry entry in rules)
                if (TryGetTough(entry, out int tough))
                    return tough;
            return 0;
        }

        private static bool TryGetTough(SpecialRuleEntry entry, out int tough)
        {
            switch (entry)
            {
                case SpecialRuleEntry_CoreNumeric num when string.Equals(num.Name, "Tough", System.StringComparison.OrdinalIgnoreCase):
                    tough = num.NumericValue; return true;
                case SpecialRuleEntry_Alias alias:
                    return TryGetTough(alias.AliasedRule, out tough);
                default:
                    tough = 0; return false;
            }
        }
    }
}
