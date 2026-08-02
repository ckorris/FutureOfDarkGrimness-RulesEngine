
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FDG.Presentation;
using FDG.Presentation.Beats;

namespace FDG.Stages
{
    public class RollToSaveStage<TMetadata> : CombatStage<RollToSaveResults, RollToSaveStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public RollToSaveStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<RollToSaveResults, Task> onFinished)
        {
            List<SuccessfulSaveInfo> successfulSaves = new List<SuccessfulSaveInfo>();
            List<FailedSaveInfo> failedSaves = new List<FailedSaveInfo>();

            float totalSuccesses = 0;
            float totalFailures = 0;

            DetermineSaveRollNeededResults saveRollsNeeded = QueryForResultOrThrowException<DetermineSaveRollNeededResults>(metaData);

            // #204: batch the beat presentation by save THRESHOLD, so hits saved with the same stats (the
            // base group plus Blast's multiplier hits plus Furious's on-6 extras) show as ONE roll, while
            // hits that save differently (Rending's per-hit AP) show as a separate, spaced roll - exactly how
            // it's rolled at the table. The dice ROLLING below is untouched (each group rolled separately,
            // same RNG draws, same per-group results), so this is purely presentational and outcome-neutral.
            List<SaveThresholdBucket> buckets = new List<SaveThresholdBucket>();

            foreach (PendingSaveRolls saveRolls in saveRollsNeeded.PendingSaveRollsList)
            {
                IDiceResults rollToSaveResults = GameContext.DiceRoller.Roll(saveRolls.HitCount);

                int saveNeeded = DiceUtilities.ClampSuccessRollNeeded(saveRolls.SaveNeeded);

                IDiceResults successfulResults = rollToSaveResults.SubsetAtOrAbove(saveNeeded);
                IDiceResults failedResults = rollToSaveResults.SubsetBelow(saveNeeded);

                successfulSaves.Add(new SuccessfulSaveInfo(successfulResults, saveRolls));
                failedSaves.Add(new FailedSaveInfo(failedResults, saveRolls));

                totalSuccesses += successfulResults.TotalRolls;
                totalFailures += failedResults.TotalRolls;

                // RollToHitStage emits one hit group per volley even when it whiffs (0 hits), so a missed
                // volley reaches here as a group with no save dice. Don't fold a hollow group into a beat.
                if (saveRolls.HitCount > 0)
                {
                    SaveThresholdBucket bucket = buckets.FirstOrDefault(b => b.SaveNeeded == saveNeeded);
                    if (bucket == null)
                    {
                        bucket = new SaveThresholdBucket(saveNeeded);
                        buckets.Add(bucket);
                    }
                    bucket.Add(rollToSaveResults, successfulResults.TotalRolls, failedResults.TotalRolls,
                        saveRolls.Source, saveRolls.HitCount);
                }
            }

            RollToSaveResults results = new RollToSaveResults(successfulSaves, failedSaves);

            GameContext.Log($"Saved {totalSuccesses} wounds, taking {totalFailures}.");

            // One save-roll beat per distinct threshold, in first-seen order, captioned with the weapon and
            // why there are extra hits ("2 hits x3 (Blast) = 6", "3 hits +1 (Furious) = 4", "2 hits, Rending
            // AP+1"). Non-held, like every other roll: each threshold gets the same full DiceRoll
            // envelope, so the exchange keeps its rhythm. The panels still overlap - #322's front-end
            // stack keeps each one up for seconds after its beat ends, so the to-hit roll is still on
            // screen (below this one) while the saves come in. See DiceRolledBeat.Held for the two
            // attempts at holding these and why the gap between rolls turned out to be load-bearing.
            // #245 glance metadata: category Defense, the defender's name as the "who", and the
            // attack-wide threshold arithmetic chips composed in DetermineSaveRollsNeededStage.
            string weaponName = metaData.WeaponType.Name;
            string defenderName = metaData.DefendingUnit.GetValue().Name;
            foreach (SaveThresholdBucket bucket in buckets)
            {
                await GameContext.Presenter.Present(
                    DiceRolledBeat.From(bucket.BuildResults(), bucket.SaveNeeded, GameContext.Settings.RandomnessType,
                        $"{weaponName}: {bucket.ComposeExplanation()}",
                        $"{bucket.Saved:0.##} saved, {bucket.Wounds:0.##} wounds",
                        category: ERollBeatCategory.Defense,
                        context: $"{defenderName} saves",
                        modifierTags: saveRollsNeeded.ThresholdTags));
            }

            // #232: no SaveBeat here anymore. The deflection pings + ping sound added noise without
            // information (the roll beats above already say "N saved, M wounds"), so saved hits get no
            // beat of their own; failed saves keep their wound/death presentation in ApplyWoundsStage.

            await onFinished(results);
        }

        /// <summary>
        /// #204: the presentation-only accumulator for one save THRESHOLD. Sums the dice, saved and wound
        /// counts across every hit group that saves at this threshold, and remembers each group's
        /// <see cref="HitGroupSource"/> so <see cref="ComposeExplanation"/> can narrate the arithmetic. Never
        /// touches the authoritative per-group results the save stage still records.
        /// </summary>
        private sealed class SaveThresholdBucket
        {
            public int SaveNeeded { get; }
            public float Saved { get; private set; }
            public float Wounds { get; private set; }

            private int _sideMin = 1;
            private float[] _faces = System.Array.Empty<float>();
            private readonly List<(HitGroupSource Source, float Count)> _sources = new List<(HitGroupSource, float)>();

            public SaveThresholdBucket(int saveNeeded) => SaveNeeded = saveNeeded;

            // hitCount is the number of HITS this group carried (== the group's save-dice count with the real
            // roller); passed explicitly rather than read off the roll so the arithmetic text is correct
            // regardless of the roller double a test injects.
            public void Add(IDiceResults roll, float saved, float wounds, HitGroupSource source, float hitCount)
            {
                if (_faces.Length == 0)
                {
                    _sideMin = roll.SideMin;
                    _faces = new float[roll.SideMax - roll.SideMin + 1];
                }
                for (int face = roll.SideMin; face <= roll.SideMax; face++)
                    _faces[face - _sideMin] += roll.At(face);

                Saved += saved;
                Wounds += wounds;
                _sources.Add((source, hitCount));
            }

            public IDiceResults BuildResults() => new DiceResults(_faces, _sideMin);

            public float TotalHits => _sources.Sum(s => s.Count);

            /// <summary>Renders "why there are this many hits" from the group sources that fed this threshold.</summary>
            public string ComposeExplanation()
            {
                // A Rending / per-hit-AP threshold: describe the raised save, not an arithmetic build-up.
                var perHitAp = _sources.Where(s => s.Source.Kind == EHitSourceKind.PerHitAp).ToList();
                if (perHitAp.Count > 0)
                {
                    string name = perHitAp.Select(s => s.Source.RuleName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";
                    float ap = perHitAp[0].Source.Amount;
                    string apTag = string.IsNullOrEmpty(name) ? $"AP+{ap:0.##}" : $"{name} AP+{ap:0.##}";
                    return $"{TotalHits:0.##} hits, {apTag}";
                }

                float baseHits = _sources.Where(s => s.Source.Kind == EHitSourceKind.BaseRolled).Sum(s => s.Count);
                var extras = _sources.Where(s => s.Source.Kind == EHitSourceKind.ExtraHits).ToList();
                var blast = _sources.FirstOrDefault(s => s.Source.Kind == EHitSourceKind.BlastMultiplier);
                bool hasBlast = blast.Source.Kind == EHitSourceKind.BlastMultiplier;

                // No modifiers - just a plain volley.
                if (extras.Count == 0 && !hasBlast)
                    return $"{baseHits:0.##} hits";

                // Extras only ("2 hits +1 (Furious) = 3").
                if (!hasBlast)
                {
                    StringBuilder sb = new StringBuilder($"{baseHits:0.##} hits");
                    foreach ((HitGroupSource src, float count) in extras)
                        sb.Append($" +{count:0.##} ({src.RuleName})");
                    return $"{sb} = {TotalHits:0.##}";
                }

                // Blast multiplies base (+ any on-6 extras). With extras the additive part is parenthesised so
                // the multiplier reads as applying to the sum ("(2 +1 Furious) x3 (Blast) = 9").
                string multiplicand;
                if (extras.Count == 0)
                {
                    multiplicand = $"{baseHits:0.##} hits";
                }
                else
                {
                    StringBuilder sb = new StringBuilder($"{baseHits:0.##}");
                    foreach ((HitGroupSource src, float count) in extras)
                        sb.Append($" +{count:0.##} {src.RuleName}");
                    multiplicand = $"({sb})";
                }
                return $"{multiplicand} x{blast.Source.Amount:0.##} ({blast.Source.RuleName}) = {TotalHits:0.##}";
            }
        }
    }
}
