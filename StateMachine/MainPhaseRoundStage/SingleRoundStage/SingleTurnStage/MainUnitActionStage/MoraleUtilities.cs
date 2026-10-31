using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Utilities;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Shared morale resolution used by every morale path — the melee loss (<see cref="RollForMoraleStage"/>
    /// → <see cref="AssignMeleeMoralePenaltyStage"/>) and the wound-driven half-strength path (shooting via
    /// <see cref="ResolveRangedMoraleStage"/>): the rule-aware morale test and the two
    /// failure outcomes — Shaken, or Rout when the unit is at half strength or less.
    /// </summary>
    public static class MoraleUtilities
    {
        // Fearless's second-chance die passes on a 4+ regardless of Quality (rulebook fixed threshold).
        private const int FEARLESS_REROLL_PASSES_ON = 4;

        /// <summary>#442: the Fearless re-roll's threshold, for estimators that mirror <see cref="TakeMoraleTest"/>.</summary>
        public const int FearlessRerollPassesOn = FEARLESS_REROLL_PASSES_ON;

        // On-screen banner colours for the two failed-morale outcomes: amber for Shaken (a warning, like
        // the Ambush-arrival / objective banners), red for a Rout (a death).
        private static readonly TextColor ShakenBannerColor = new TextColor(255, 170, 60, 255);
        private static readonly TextColor RoutBannerColor = new TextColor(220, 40, 40, 255);

        /// <summary>The result of a morale test: whether it passed, the (post-modifier, clamped) roll it
        /// needed, whether it only passed via a granted re-roll (Fearless), and whether it only passed
        /// because a rule CONVERTED the failure at a price (#197 P7 No Retreat) — the last three for
        /// logging.</summary>
        public readonly struct MoraleTestOutcome
        {
            public readonly bool Passed;
            public readonly int RollNeeded;
            public readonly bool PassedViaReroll;
            public readonly bool PassedViaConversion;

            public MoraleTestOutcome(bool passed, int rollNeeded, bool passedViaReroll,
                bool passedViaConversion = false)
            {
                PassedViaConversion = passedViaConversion;
                Passed = passed;
                RollNeeded = rollNeeded;
                PassedViaReroll = passedViaReroll;
            }
        }

        /// <summary>#421: where the Quality a morale test is taken at came from.</summary>
        public enum EMoraleQualitySource
        {
            /// <summary>The testing unit's own Quality.</summary>
            Unit = 0,
            /// <summary>The living, un-Shaken hero joined to the testing unit (#006).</summary>
            JoinedHero = 1,
            /// <summary>A friendly Tough(9)+ Hero within 6" of the testing unit (3.6).</summary>
            HeroAura = 2,
        }

        /// <summary>
        /// #421: the Quality a unit takes a Morale Test at and where it came from - the testing unit's own, its
        /// joined hero's, or a nearby Tough(9)+ Hero's (<see cref="ResolveMoraleQuality"/>). <see cref="HeroName"/>
        /// names the lending hero for the log line and the roll chip; null for <see cref="EMoraleQualitySource.Unit"/>.
        /// </summary>
        public readonly record struct MoraleQuality(int Quality, EMoraleQualitySource Source, string? HeroName = null)
        {
            /// <summary>A test at the unit's own (or a caller-chosen) Quality - no hero involved.</summary>
            public static MoraleQuality Own(int quality) => new(quality, EMoraleQualitySource.Unit);

            /// <summary>
            /// The threshold and its source as players read it - the morale roll's first chip and its log line:
            /// "Quality 5+", "Quality 3+ (joined Hero: Captain)", "Quality 3+ (Hero aura: Lord)". ASCII only.
            /// </summary>
            public string Describe() => Source switch
            {
                EMoraleQualitySource.JoinedHero => $"Quality {Quality}+ (joined Hero: {HeroName ?? "Hero"})",
                EMoraleQualitySource.HeroAura => $"Quality {Quality}+ (Hero aura: {HeroName ?? "Hero"})",
                _ => $"Quality {Quality}+",
            };
        }

        /// <summary>
        /// #421: THE seam every morale test reads its Quality from - the melee loser's (<c>DetermineMoraleSaveNeededStage</c>),
        /// the wound-driven one (<see cref="ResolveWoundDrivenMorale"/>), a spell's (<c>CastSpellStage</c>) and a
        /// rule's (<c>GameOperationServices.MoraleTestThen</c>: Mind Control, Fatigue Debuff, Spell Drain).
        /// <para>
        /// OPR 3.6 Hero: "The Hero's unit, and units within 6\" of friendly Heroes with Tough(9) or higher, may use
        /// the Hero's Quality for Morale Tests as long as the Hero isn't Shaken." "May use" is a benefit, so the
        /// engine takes the best Quality on offer with no prompt (#421 D2): the unit's own, its joined hero's
        /// (<see cref="HeroStatRules.GetMoraleQuality"/>: alive and not Shaken), and that of every friendly
        /// (team-aware) Tough(9)+ Hero that projects (<see cref="HeroStatRules.TryGetMoraleAuraHero"/>: alive, not
        /// Shaken, on the table) whose model is within <see cref="HeroStatRules.MoraleAuraRangeInches"/> of the
        /// testing unit's closest living model. Lower is better; a tie keeps the unit's own, then the joined hero,
        /// then the nearer aura hero, so a hero is only named when it actually changed the number.
        /// </para>
        /// <para>
        /// Measured when the test is taken, base to base in 3D (<see cref="UnitCompareUtilities.MinDistanceFromModelToUnit"/>,
        /// the same measure as the casting-support "friendly units within 12\"" scan and every shooting range gate;
        /// #421 D4). A testing unit that is not on the table is within 6" of nothing and gets no aura.
        /// </para>
        /// </summary>
        public static MoraleQuality ResolveMoraleQuality(IGameContext gameContext, IUnit testingUnit)
            => ResolveMoraleQuality(gameContext.TableState, testingUnit);

        /// <summary>
        /// The same resolution from the table state alone - the seam a read-only estimator (the Tactician's morale
        /// pass chance, #442) prices through, so it reads the very Quality the test will be taken at.
        /// </summary>
        public static MoraleQuality ResolveMoraleQuality(ITableState tableState, IUnit testingUnit)
        {
            MoraleQuality best = MoraleQuality.Own(testingUnit.Quality);

            if (testingUnit is UnitData { HeroAttachment: { } attachment } host)
            {
                int joined = HeroStatRules.GetMoraleQuality(host);
                if (joined < best.Quality)
                {
                    best = new MoraleQuality(joined, EMoraleQualitySource.JoinedHero, attachment.Name ?? "Hero");
                }
            }

            if (BestAuraQuality(tableState, testingUnit) is { } aura && aura.Quality < best.Quality)
            {
                best = aura;
            }

            return best;
        }

        // The best Quality a friendly Tough(9)+ Hero within 6" lends the testing unit, or null when none does.
        private static MoraleQuality? BestAuraQuality(ITableState tableState, IUnit testingUnit)
        {
            if (!testingUnit.GetIsOnBattlefield())
            {
                return null;
            }

            MoraleQuality? best = null;
            float bestDistance = float.PositiveInfinity;
            List<ITeam> teams = tableState.Teams.Objects.ToList();
            foreach (IUnit other in tableState.Units.Objects)
            {
                // The testing unit's own Hero is "the Hero's unit" - its own Quality, already the baseline.
                if (ReferenceEquals(other, testingUnit) || other.ID.Equals(testingUnit.ID)) continue;
                if (!ITeamExtensions.AreAllied(teams, testingUnit.PlayerID, other.PlayerID)) continue;
                if (!HeroStatRules.TryGetMoraleAuraHero(other, out HeroStatRules.MoraleAuraHero hero)) continue;

                float distance = UnitCompareUtilities.MinDistanceFromModelToUnit(hero.Model, testingUnit,
                    includeVertical: true);
                if (distance > HeroStatRules.MoraleAuraRangeInches) continue;

                if (best == null || hero.Quality < best.Value.Quality
                    || (hero.Quality == best.Value.Quality && distance < bestDistance))
                {
                    best = new MoraleQuality(hero.Quality, EMoraleQualitySource.HeroAura, hero.Name);
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Take a morale test for <paramref name="testingUnit"/> against <paramref name="baseRollNeeded"/>
        /// (its Quality), applying morale special rules. Fires <see cref="EHookID.Morale_OnPreMoraleTest"/>
        /// and folds any <c>ApplyRollModifier(Morale)</c> into the threshold (a +N makes the test easier, so
        /// it lowers the roll needed; clamped so a 1 always fails and a 6 always passes). The test is a
        /// single decisive die (#090). On a failure it fires <see cref="EHookID.Morale_OnMoraleTestComplete"/>
        /// and, if a morale re-roll is granted (Fearless), rolls one more decisive die that passes on a 4+
        /// regardless of Quality.
        /// <para>
        /// #432: <paramref name="shotFromInches"/> / <paramref name="chargedFromInches"/> describe the attack that
        /// triggered the test - a shooting attack's distance, or the charge the testing unit received - and ride the
        /// pre-test context (<see cref="PreMoraleTestContext"/>) so OPR 3.6's "+1 to Morale Test rolls when
        /// shot/charged from over 9\"/6\" away" rules can read them. Null (the default) for tests nothing like that
        /// triggered (spells, Mind Control, a charger losing its own melee).
        /// </para>
        /// <para>
        /// #435: <paramref name="cause"/> says what triggered the test - shooting (the wound-driven test), melee (the
        /// loser's test) or anything else (a spell's or a rule's demand, the default) - and rides the same context for
        /// OPR 3.6 AoF Banner's "+1 to Morale Test rolls triggered by shooting/melee".
        /// </para>
        /// </summary>
        public static Task<MoraleTestOutcome> TakeMoraleTest(IGameContext gameContext, IUnit testingUnit,
            int baseRollNeeded, bool failureCausesShakenOrRout = false, float? shotFromInches = null,
            float? chargedFromInches = null, EMoraleTestCause cause = EMoraleTestCause.Other)
            => TakeMoraleTest(gameContext, testingUnit, MoraleQuality.Own(baseRollNeeded), failureCausesShakenOrRout,
                shotFromInches, chargedFromInches, cause);

        /// <summary>
        /// #421: <see cref="TakeMoraleTest(IGameContext, IUnit, int, bool, float?, float?, EMoraleTestCause)"/> at a
        /// Quality the caller resolved through <see cref="ResolveMoraleQuality"/>, which also says where it came from.
        /// </summary>
        public static async Task<MoraleTestOutcome> TakeMoraleTest(IGameContext gameContext, IUnit testingUnit,
            MoraleQuality quality, bool failureCausesShakenOrRout = false, float? shotFromInches = null,
            float? chargedFromInches = null, EMoraleTestCause cause = EMoraleTestCause.Other)
        {
            int baseRollNeeded = quality.Quality;

            // A Shaken unit always fails its morale tests (GF Core Rules v3.6.0, as in v3.5.1) — no die is rolled. This is what
            // escalates a Shaken unit that loses a later melee at half strength into a Rout, and keeps an
            // already-Shaken unit Shaken on any other failed test. Checked before the roll so a lucky die
            // can't rescue it.
            //
            // #197 P7: a conversion rule (No Retreat) is offered even here, BEFORE the short-circuit
            // returns. This automatic failure is precisely the one that Routs, and "Routed" is named in
            // the rule's own text, so gating it behind a die the unit never rolls would drop the half of
            // the rule that matters most. Owner-ruled 2026-07-29. A unit without such a rule still
            // auto-fails exactly as before — TryConvertFailure returns false and nothing else changes.
            if (testingUnit.Tokens.HasToken(TokenType.Shaken))
            {
                if (await TryConvertFailure(gameContext, testingUnit, failureCausesShakenOrRout))
                {
                    return new MoraleTestOutcome(passed: true, baseRollNeeded, passedViaReroll: false,
                        passedViaConversion: true);
                }
                return new MoraleTestOutcome(passed: false, baseRollNeeded, passedViaReroll: false);
            }

            // #421: a Hero lent the Quality - say whose, so the number in the log is never a mystery. (The unit's
            // own Quality needs no line; the existing pass/fail lines carry the threshold.)
            if (quality.Source != EMoraleQualitySource.Unit)
            {
                gameContext.Log($"{testingUnit.Name} tests morale at {quality.Describe()}.");
            }

            // #245: the NAMED live evaluation - identical to EvaluateAll (logs, spends grants), but each
            // op carries its rule name so the beat's chips can say which rule shifted the test.
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = gameContext.RuleEvaluator.EvaluateAllNamedLive(
                new PreMoraleTestContext(testingUnit, shotFromInches, chargedFromInches, cause),
                RuleParticipant.Actor(testingUnit));
            RollModifierSink modifiers = new RollModifierSink();
            modifiers.ApplyFrom(named.Select(n => n.Op).ToList());
            // #033 granted morale modifiers (e.g. a spell's "-1 to morale" debuff) fold in with the same
            // sign; one-shot ("next time") grants are consumed by this test.
            int grantedNet = GrantedRollModifiers.ConsumeNet(testingUnit, ERollKind.Morale);
            int rollNeeded = DiceUtilities.ClampSuccessRollNeeded(baseRollNeeded - modifiers.Net(ERollKind.Morale)
                - grantedNet);

            // Decisive single die — one concrete face even under the probabilistic roller (#090).
            IDiceResults initialRoll = gameContext.DiceRoller.RollDecisive();
            bool passedInitial = initialRoll.AtOrAbove(rollNeeded) >= 1f;
            // Name the unit under test: a shoot action can now roll morale for several defenders in a row,
            // so an anonymous "Morale Test" banner leaves the player guessing which one is being tested.
            // #289: FromDecisive, not the game's randomness setting - the die above is a real, concrete
            // face in either roller mode, so the front-end must draw a die rather than a probability bar.
            await gameContext.Presenter.Present(DiceRolledBeat.FromDecisive(initialRoll, rollNeeded,
                $"{testingUnit.Name} - Morale Test",
                passedInitial ? "Passed" : "Failed",
                modifierTags: ComposeMoraleTags(quality, named, grantedNet)));
            if (passedInitial)
            {
                return new MoraleTestOutcome(passed: true, rollNeeded, passedViaReroll: false);
            }

            // Failed — offer morale rules a second chance (Fearless: a fresh decisive die, 4+ passes).
            //
            // #183 shape: the participant carries the unit's LIVING MODELS, so a rule held on a model
            // rather than the unit (a joined hero's, or a per-model champion upgrade) is visible here at
            // all. Without it MostModelsHaveThisRule could only ever see unit-level attachments, making
            // its majority arithmetic dead code. Fearless rides the same gate since #428 (3.6: "a unit
            // where most models have this rule"), so a joined hero without it no longer breaks it.
            IReadOnlyList<RuleOperation> completeOps = gameContext.RuleEvaluator.EvaluateAll(
                new MoraleTestContext(testingUnit),
                RuleParticipant.Actor(testingUnit, models: HeroStatRules.LivingModels(testingUnit)));
            RerollSink rerollSink = new RerollSink();
            rerollSink.ApplyFrom(completeOps);
            if (rerollSink.RerollMoraleOnFailure)
            {
                IDiceResults reroll = gameContext.DiceRoller.RollDecisive();
                bool passedReroll = reroll.AtOrAbove(FEARLESS_REROLL_PASSES_ON) >= 1f;
                await gameContext.Presenter.Present(DiceRolledBeat.FromDecisive(reroll, FEARLESS_REROLL_PASSES_ON,
                    $"{testingUnit.Name} - Fearless Re-roll",
                    passedReroll ? "Passed" : "Failed"));
                if (passedReroll)
                {
                    return new MoraleTestOutcome(passed: true, rollNeeded, passedViaReroll: true);
                }
            }

            // #197 P7: last, after every free rescue has been tried. A Fearless re-roll costs nothing, so
            // it must go first — converting here when the re-roll would have passed anyway would charge the
            // unit a wound pool it never owed. `completeOps` is reused rather than re-evaluated: a second
            // pass would double-spend any one-shot grant the first one consumed.
            if (await TryConvertFailure(gameContext, testingUnit, failureCausesShakenOrRout, completeOps))
            {
                return new MoraleTestOutcome(passed: true, rollNeeded, passedViaReroll: false,
                    passedViaConversion: true);
            }

            return new MoraleTestOutcome(passed: false, rollNeeded, passedViaReroll: false);
        }

        /// <summary>
        /// #197 P7: offer any <see cref="RuleOperation.PassMoraleTest"/> the unit holds, and if one fires,
        /// charge its price — a pool of one die per wound still needed to destroy the unit, each result at
        /// or below the op's threshold dealing one unignorable wound. Returns whether the failure was
        /// converted.
        /// <para>
        /// <paramref name="failureCausesShakenOrRout"/> gates the whole thing: the corpus wording is "fails
        /// a morale test that causes it to be Shaken or Routed", and two of this file's callers (Mind
        /// Control / Fatigue Debuff, and a spell's own morale test) have other consequences entirely, which
        /// the rule does not cover.
        /// </para>
        /// <paramref name="alreadyEvaluated"/> lets the post-re-roll caller pass the queue it already has,
        /// so the hook is evaluated exactly once per test and one-shot grants are spent once.
        /// </summary>
        private static async Task<bool> TryConvertFailure(IGameContext gameContext, IUnit testingUnit,
            bool failureCausesShakenOrRout, IReadOnlyList<RuleOperation>? alreadyEvaluated = null)
        {
            if (!failureCausesShakenOrRout) return false;

            IReadOnlyList<RuleOperation> operations = alreadyEvaluated
                ?? gameContext.RuleEvaluator.EvaluateAll(
                    new MoraleTestContext(testingUnit),
                    RuleParticipant.Actor(testingUnit, models: HeroStatRules.LivingModels(testingUnit)));

            RuleOperation.PassMoraleTest? conversion =
                operations.OfType<RuleOperation.PassMoraleTest>().FirstOrDefault();
            if (conversion == null) return false;

            await gameContext.Presenter.Present(new BannerBeat(
                $"{testingUnit.Name} will not retreat - the test counts as passed!",
                ShakenBannerColor, EBannerTier.Notice));

            await RollSelfWoundPool(gameContext, testingUnit, conversion.SelfWoundOnRollAtMost);
            return true;
        }

        /// <summary>
        /// "Roll as many dice as the number of wounds it would take to fully destroy it, and for each
        /// result of 1-X the unit takes one wound, which can't be ignored." One batched roll, so the
        /// probabilistic roller yields the expected number of low faces rather than N decisive dice.
        /// <para>
        /// The pool is <see cref="IUnitExtensions.GetWoundsToDestroy"/> (#420) - the same count the 3.6
        /// Dangerous terrain test rolls, floored from the unit's remaining wounds (the P17d Reanimation
        /// precedent: a fractional tail under the probabilistic roller cannot buy a whole die, and dice
        /// counts stay integral without int-locking a roll-derived value). The wounds themselves stay
        /// fractional.
        /// </para>
        /// </summary>
        private static async Task RollSelfWoundPool(IGameContext gameContext, IUnit unit, int maxRoll)
        {
            int pool = unit.GetWoundsToDestroy();
            if (pool <= 0) return;

            IDiceResults roll = gameContext.DiceRoller.Roll(6, pool);
            float wounds = 0f;
            for (int face = 1; face <= maxRoll; face++) wounds += roll.At(face);

            gameContext.Log($"{unit.Name}: No Retreat rolled {pool} dice, " +
                $"{wounds:0.##} at {maxRoll} or less - that many unignorable wounds.");
            await gameContext.Presenter.Present(DiceRolledBeat.From(roll, successThreshold: maxRoll + 1,
                gameContext.Settings.RandomnessType, "No Retreat",
                wounds > 0f ? $"{wounds:0.##} wound{(wounds == 1f ? "" : "s")}" : "Unscathed"));

            // The spread, the casualty animations and the destruction seam are the shared unit-owes-a-pool
            // path (#197 Hazardous rides the same one).
            await CasualtyPresentation.ApplyUnitWounds(gameContext, unit, wounds);
        }

        // #245: the morale beat's modifier chips - base quality plus each named morale modifier and any
        // granted debuff/buff. Null (no chips, no extended beat) when the test is unmodified.
        // Internal for tests.
        internal static List<string>? ComposeMoraleTags(int baseRollNeeded,
            IReadOnlyList<(RuleOperation Op, string RuleName)> named, int grantedNet)
            => ComposeMoraleTags(MoraleQuality.Own(baseRollNeeded), named, grantedNet);

        // #421: the first chip names a lending Hero ("Quality 3+ (Hero aura: Lord)"), and a Hero-sourced test always
        // shows its chips - the borrowed Quality IS the modifier worth reading, even with nothing else on the roll.
        internal static List<string>? ComposeMoraleTags(MoraleQuality quality,
            IReadOnlyList<(RuleOperation Op, string RuleName)> named, int grantedNet)
        {
            List<string> tags = new List<string> { quality.Describe() };
            foreach ((RuleOperation op, string ruleName) in named)
            {
                if (op is not RuleOperation.ApplyRollModifier mod || mod.Roll != ERollKind.Morale || mod.Delta == 0)
                    continue;
                tags.Add($"{RollTags.NameOr(ruleName, "modifier")} {RollTags.Delta(mod.Delta)}");
            }
            if (grantedNet != 0) tags.Add($"buff {RollTags.Delta(grantedNet)}");

            return tags.Count > 1 || quality.Source != EMoraleQualitySource.Unit ? tags : null;
        }

        /// <summary>
        /// Whether a wound-driven morale test is due: the unit took wounds (its remaining wounds dropped
        /// below <paramref name="remainingWoundsBefore"/>, the baseline captured when the action began)
        /// and it now sits at half strength or less. The rule tests at the end of ANY activation whose
        /// wounds leave the unit at half or less (GF Core Rules v3.6.0, as in v3.5.1) — including a unit that was already below
        /// half before this activation's wounds (#254; an earlier crossing-only check skipped those).
        /// <para>
        /// #431: <paramref name="countedWounds"/> are wounds the unit only COUNTS as having taken (OPR 3.6
        /// Scourge, "... if shooting wounds would trigger Morale Tests"; <see cref="Rules.Dispatch.CountedWounds"/>).
        /// They count as wounds taken, so they alone can make the test due, and they are allocated virtually for
        /// the half-strength check (<see cref="IUnitExtensions.GetIsAtHalfStrength(IUnit, float)"/>).
        /// </para>
        /// </summary>
        public static bool WoundsLeftUnitAtHalfStrength(float remainingWoundsBefore, IUnit unitAfter,
            float countedWounds = 0f)
        {
            bool tookWounds = unitAfter.RemainingWounds < remainingWoundsBefore || countedWounds > 0f;
            return tookWounds && unitAfter.GetIsAtHalfStrength(countedWounds);
        }

        /// <summary>
        /// #413: why <see cref="ResolveWoundDrivenMorale"/> took no test, as a log line - or null when
        /// nothing is worth saying (a unit destroyed outright already has its death logged). Without it a
        /// skipped test left only "Resolving ranged morale." in the log, which read as a missed roll.
        /// Example: a 3-model Tough(3) unit shot to 4/9 wounds with 2 models standing logs
        /// "Hive Guardians took wounds but is above half strength (2/3 models) - no morale test."
        /// </summary>
        public static string? DescribeSkippedWoundDrivenMorale(float remainingWoundsBefore, IUnit unitAfter,
            float countedWounds = 0f)
        {
            if (unitAfter.GetIsAlive() == false) return null;
            if ((unitAfter.RemainingWounds < remainingWoundsBefore || countedWounds > 0f) == false)
                return $"{unitAfter.Name} took no wounds - no morale test.";
            if (unitAfter.GetIsAtHalfStrength(countedWounds) == false)
                return $"{unitAfter.Name} took wounds but is above half strength ({DescribeStrength(unitAfter)}" +
                    (countedWounds > 0f ? $", counting {countedWounds:0.##} extra" : "") + ") - no morale test.";
            return null;
        }

        // Mirrors GetIsAtHalfStrength's shape split: models for a multi-model unit, wounds for a lone model.
        private static string DescribeStrength(IUnit unit)
        {
            if (unit.Models.Count == 1)
                return $"{unit.RemainingWounds:0.##}/{unit.MaxWounds:0.##} wounds";

            int livingModels = unit.Models.Count(model => model.GetIsAlive());
            return $"{livingModels}/{unit.Models.Count} models";
        }

        /// <summary>
        /// Resolve a wound-driven morale test (shooting): if the unit took wounds and is now at half
        /// strength or less, it tests, and a failure makes it Shaken. A failed non-melee
        /// morale test never Routs — Rout is a melee-only result (GF Core Rules v3.6.0, as in v3.5.1: the general morale rule says
        /// a failed test makes a unit Shaken; only the melee-results rule routs a loser at half strength).
        /// Returns null if no test was taken (above half, unwounded, or destroyed outright), true if
        /// passed, false if failed and Shaken. Logging is left to the caller, which knows the wound source.
        /// <paramref name="countedWounds"/>: #431 Scourge's counted wounds - see
        /// <see cref="WoundsLeftUnitAtHalfStrength"/>; the caller takes them (<c>CountedWounds.Take</c>).
        /// <paramref name="shotFromInches"/>: #432, the shooting distance of the attack that wounded the unit, for
        /// the "shot from over 9\"" morale rules (see <see cref="TakeMoraleTest"/>); null when not known.
        /// </summary>
        public static async Task<bool?> ResolveWoundDrivenMorale(IGameContext gameContext, DataBinding<UnitData> unitBinding,
            float remainingWoundsBefore, float countedWounds = 0f, float? shotFromInches = null)
        {
            IUnit unit = unitBinding.GetValue();
            if (!unit.GetIsAlive()) return null;                              // wiped out outright — no test
            if (!WoundsLeftUnitAtHalfStrength(remainingWoundsBefore, unit, countedWounds)) return null;

            // #421: the best Quality on offer - the unit's own, its joined hero's, or a Tough(9)+ Hero's within 6".
            // failureCausesShakenOrRout: this test's failure Shakens the unit, so a conversion rule
            // (#197 P7 No Retreat) may rescue it.
            // #435: a wound-driven test is the one shooting triggers (a strafe / Crossing Strike attacks "as if it
            // was shooting" and lands here too) - OPR 3.6 Banner's "triggered by shooting/melee".
            if ((await TakeMoraleTest(gameContext, unit, ResolveMoraleQuality(gameContext, unit),
                    failureCausesShakenOrRout: true, shotFromInches: shotFromInches,
                    cause: EMoraleTestCause.Shooting)).Passed) return true;

            await ApplyShakenWithPresentation(gameContext, unitBinding);
            return false;
        }

        /// <summary>
        /// Mark a unit Shaken as the consequence of a failed morale test, and announce it on-screen with
        /// a <see cref="BannerBeat"/>. Used by every failed-morale path except a melee loser at half
        /// strength (which Routs instead — see <see cref="RoutWithPresentation"/>). Idempotent on the
        /// token; the banner still fires so a repeat failure is visible.
        /// </summary>
        public static async Task ApplyShakenWithPresentation(IGameContext gameContext, DataBinding<UnitData> unitBinding)
        {
            ApplyShaken(unitBinding);
            await gameContext.Presenter.Present(
                new BannerBeat($"{unitBinding.GetValue().Name} fails morale - Shaken!", ShakenBannerColor,
                    EBannerTier.Notice));

            // #197 P17c Reinforcement: "when a unit where all models have this rule is Shaken ... you may
            // remove it from the table as destroyed and place a new copy ..." - the offer rides the
            // Shaken moment itself.
            await OfferShakenTriggeredRules(gameContext, unitBinding.GetValue());
        }

        /// <summary>
        /// #197 P17c: evaluates the newly-Shaken unit at <see cref="EHookID.Morale_OnShakenApplied"/> and
        /// offers any <see cref="RuleOperation.InvokeReinforce"/> it produces as one Yes/No (default yes -
        /// the EOF/AI fallback trades a Shaken unit for a fresh copy). Token operations apply
        /// unconditionally, mirroring the destruction seam's shape.
        ///
        /// Public because the failed-morale path above is not the only way a unit becomes Shaken: a
        /// destroyed transport's cargo is Shaken by <see cref="SpilloutExecutor"/>, which calls this
        /// directly (its own Shaken application lives in the Rules layer, which cannot reach a stage).
        /// </summary>
        public static async Task OfferShakenTriggeredRules(IGameContext gameContext, IUnit unit)
        {
            IReadOnlyList<RuleOperation> ops = gameContext.RuleEvaluator.EvaluateAll(
                new ShakenAppliedContext(unit),
                RuleParticipant.Actor(unit, models: unit.Models));
            if (ops.Count == 0)
            {
                return;
            }

            OperationApplier.ApplyTokenOperations(ops);

            if (ops.OfType<RuleOperation.InvokeReinforce>().Any())
            {
                bool accepted = await gameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(new YesNoRequest(unit.PlayerID,
                        $"{unit.Name} is Shaken - remove it as destroyed and reinforce next round?",
                        defaultAnswer: true, displayName: $"Deciding Whether to Reinforce {unit.Name}"));
                if (!accepted)
                {
                    return;
                }
            }

            await OperationExecutor.Execute(ops, new GameOperationServices(gameContext));
        }

        /// <summary>
        /// Mark a unit Shaken. The token clears manually when the unit spends an activation idle to
        /// recover (#008); applied idempotently.
        /// </summary>
        public static void ApplyShaken(DataBinding<UnitData> unitBinding)
        {
            IUnit unit = unitBinding.GetValue();
            if (unit.Tokens.HasToken(TokenType.Shaken)) return;

            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            ClearShakenTriggeredTokens(unit);
        }

        /// <summary>
        /// #100 #13: tokens carried with a <c>CustomHook(Morale_OnShakenApplied)</c> clear trigger die
        /// the moment their bearer becomes Shaken (Fortified Growth's "if this unit is ever Shaken, it
        /// loses all its markers"). Called from both Shaken-application sites (here and the transport
        /// spillout); the idempotence guard above means it fires once per became-Shaken transition.
        /// </summary>
        public static void ClearShakenTriggeredTokens(IUnit unit)
        {
            List<ITokenContainer> containers = new List<ITokenContainer> { unit.Tokens };
            containers.AddRange(unit.Models.Select(model => model.Tokens));
            new TokenClearService().ClearForHook(EHookID.Morale_OnShakenApplied, containers);
        }

        /// <summary>
        /// Remove a unit from play by dealing lethal wounds to all its living models. The engine has no
        /// whole-unit removal primitive; an all-models-dead unit is already filtered out of activation,
        /// turn order, and objective scoring everywhere via <c>GetIsAlive</c>. Returns the models that
        /// were killed (those that had been alive) so a caller on the presentation path can animate them.
        /// </summary>
        public static IReadOnlyList<IModel> Rout(DataBinding<UnitData> unitBinding)
        {
            List<IModel> killed = new List<IModel>();
            foreach (IModel model in unitBinding.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;

                float remaining = model.TotalWounds - model.WoundsDealt;
                if (remaining > 0f)
                {
                    model.DealWounds(remaining);
                    killed.Add(model);
                }
            }
            return killed;
        }

        /// <summary>
        /// <see cref="Rout"/> plus presentation: announces the rout on-screen with a <see cref="BannerBeat"/>
        /// while the models are still standing, then deals the lethal wounds and emits one
        /// <see cref="UnitRoutedBeat"/> so the front-end plays every routed model's death animation at
        /// once (rather than the one-at-a-time sequence individual death beats would produce). Returns
        /// early (no banner, no beat) if the unit has nothing alive to rout.
        /// </summary>
        public static async Task RoutWithPresentation(IGameContext gameContext, DataBinding<UnitData> unitBinding)
        {
            UnitData unit = unitBinding.GetValue();
            if (!unit.GetIsAlive()) return;   // nothing to rout — don't announce a phantom rout

            // Banner first, before Rout deals the lethal wounds, so the announcement starts over the
            // still-living unit rather than firing on empty bases. As a Notice (#275) it paces only its
            // lead-in, so the deaths below begin while the words are still up -- the announcement and
            // the thing it announces now play together instead of one after the other.
            await gameContext.Presenter.Present(
                new BannerBeat($"{unit.Name} fails morale - Routed!", RoutBannerColor, EBannerTier.Notice));

            IReadOnlyList<IModel> killed = Rout(unitBinding);
            if (killed.Count == 0) return;

            List<RoutedModel> deaths = new List<RoutedModel>(killed.Count);
            foreach (IModel model in killed)
                deaths.Add(new RoutedModel(model.ID, model.Position));

            await gameContext.Presenter.Present(new UnitRoutedBeat(unit.ID, unit.Name, deaths));

            // A routed unit is destroyed: its cross-unit OwnerDestroyed marks clear. No killer is passed —
            // whether a rout counts as "destroyed by" the melee winner is an open rules question, so the
            // unit-destroyed hook deliberately does not fire here (see UnitDestructionNotifier).
            await UnitDestructionNotifier.NotifyUnitDestroyed(gameContext, unit, killer: null);
        }
    }
}
