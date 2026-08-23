using System;
using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #357 — run ListCompiler BACKWARDS: given a compiled army and the book it was built from, find the
    // upgrade picks that produce it. This is what a plain .fdgarmy is missing (it stores the RESULT of the
    // picks, never the picks), and it is the only way an already-saved army becomes catalog-editable again.
    //
    // The search is exhaustive-with-a-budget rather than clever: enumerate each roster unit's option space
    // depth-first, compile every candidate through the REAL compiler, and accept only an exact match. That
    // sidesteps having to re-derive the replace-chain algebra (#218/#261/#323/#324) in reverse - the
    // compiler stays the single authority on what a pick-set means, and a wrong guess cannot pass.
    //
    // Deliberately NOT inferred: combined pairs (#107) and hero joins (#006). Both are cross-unit structure
    // rather than per-unit picks; a unit carrying either is reported unsolved rather than guessed at.

    /// <summary>The outcome for one compiled unit.</summary>
    public sealed class UnitSolve
    {
        public required string UnitName { get; init; }

        /// <summary>The picks that reproduce this unit: one entry normally, two for a combined pair (#107).
        /// Empty when nothing was found.</summary>
        public IReadOnlyList<BuilderUnit> Selection { get; init; } = Array.Empty<BuilderUnit>();

        /// <summary>Why it could not be solved. Null when <see cref="Selection"/> is set.</summary>
        public string? Failure { get; init; }

        /// <summary>Our compiled price minus the saved one. Non-zero is expected and not an error on an
        /// Army Forge import - their per-unit costs are theirs, and some options they never publish a price
        /// for at all (#218/#219). The picks are still the right picks.</summary>
        public int PointsDelta { get; init; }

        public bool Solved => Selection.Count > 0;

        /// <summary>True when this unit turned out to be two roster copies merged at compile time.</summary>
        public bool IsCombinedPair => Selection.Count == 2;
    }

    /// <summary>The outcome for a whole army. <see cref="Selections"/> is populated only when EVERY unit
    /// solved - a partial answer is not a list, and attaching one would make the army's editable half
    /// quietly disagree with the army itself.</summary>
    public sealed class ArmySolve
    {
        public required IReadOnlyList<UnitSolve> Units { get; init; }
        public BuilderList? Selections { get; init; }
        public bool Complete => Selections is not null;
        public int SolvedCount => Units.Count(u => u.Solved);
    }

    public static class SelectionSolver
    {
        /// <summary>Candidate pick-sets to try per unit before giving up. The measured corpus needs a median
        /// of 16 and never exceeds ~270k, so this clears almost everything while bounding the pathological
        /// tail (a unit with many counted sections) to something a CLI run can wait for.</summary>
        public const int DefaultCombinationCap = 100_000;

        public static ArmySolve Solve(BookFile book, ArmyListFile army, int combinationCap = DefaultCombinationCap)
        {
            if (book is null) throw new ArgumentNullException(nameof(book));
            if (army is null) throw new ArgumentNullException(nameof(army));

            var results = new List<UnitSolve>();
            foreach (UnitFileEntry entry in army.Units)
                results.Add(SolveUnit(book, entry, combinationCap));

            BuilderList? selections = null;
            if (results.Count > 0 && results.All(r => r.Solved))
            {
                selections = new BuilderList
                {
                    Name = army.Name,
                    BookName = book.Name,
                    PointsLimit = army.PointsLimit,
                };
                foreach (UnitSolve r in results) selections.Units.AddRange(r.Selection);
            }

            return new ArmySolve { Units = results, Selections = selections };
        }

        /// <summary>Search state for one unit: the remaining candidate budget and the best pick-set found so
        /// far (best = smallest price disagreement; an exact one ends the search immediately).</summary>
        private sealed class Search
        {
            public int Budget;
            public BuilderUnit? Best;
            public int BestDelta = int.MaxValue;
            public bool Exact => Best is not null && BestDelta == 0;
        }

        private static UnitSolve SolveUnit(BookFile book, UnitFileEntry entry, int combinationCap)
        {
            UnitSolve Fail(string why) => new() { UnitName = entry.Name, Failure = why };

            // A combined pair (#107) compiles to ONE merged unit. Some carry a "(Combined)" suffix the roster
            // has no entry for; others keep the plain name and are only detectable by the fact that no single
            // copy can account for the size (Robot Legions Warriors: 10 models from a 5-model roster entry
            // with no add-models section). So the suffix is stripped when present, and the pair reading is
            // simply tried whenever the single-copy one fails.
            string rosterName = StripCombinedSuffix(entry.Name);
            List<RosterUnit> candidates = book.Units.Where(u => u.Name == rosterName).ToList();
            if (candidates.Count == 0)
                return Fail($"no unit named '{rosterName}' in book '{book.Name}'");

            var search = new Search { Budget = combinationCap };
            foreach (RosterUnit roster in candidates)
            {
                Descend(book, roster, entry, 0, new List<UpgradeChoice>(), search);
                if (search.Exact || search.Budget <= 0) break;
            }

            if (search.Best is not null)
                return Solved(entry, search, new List<BuilderUnit> { search.Best });

            var pairSearch = new Search { Budget = combinationCap };
            foreach (RosterUnit roster in candidates)
            {
                DescendPair(book, roster, entry, 0, new List<UpgradeChoice>(), pairSearch);
                if (pairSearch.Exact || pairSearch.Budget <= 0) break;
            }

            if (pairSearch.Best is not null)
            {
                BuilderUnit second = Clone(pairSearch.Best);
                second.Id = (entry.Id ?? entry.Name) + "-combined";
                second.CombinedWithId = entry.Id;
                return Solved(entry, pairSearch, new List<BuilderUnit> { pairSearch.Best, second });
            }

            return Fail(search.Budget <= 0 || pairSearch.Budget <= 0
                ? $"gave up after {combinationCap} candidate pick-sets (option space too large)"
                : "no combination of this unit's upgrades, alone or as a combined pair, reproduces its "
                  + "weapons and size");
        }

        private static UnitSolve Solved(UnitFileEntry entry, Search search, List<BuilderUnit> units)
        {
            units[0].Id = entry.Id;
            // A joined hero (#006) is its own entry in the file and its own unit in the list; the link is a
            // plain id reference, so it carries across as-is once the hero itself has been solved.
            units[0].JoinsUnitId = entry.JoinsUnitId;
            return new UnitSolve
            {
                UnitName = entry.Name, Selection = units, PointsDelta = search.BestDelta,
            };
        }

        private static string StripCombinedSuffix(string name) => EditableSession.NormalizeUnitName(name);

        private static BuilderUnit Clone(BuilderUnit unit) => new()
        {
            RosterUnitId = unit.RosterUnitId,
            ModelCount = unit.ModelCount,
            Choices = unit.Choices.Select(c => new UpgradeChoice
            {
                SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count,
            }).ToList(),
        };

        // Depth-first over the roster unit's sections. Each section contributes zero or more choices; a leaf
        // is a complete pick-set, which is compiled and compared against the target.
        private static void Descend(BookFile book, RosterUnit roster, UnitFileEntry target,
            int sectionIndex, List<UpgradeChoice> chosen, Search search)
        {
            if (search.Budget <= 0 || search.Exact) return;

            if (sectionIndex >= roster.Sections.Count)
            {
                search.Budget--;
                var candidate = new BuilderUnit
                {
                    RosterUnitId = roster.Id,
                    ModelCount = roster.BaseModelCount,
                    Choices = chosen.Select(c => new UpgradeChoice
                    {
                        SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count,
                    }).ToList(),
                };
                (UnitFileEntry compiled, _) = ListCompiler.CompileUnitDetailed(book, candidate);
                if (!Matches(compiled, target)) return;

                int delta = compiled.PointCost - target.PointCost;
                if (Math.Abs(delta) >= Math.Abs(search.BestDelta)) return;
                search.Best = candidate;
                search.BestDelta = delta;
                return;
            }

            UpgradeSection section = roster.Sections[sectionIndex];
            foreach (List<UpgradeChoice> variant in SectionVariants(roster, section, target))
            {
                int before = chosen.Count;
                chosen.AddRange(variant);
                Descend(book, roster, target, sectionIndex + 1, chosen, search);
                chosen.RemoveRange(before, chosen.Count - before);
                if (search.Budget <= 0 || search.Exact) return;
            }
        }

        // The same walk, but each leaf is compiled as TWO copies of the roster unit merged (#107). Only
        // symmetric pairs are tried - both copies taking the same picks - which is what a doubled-up squad
        // is in practice, and it keeps the space the same size as the single-copy search rather than
        // squaring it. An asymmetric pair (the two halves upgraded differently) is left unsolved.
        private static void DescendPair(BookFile book, RosterUnit roster, UnitFileEntry target,
            int sectionIndex, List<UpgradeChoice> chosen, Search search)
        {
            if (search.Budget <= 0 || search.Exact) return;

            if (sectionIndex >= roster.Sections.Count)
            {
                search.Budget--;
                var first = new BuilderUnit
                {
                    RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount, Id = "pair-a",
                    Choices = chosen.Select(c => new UpgradeChoice
                    {
                        SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count,
                    }).ToList(),
                };
                BuilderUnit second = Clone(first);
                second.Id = "pair-b";
                second.CombinedWithId = "pair-a";

                var pair = new BuilderList { BookName = book.Name, Units = { first, second } };
                BuiltArmyFile compiled = ListCompiler.Compile(book, pair);
                if (compiled.Units.Count != 1) return; // the pair did not merge - not a valid combine here
                if (!Matches(compiled.Units[0], target)) return;

                int delta = compiled.Units[0].PointCost - target.PointCost;
                if (Math.Abs(delta) >= Math.Abs(search.BestDelta)) return;
                first.Id = null;
                search.Best = first;
                search.BestDelta = delta;
                return;
            }

            UpgradeSection section = roster.Sections[sectionIndex];
            foreach (List<UpgradeChoice> variant in SectionVariants(roster, section, target))
            {
                int before = chosen.Count;
                chosen.AddRange(variant);
                DescendPair(book, roster, target, sectionIndex + 1, chosen, search);
                chosen.RemoveRange(before, chosen.Count - before);
                if (search.Budget <= 0 || search.Exact) return;
            }
        }

        // Every way one section can be left: nothing, or one of its options (at each legal application count
        // for a counted section). Multi-option counted combinations within one section are deliberately not
        // enumerated - no bundled book needs them, and they are what makes the space explode.
        private static IEnumerable<List<UpgradeChoice>> SectionVariants(
            RosterUnit roster, UpgradeSection section, UnitFileEntry target)
        {
            yield return new List<UpgradeChoice>(); // section untouched

            foreach (UpgradeOption option in section.Options)
            {
                if (!section.IsCounted)
                {
                    yield return new List<UpgradeChoice>
                    {
                        new() { SectionId = section.Id, OptionId = option.Id, Count = 1 },
                    };
                    continue;
                }

                for (int count = 1; count <= CountedBound(roster, section, target); count++)
                    yield return new List<UpgradeChoice>
                    {
                        new() { SectionId = section.Id, OptionId = option.Id, Count = count },
                    };
            }
        }

        // How far a counted section can go before it cannot possibly match: model-adding sections are capped
        // by the size the target actually is, replacements by how many copies of their targets could exist.
        //
        // "Could" is doing real work there. The base loadout is not the ceiling: another section may GRANT
        // copies of this section's target, and then this one can apply more often than the roster entry
        // alone suggests - the #323 starved-replace shape (a Titan whose shield swaps into a second Heavy
        // Hammer, after which "Replace any Heavy Hammer" applies twice). Counting only the base loadout
        // silently puts the real answer outside the search.
        private static int CountedBound(RosterUnit roster, UpgradeSection section, UnitFileEntry target)
        {
            if (section.Variant == UpgradeVariant.AddModels)
                return Math.Max(0, target.ModelCount - roster.BaseModelCount);

            int declared = section.MaxApplications > 0 ? section.MaxApplications : int.MaxValue;
            // #383: a per-model section applies at most once per model, so the target's size caps the
            // search — and for the targets-less attachment form it is the WHOLE bound (there is no weapon
            // pool to consume, which the availability sum below would misread as "can never apply").
            if (section.PerModelBudget)
            {
                declared = Math.Min(declared, target.ModelCount);
                if (section.Targets.Count == 0) return declared;
            }
            int available = ListCompiler.AvailableApplications(roster.Weapons, roster.Items, section.Targets)
                + GrantableCopies(roster, section);
            return Math.Min(declared, Math.Max(available, 0));
        }

        /// <summary>How many copies of <paramref name="section"/>'s targets the unit's OTHER sections can
        /// hand it. An upper bound, not a prediction - it only has to be generous enough that the true
        /// application count is inside the search.</summary>
        private static int GrantableCopies(RosterUnit roster, UpgradeSection section)
        {
            var targets = section.Targets.Select(TargetName).ToList();
            if (targets.Count == 0) return 0;

            int total = 0;
            foreach (UpgradeSection other in roster.Sections)
            {
                if (ReferenceEquals(other, section)) continue;
                foreach (UpgradeOption option in other.Options)
                {
                    foreach (WeaponFileEntry gained in option.WeaponsGained)
                        if (targets.Any(t => ListCompiler.TargetMatches(gained.Name, t)))
                            total += Math.Max(1, gained.Quantity);
                    foreach (ItemEntry gained in option.ItemsGained)
                        if (targets.Any(t => ListCompiler.TargetMatches(gained.Name, t)))
                            total += Math.Max(1, gained.Quantity);
                }
            }
            return total;
        }

        // "2x Rapid Shard Cannon" targets the weapon named "Rapid Shard Cannon" (#261); the quantity prefix
        // is about how many copies one application consumes, which is not what this bound needs.
        private static string TargetName(string target)
        {
            int x = target.IndexOf('x');
            if (x > 0 && int.TryParse(target[..x].Trim(), out _)) return target[(x + 1)..].Trim();
            return target;
        }

        /// <summary>A candidate reproduces the target when its size and weapon loadout agree.
        ///
        /// Price is deliberately NOT part of the test, only the tie-break: on an Army Forge import the saved
        /// cost is THEIR number, and our compiler is known to disagree on some units (#218) and to count some
        /// options as free because OPR publishes no price for them at all (#219). Requiring equality would
        /// reject the correct picks on exactly the armies this exists to convert.
        ///
        /// Special rules are not compared either - an imported unit's rules come from OPR's data while a
        /// compiled one's come from our book, so they can differ in representation without the picks being
        /// wrong. Size plus the full weapon loadout is a strong signature; two pick-sets that agree on both
        /// produce the same unit on the table.</summary>
        private static bool Matches(UnitFileEntry candidate, UnitFileEntry target) =>
            candidate.ModelCount == target.ModelCount
            && WeaponSignature(candidate) == WeaponSignature(target);

        private static string WeaponSignature(UnitFileEntry unit) =>
            string.Join("|", unit.Weapons
                .GroupBy(w => w.Name, StringComparer.Ordinal)
                .Select(g => $"{g.Key}x{g.Sum(w => w.Quantity)}")
                .OrderBy(s => s, StringComparer.Ordinal));
    }
}
