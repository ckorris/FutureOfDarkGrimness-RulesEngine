using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #423 Parry(X), Core Rules v3.6.0: "When this unit takes hits in melee, roll X dice per model with this rule in
    /// it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)." Sits on the
    /// defender's side between the hit roll and <c>DetermineSaveRollsNeededStage</c> in both melee chains - every weapon
    /// profile the attacker swings (<see cref="SwingMeleeWeaponStage"/>) and the charge's Impact hits
    /// (<see cref="ResolveImpactHitsStage"/>) - and does nothing for shooting, for a whiff, or for a defender without
    /// the rule.
    ///
    /// <para><b>The pick is made by the engine</b> (D1, owner sign-off 2026-10-30): the hits of one swing differ in
    /// exactly one way, their save threshold (the base-AP group plus the natural-6 group(s) <see cref="PerHitApSplitter"/>
    /// peels for Rending / Crack), so ignoring the highest-threshold hit first is strictly best and the "defender picks
    /// which" has one right answer. <see cref="RemoveWorstHitsFirst"/> edits the <see cref="RollToHitResults"/> groups
    /// in place (identity and <see cref="HitGroupSource"/> kept, so the save beats still say "Rending AP+1"). The
    /// moment a second per-hit dimension exists (a per-hit wound multiplier, a per-hit rule) this stops being a total
    /// order and the pick question must be re-presented to the owner.</para>
    ///
    /// <para><b>Visibility is the point</b>: the roll is a <see cref="DiceRolledBeat"/> in the same channel as the hit
    /// and save rolls (6+ highlighted), its result line names the parried groups worst first
    /// (<see cref="Summary"/>: "Parry: 5 dice, 3 x 6+ - ignored 2 Rending hits, 1 base hit"), the log line carries the
    /// same text, and the save beat that follows reads "(after Parry)" (<see cref="RollToSaveStage{TMetadata}"/>).</para>
    ///
    /// <para><b>Budget</b> (B1): one pool per enemy melee attack, X x the living carriers when the attack's first hits
    /// land, spent across the attacker's weapon profiles, each swing rolling min(remaining, its hits) - see
    /// <see cref="ParryBudget"/>. Carriers are counted by the rule graph, not by name: <see cref="Effect.ParryHits"/>
    /// names the living models whose melee weapons carry each attachment, so an army's embedded or renamed Parry is
    /// recognised by its effect. The dice are the defender's own roll - #418's fatigued-6 gate reads the attacker's
    /// hit roll and never touches them.</para>
    /// </summary>
    public class ParryHitsStage<TMetadata> : CombatStage<ParryResults, ParryHitsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        /// <summary>"For each 6+ one hit is ignored."</summary>
        public const int SUCCESS_FACE = 6;

        // Below this many hits (or dice) nothing is rolled or removed: the probabilistic roller leaves float dust.
        private const float EPSILON = 1e-4f;

        public ParryHitsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<ParryResults, Task> onFinished)
        {
            if (!metaData.IsMelee)
            {
                await onFinished(ParryResults.None);
                return;
            }

            RollToHitResults hits = QueryForResultOrThrowException<RollToHitResults>(metaData);
            float hitsTaken = hits.SuccessfulHitList.Sum(group => group.HitCount);
            if (hitsTaken <= EPSILON)
            {
                // "When this unit takes hits": a whiff is not that event, so the hook does not fire.
                await onFinished(ParryResults.None);
                return;
            }

            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();

            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator.EvaluateAllNamedLive(
                new MeleeHitsTakenContext(attacker, defender, hitsTaken),
                HitsTakenParticipants(attacker, defender));

            ParrySink sink = new ParrySink();
            sink.ApplyFrom(named.Select(n => n.Op));
            if (sink.TotalDice <= 0)
            {
                await onFinished(ParryResults.None);
                return;
            }

            // Shown as the army wrote it ("Parry"), without the authored value ("Parry(1)") - the X comes from the sink.
            string ruleName = ResolveImpactHitsStage.WithoutAuthoredValue(
                named.First(n => n.Op is RuleOperation.ParryDice).RuleName);
            if (string.IsNullOrWhiteSpace(ruleName)) ruleName = "Parry";
            string pool = DescribePool(sink, ruleName);
            string weaponName = metaData.WeaponType.Name;

            // B1: the pool is sized once, when the attack's first hits land; a swing-chain caller without a shared
            // budget (the Impact entry, a single-volley harness) gets a pool of its own.
            ParryBudget budget = metaData.ParryBudget ?? new ParryBudget();
            if (!budget.IsSet) budget.Set(sink.TotalDice);

            float dice = budget.Take(hitsTaken);
            if (dice <= EPSILON)
            {
                GameContext.Log($"{Possessive(defender.Name)} {ruleName} has no dice left this attack " +
                    $"({budget.Total} already rolled); {RollTags.Count(hitsTaken, "hit")} from " +
                    $"{Possessive(attacker.Name)} {weaponName} stand.");
                await onFinished(ParryResults.None);
                return;
            }

            IDiceResults rolled = GameContext.DiceRoller.Roll(dice);
            float sixes = rolled.AtOrAbove(SUCCESS_FACE);
            IReadOnlyList<ParriedGroup> ignored = RemoveWorstHitsFirst(hits.SuccessfulHitList, sixes);
            float hitsIgnored = ignored.Sum(g => g.Count);
            string summary = Summary(dice, sixes, ignored);
            ParryResults results = new ParryResults(dice, sixes, hitsIgnored, ignored, summary);

            List<string> chips = Chips(pool, hitsTaken, dice, budget);
            GameContext.Log($"{defender.Name} vs {Possessive(attacker.Name)} {weaponName} - {summary} " +
                $"({string.Join("; ", chips)}).");

            // The defender's roll, so Defense-coloured like its saves; the result line is the per-group text.
            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rolled, SUCCESS_FACE, GameContext.Settings.RandomnessType,
                    $"Parry vs {weaponName}",
                    summary,
                    category: ERollBeatCategory.Defense,
                    context: $"{defender.Name} parries {attacker.Name}",
                    modifierTags: chips));

            await onFinished(results);
        }

        /// <summary>
        /// The hits-taken evaluation's participants: the attacker as the Actor (so an attacker-side rule could one day
        /// suppress the defender's Parry) and the defender as a Subject with its living models and each melee weapon
        /// (<see cref="DetermineStrikeOrderStage.SubjectWithMeleeWeapons"/>, so weapon-scoped Parry fires). Shared with
        /// the AI's CombatMath and the Combat Calculator so their dice match the stage's.
        /// </summary>
        public static RuleParticipant[] HitsTakenParticipants(IUnit attacker, IUnit defender)
        {
            RuleParticipant[] defenderSide = DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender);
            var participants = new RuleParticipant[defenderSide.Length + 1];
            participants[0] = RuleParticipant.Actor(attacker);
            Array.Copy(defenderSide, 0, participants, 1, defenderSide.Length);
            return participants;
        }

        /// <summary>
        /// Removes <paramref name="sixes"/> hits from <paramref name="groups"/> (edited in place, identity and
        /// <see cref="HitGroupSource"/> kept), WORST group first: the lowest per-hit save modifier raises the defender's
        /// threshold the most (Rending's natural-6 group saves at AP+1 over the base group), so it goes first. Stable, so
        /// equal-threshold groups go in volley order - the rolled base group before Furious's extras and Blast's overflow.
        /// Returns what was removed, merged by group name, in removal order (worst first). Shared with the AI's CombatMath
        /// so the estimate removes exactly what the stage would.
        /// </summary>
        public static IReadOnlyList<ParriedGroup> RemoveWorstHitsFirst(List<SuccessfulHitInfo> groups, float sixes)
        {
            List<ParriedGroup> ignored = new List<ParriedGroup>();
            float remaining = sixes;
            if (remaining <= EPSILON) return ignored;

            List<int> worstFirst = Enumerable.Range(0, groups.Count).OrderBy(i => groups[i].SaveModifier).ToList();
            foreach (int index in worstFirst)
            {
                if (remaining <= EPSILON) break;
                SuccessfulHitInfo group = groups[index];
                float take = Math.Min(remaining, group.HitCount);
                if (take <= EPSILON) continue;

                groups[index] = new SuccessfulHitInfo(group.Rolls.WithoutTopRolls(take), group.SaveModifier, group.Source);
                remaining -= take;

                string name = GroupName(group.Source);
                int at = ignored.FindIndex(g => g.Name == name);
                if (at >= 0) ignored[at] = new ParriedGroup(name, ignored[at].Count + take);
                else ignored.Add(new ParriedGroup(name, take));
            }

            return ignored;
        }

        /// <summary>
        /// The result line (also the log text): "Parry: 5 dice, 3 x 6+ - ignored 2 Rending hits, 1 base hit",
        /// "Parry: 3 dice, one 6+ - ignored 1 Rending hit", "Parry: 3 dice, no 6+". Groups worst first, each named by
        /// the rule that made it worse ("Rending") or "base". ASCII only (game text). Internal for the tests.
        /// </summary>
        internal static string Summary(float dice, float sixes, IReadOnlyList<ParriedGroup> ignored)
        {
            string rolled = DiceWord(dice);
            if (sixes <= EPSILON || ignored.Count == 0) return $"Parry: {rolled}, no 6+";

            string removed = string.Join(", ", ignored.Select(g =>
                $"{g.Count:0.##} {g.Name} hit{(Math.Abs(g.Count - 1f) < EPSILON ? "" : "s")}"));
            return $"Parry: {rolled}, {SixesWord(sixes)} - ignored {removed}";
        }

        /// <summary>"Rending" for a per-hit-AP group (the rule that made it worse); "base" for every group saving at
        /// the weapon's own threshold (rolled hits, Furious extras, Blast overflow). Internal for the tests.</summary>
        internal static string GroupName(HitGroupSource source) =>
            source.Kind == EHitSourceKind.PerHitAp
                ? (string.IsNullOrEmpty(source.RuleName) ? "AP" : source.RuleName)
                : "base";

        /// <summary>"5 models x Parry(1)", or "5 models, 7 Parry dice" when the carriers' ratings differ. Internal for the tests.</summary>
        internal static string DescribePool(ParrySink sink, string ruleName)
        {
            string models = $"{sink.Carriers} model{(sink.Carriers == 1 ? "" : "s")}";
            return sink.UniformDicePerModel is int x
                ? $"{models} x {ruleName}({x})"
                : $"{models}, {sink.TotalDice} {ruleName} dice";
        }

        // The beat's chips: the pool, the cap, and - when this swing did not spend the whole pool in one go - how much of
        // the attack's budget is used so the reduced second-swing roll is attributable.
        private static List<string> Chips(string pool, float hitsTaken, float dice, ParryBudget budget)
        {
            List<string> chips = new List<string> { pool, $"{RollTags.Count(hitsTaken, "hit")} -> {DiceWord(dice)}" };
            if (Math.Abs(budget.Total - dice) > EPSILON)
            {
                chips.Add($"{budget.Spent:0.##} of {budget.Total} Parry dice used this attack");
            }
            return chips;
        }

        private static string DiceWord(float dice) => Math.Abs(dice - 1f) < EPSILON ? "1 die" : $"{dice:0.##} dice";

        private static string SixesWord(float sixes) => Math.Abs(sixes - 1f) < EPSILON ? "one 6+" : $"{sixes:0.##} x 6+";

        // "Warriors'" / "Ogre's" - the melee stages' possessive (#447's wording).
        private static string Possessive(string name) =>
            name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name + "'" : name + "'s";
    }
}
