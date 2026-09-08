using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Calculator
{
    /// <summary>
    /// #397: works out what happens when one unit attacks another, by BUILDING A REAL GAME WORLD and
    /// running the REAL combat stages in it - never by mirroring their arithmetic.
    ///
    /// <para>
    /// The project's standing rule is that a preview and the resolution it previews share one
    /// implementation (#325), because a second copy of the maths is a second thing to keep honest and
    /// it always loses. This takes that to its conclusion: a throwaway <c>GameDataStore</c>, both
    /// armies built through <see cref="GameBootstrap.CreateArmy"/> (so hero joins, combined units and
    /// rule attachment are the real ones), the models placed at the requested distance, and then
    /// <c>BuildTargetList -> DetermineHitRoll -> RollToHit -> DetermineSaveRollsNeeded -> RollToSave ->
    /// AssignWounds -> ApplyWounds</c> per weapon batch. Under the
    /// <see cref="ProbabilisticDiceRoller"/> every roll is its own expected value, so one pass through
    /// the real stages IS the forecast. Every special rule in the corpus - core, book-embedded, or
    /// authored tomorrow - prices itself for free, and there is nothing to drift.
    /// </para>
    ///
    /// <para>
    /// Wounds are APPLIED between batches, so a unit's second weapon fires into the casualties its
    /// first one caused and cannot over-kill a dead target. What the sandbox cannot know is listed in
    /// <see cref="CombatReport.Notes"/> rather than quietly assumed away: no terrain, clear line of
    /// sight, and no spendable markers or one-shot granted buffs (an empty world has none).
    /// </para>
    /// </summary>
    public static class CombatCalculator
    {
        /// <summary>
        /// Fixed so the same inputs always give the same report. Only decisive rolls consume it - the
        /// probabilistic roller's ordinary <c>Roll</c> never touches randomness.
        /// </summary>
        internal const int SandboxSeed = 3970397;

        private static readonly PlayerID AttackerID = new PlayerID(new Guid("39700000-0000-0000-0000-0000000000a0"));
        private static readonly PlayerID DefenderID = new PlayerID(new Guid("39700000-0000-0000-0000-0000000000d0"));

        // Well inside a notional table and far from the (0,0) sentinel that GetIsOnBattlefield and
        // ShotEligibility both read as "not placed yet".
        private const float OriginX = 24f;
        private const float LaneZ = 24f;

        // A separation comfortably past any base, used once to measure what the bases themselves span.
        private const float ProbeSeparation = 40f;

        /// <inheritdoc cref="RunAsync"/>
        public static CombatReport Run(ArmyListFile attackerArmy, ArmyListFile defenderArmy,
            CombatSituation situation)
            => RunAsync(attackerArmy, defenderArmy, situation).GetAwaiter().GetResult();

        /// <summary>
        /// Simulates the first army's single unit attacking the second's, under <paramref name="situation"/>.
        /// Each army is expected to hold the one unit being tested (a joined hero rides along in its host's
        /// list and is merged by army creation).
        /// </summary>
        public static async Task<CombatReport> RunAsync(ArmyListFile attackerArmy, ArmyListFile defenderArmy,
            CombatSituation situation)
        {
            ArgumentNullException.ThrowIfNull(attackerArmy);
            ArgumentNullException.ThrowIfNull(defenderArmy);
            ArgumentNullException.ThrowIfNull(situation);

            var notes = new List<string>();
            var warnings = new List<string>();

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            // Core rules first, then each army's embedded definitions - the precedence army load uses,
            // so a book that redefines a core rule by name behaves here exactly as it will in play.
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, attackerArmy);
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, defenderArmy);

            var context = new SandboxGameContext(store, new ProbabilisticDiceRoller(SandboxSeed), resolver,
                SandboxSeed, AttackerID, DefenderID);

            GameBootstrap.CreateArmy(AttackerID, attackerArmy, store, resolver);
            GameBootstrap.CreateArmy(DefenderID, defenderArmy, store, resolver);

            // CreateArmy does NOT apply Tough / Armor(X) / a joined hero's wounds / creation auras -
            // FDGServer and ScenarioCompiler each run this loop themselves once the armies exist. Skip
            // it and every model silently has one wound, which would be wrong everywhere at once.
            foreach (DataBinding<UnitData> binding in store.GetAllDataBindings<UnitData>())
            {
                UnitCreationRules.Apply(binding.GetValue(), context.RuleEvaluator);
            }

            List<DataBinding<UnitData>> attackerUnits = UnitsOf(store, AttackerID);
            List<DataBinding<UnitData>> defenderUnits = UnitsOf(store, DefenderID);
            if (attackerUnits.Count == 0 || defenderUnits.Count == 0)
            {
                warnings.Add("Both sides need a unit before anything can be worked out.");
                return new CombatReport(situation.Mode, attackerArmy.Name, defenderArmy.Name,
                    0f, 0f, Array.Empty<VolleyReport>(), 0f, 0f, notes, warnings);
            }

            DataBinding<UnitData> attacker = PickCombatant(attackerUnits, "Attacker", warnings);
            DataBinding<UnitData> defender = PickCombatant(defenderUnits, "Defender", warnings);
            UnitData attackingUnit = attacker.GetValue();
            UnitData defendingUnit = defender.GetValue();

            bool melee = situation.Mode == ECombatMode.Melee;
            PlaceAtGap(attacker, defender, melee ? 0f : MathF.Max(0f, situation.DistanceInches));

            if (situation.AttackerFatigued)
            {
                FatigueUtilities.ApplyFatigued(attackingUnit);
            }

            float woundsBefore = defendingUnit.RemainingWounds;

            // Batches are taken once, before a shot is fired: a volley is declared with the unit as it
            // stands, and pooling matches what CombatActionContext would build for the live action.
            var volleys = new List<VolleyReport>();
            foreach ((Weapon weapon, int copies) in Batches(attackingUnit, melee))
            {
                volleys.Add(await ResolveVolley(context, attacker, defender, weapon, copies, situation, melee));
            }

            float woundsAfter = defendingUnit.RemainingWounds;

            if (volleys.Count == 0)
            {
                notes.Add(melee
                    ? $"{attackingUnit.Name} has no melee weapons."
                    : $"{attackingUnit.Name} has no ranged weapons.");
            }

            notes.Add("Assumes no terrain, a clear line of sight, and no spent markers or one-shot buffs.");
            if (melee)
            {
                notes.Add("Assumes every model reaches base contact. Strike-back is not included.");
            }

            return new CombatReport(situation.Mode, attackingUnit.Name, defendingUnit.Name,
                woundsBefore, woundsAfter, volleys,
                volleys.Sum(volley => volley.ExpectedHits), woundsBefore - woundsAfter, notes, warnings);
        }

        /// <summary>Runs one weapon profile's batch through the real stage chain.</summary>
        private static async Task<VolleyReport> ResolveVolley(SandboxGameContext context,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, Weapon weapon, int copies,
            CombatSituation situation, bool melee)
        {
            UnitData attackingUnit = attacker.GetValue();
            UnitData defendingUnit = defender.GetValue();
            var volleyNotes = new List<string>();

            float effectiveRange = melee
                ? 0f
                : RangeRuleQueries.EffectiveRange(attackingUnit, weapon, defendingUnit, context.RuleEvaluator);

            // Shooting asks each carrier whether IT can reach, which is the question the live targeting
            // stage asks. Melee assumes the pile-in put every carrier in contact (noted on the report).
            int firing = melee
                ? copies
                : CarriersInRange(attackingUnit, defendingUnit, weapon, effectiveRange);

            if (firing <= 0)
            {
                volleyNotes.Add($"Out of range - reaches {effectiveRange:0.##}in.");
                return new VolleyReport(weapon, copies, InRange: false, effectiveRange, 0f, 0,
                    Array.Empty<string>(), 0f, Array.Empty<SaveBucket>(), Array.Empty<string>(), 0f,
                    volleyNotes);
            }

            if (firing < copies)
            {
                volleyNotes.Add($"{firing} of {copies} carriers in range.");
            }

            // Cover is geometry, which this world does not have, so it is seeded exactly as
            // CoverCheckStage would have produced it - including the engine's own ignore-cover query.
            int coverBonus = !melee && situation.DefenderInCover
                && !SightRuleQueries.IgnoresCover(attackingUnit, weapon, context.RuleEvaluator)
                ? 1
                : 0;

            var metadata = new CombatMetadata(context, attacker, defender, weapon, firing,
                attackerMoved: situation.AttackerMoved,
                isMelee: melee,
                isCharging: melee && situation.AttackerCharging);
            metadata.AddResult(new CoverCheckResults(coverBonus));

            float woundsBefore = defendingUnit.RemainingWounds;

            var layer = new PassThroughLayer<ICombatMetadata>();
            await RunStage(new BuildTargetListStage<ICombatMetadata>(context, layer), metadata);
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(context, layer), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(context, layer), metadata);
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(context, layer), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(context, layer), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(context, layer), metadata);
            await RunStage(new ApplyWoundsStage<ICombatMetadata>(context, layer), metadata);

            metadata.QueryForResult(out DetermineHitRollResults hitRoll);
            metadata.QueryForResult(out RollToHitResults toHit);
            metadata.QueryForResult(out DetermineSaveRollNeededResults saveNeeded);

            float expectedHits = toHit.SuccessfulHitList?.Sum(hit => hit.HitCount) ?? 0f;

            List<SaveBucket> buckets = (saveNeeded.PendingSaveRollsList ?? new List<PendingSaveRolls>())
                .Select(pending => new SaveBucket(pending.SaveNeeded, pending.HitCount,
                    BucketLabel(pending.Source)))
                .ToList();

            // What the defender actually lost - the honest figure, already capped by what it had left
            // and net of everything the wound stages did (Deadly, Regeneration, allocation).
            float dealt = woundsBefore - defendingUnit.RemainingWounds;

            return new VolleyReport(weapon, copies, InRange: true, effectiveRange,
                hitRoll.AttackCount, hitRoll.HitRollNeeded,
                hitRoll.ThresholdTags ?? (IReadOnlyList<string>)Array.Empty<string>(),
                expectedHits, buckets,
                saveNeeded.ThresholdTags ?? (IReadOnlyList<string>)Array.Empty<string>(),
                dealt, volleyNotes);
        }

        /// <summary>Binds the stage's exit to a sink and runs exactly that stage.</summary>
        private static Task RunStage<TResult, TSelf>(CombatStage<TResult, TSelf, ICombatMetadata> stage,
            CombatMetadata metadata)
            where TSelf : CombatStage<TResult, TSelf, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            return stage.Enter(metadata);
        }

        /// <summary>
        /// Puts both units on the table so the measured distance between them is exactly
        /// <paramref name="targetGap"/> inches, edge to edge.
        /// <para>
        /// Models are stacked rather than lined up, on purpose. Distance is measured base-to-base, so a
        /// stack means every carrier is the same distance from the target and casualties never shift it
        /// - with a line, the far models drift out of range as the near ones die. Nothing in the attack
        /// chain reads intra-unit geometry (no occlusion blockers, no cover shapes; wounds are allocated
        /// by wound state, not position), so the overlap is inert.
        /// </para>
        /// <para>
        /// The bases are measured, not calculated: at the probe separation the nearest points lie along
        /// X, so the engine's own distance function reveals the combined half-extents in one reading.
        /// That works for any base shape, including ones added later.
        /// </para>
        /// </summary>
        private static void PlaceAtGap(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            float targetGap)
        {
            PlaceAll(attacker, OriginX);
            PlaceAll(defender, OriginX + ProbeSeparation);

            float probed = UnitCompareUtilities.MinDistanceBetweenUnits(attacker.GetValue(),
                defender.GetValue(), out _, out _, includeVertical: true);
            float combinedHalfExtents = ProbeSeparation - probed;

            PlaceAll(defender, OriginX + targetGap + combinedHalfExtents);
        }

        private static void PlaceAll(DataBinding<UnitData> unit, float x)
        {
            foreach (IModel model in unit.GetValue().Models)
            {
                model.SetPosition(new Position(x, LaneZ));
            }
        }

        /// <summary>
        /// How many copies of <paramref name="weapon"/> can actually reach: the per-model question
        /// <c>ChooseRangedAttackStage</c> asks, so a weapon whose carriers are all too far away reports
        /// nothing firing rather than rolling dice it would never get to roll.
        /// </summary>
        private static int CarriersInRange(UnitData attacker, UnitData defender, Weapon weapon,
            float rangeInches)
        {
            var comparer = new WeaponComparer();
            int firing = 0;

            foreach (IModel model in attacker.Models)
            {
                if (!model.GetIsAlive())
                {
                    continue;
                }

                int copiesOnModel = model.Weapons.Count(carried => comparer.Equals(carried, weapon));
                if (copiesOnModel == 0)
                {
                    continue;
                }

                if (ShotEligibility.CanHitAny(model.Position, model.BaseShape, model.Facing,
                        defender.Models, blockers: null, rangeInches))
                {
                    firing += copiesOnModel;
                }
            }

            return firing;
        }

        /// <summary>
        /// The unit's weapons pooled by profile, exactly as the live combat context pools them, in a
        /// stable order - #209's profile key, so two runs never disagree about which weapon fires first.
        /// </summary>
        private static List<(Weapon Weapon, int Copies)> Batches(UnitData unit, bool melee)
        {
            List<Weapon> weapons = melee ? unit.GetMeleeWeapons() : unit.GetRangedWeapons();

            return WeaponPool.GroupByProfile(weapons)
                .Select(pair => (Weapon: pair.Key, Copies: pair.Value))
                .OrderBy(batch => WeaponProfileKey.For(batch.Weapon), StringComparer.Ordinal)
                .ToList();
        }

        private static string BucketLabel(HitGroupSource source) => source.Kind switch
        {
            EHitSourceKind.BaseRolled => string.Empty,
            EHitSourceKind.BlastMultiplier => $"{source.RuleName} x{source.Amount:0.##}",
            EHitSourceKind.PerHitAp => $"{source.RuleName} AP+{source.Amount:0.##}",
            _ => source.RuleName,
        };

        private static List<DataBinding<UnitData>> UnitsOf(GameDataStore store, PlayerID player)
        {
            var units = new List<DataBinding<UnitData>>();
            if (!store.IsTypeAssigned<ArmyData>())
            {
                return units;
            }

            foreach (ArmyData army in store.GetAllValues<ArmyData>())
            {
                if (army.PlayerID == player)
                {
                    units.AddRange(army.UnitBindings);
                }
            }

            return units;
        }

        /// <summary>
        /// The unit that fights. Normally there is exactly one: a joined hero is absorbed into its host
        /// by army creation. More than one means a join was REFUSED (Tough over the cap, an ineligible
        /// host), so the host fights and the leftover is named rather than silently ignored.
        /// </summary>
        private static DataBinding<UnitData> PickCombatant(List<DataBinding<UnitData>> units, string side,
            List<string> warnings)
        {
            if (units.Count == 1)
            {
                return units[0];
            }

            DataBinding<UnitData> host = units
                .OrderByDescending(unit => unit.GetValue().Models.Count)
                .First();

            foreach (DataBinding<UnitData> unit in units)
            {
                if (!ReferenceEquals(unit.GetValue(), host.GetValue()))
                {
                    warnings.Add($"{side}: \"{unit.GetValue().Name}\" did not join the unit and is left out.");
                }
            }

            return host;
        }
    }
}
