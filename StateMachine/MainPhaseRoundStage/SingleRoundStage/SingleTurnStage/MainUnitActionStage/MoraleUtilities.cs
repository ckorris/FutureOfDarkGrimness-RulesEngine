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

        /// <summary>
        /// Take a morale test for <paramref name="testingUnit"/> against <paramref name="baseRollNeeded"/>
        /// (its Quality), applying morale special rules. Fires <see cref="EHookID.Morale_OnPreMoraleTest"/>
        /// and folds any <c>ApplyRollModifier(Morale)</c> into the threshold (a +N makes the test easier, so
        /// it lowers the roll needed; clamped so a 1 always fails and a 6 always passes). The test is a
        /// single decisive die (#090). On a failure it fires <see cref="EHookID.Morale_OnMoraleTestComplete"/>
        /// and, if a morale re-roll is granted (Fearless), rolls one more decisive die that passes on a 4+
        /// regardless of Quality.
        /// </summary>
        public static async Task<MoraleTestOutcome> TakeMoraleTest(IGameContext gameContext, IUnit testingUnit,
            int baseRollNeeded, bool failureCausesShakenOrRout = false)
        {
            // A Shaken unit always fails its morale tests (GF v3.5.1) — no die is rolled. This is what
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

            // #245: the NAMED live evaluation - identical to EvaluateAll (logs, spends grants), but each
            // op carries its rule name so the beat's chips can say which rule shifted the test.
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = gameContext.RuleEvaluator.EvaluateAllNamedLive(
                new PreMoraleTestContext(testingUnit), RuleParticipant.Actor(testingUnit));
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
                modifierTags: ComposeMoraleTags(baseRollNeeded, named, grantedNet)));
            if (passedInitial)
            {
                return new MoraleTestOutcome(passed: true, rollNeeded, passedViaReroll: false);
            }

            // Failed — offer morale rules a second chance (Fearless: a fresh decisive die, 4+ passes).
            //
            // #183 shape: the participant carries the unit's LIVING MODELS, so a rule held on a model
            // rather than the unit (a joined hero's, or a per-model champion upgrade) is visible here at
            // all. Without it MostModelsHaveThisRule could only ever see unit-level attachments, making
            // its majority arithmetic dead code. Fearless is unaffected - it gates on
            // AllModelsHaveThisRule, which a lone hero still fails.
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
        /// The pool is FLOORED from the unit's remaining wounds, the P17d Reanimation precedent: a
        /// fractional tail under the probabilistic roller cannot buy a whole die, and dice counts stay
        /// integral without int-locking a roll-derived value. The wounds themselves stay fractional.
        /// </para>
        /// </summary>
        private static async Task RollSelfWoundPool(IGameContext gameContext, IUnit unit, int maxRoll)
        {
            int pool = (int)unit.RemainingWounds;
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
        {
            List<string> tags = new List<string> { $"Quality {baseRollNeeded}+" };
            foreach ((RuleOperation op, string ruleName) in named)
            {
                if (op is not RuleOperation.ApplyRollModifier mod || mod.Roll != ERollKind.Morale || mod.Delta == 0)
                    continue;
                tags.Add($"{RollTags.NameOr(ruleName, "modifier")} {RollTags.Delta(mod.Delta)}");
            }
            if (grantedNet != 0) tags.Add($"buff {RollTags.Delta(grantedNet)}");

            return tags.Count > 1 ? tags : null;
        }

        /// <summary>
        /// Whether a wound-driven morale test is due: the unit took wounds (its remaining wounds dropped
        /// below <paramref name="remainingWoundsBefore"/>, the baseline captured when the action began)
        /// and it now sits at half strength or less. The rule tests at the end of ANY activation whose
        /// wounds leave the unit at half or less (GF v3.5.1) — including a unit that was already below
        /// half before this activation's wounds (#254; an earlier crossing-only check skipped those).
        /// </summary>
        public static bool WoundsLeftUnitAtHalfStrength(float remainingWoundsBefore, IUnit unitAfter)
        {
            bool tookWounds = unitAfter.RemainingWounds < remainingWoundsBefore;
            return tookWounds && unitAfter.GetIsAtHalfStrength();
        }

        /// <summary>
        /// Resolve a wound-driven morale test (shooting): if the unit took wounds and is now at half
        /// strength or less, it tests, and a failure makes it Shaken. A failed non-melee
        /// morale test never Routs — Rout is a melee-only result (GF v3.5.1: the general morale rule says
        /// a failed test makes a unit Shaken; only the melee-results rule routs a loser at half strength).
        /// Returns null if no test was taken (above half, unwounded, or destroyed outright), true if
        /// passed, false if failed and Shaken. Logging is left to the caller, which knows the wound source.
        /// </summary>
        public static async Task<bool?> ResolveWoundDrivenMorale(IGameContext gameContext, DataBinding<UnitData> unitBinding,
            float remainingWoundsBefore)
        {
            IUnit unit = unitBinding.GetValue();
            if (!unit.GetIsAlive()) return null;                              // wiped out outright — no test
            if (!WoundsLeftUnitAtHalfStrength(remainingWoundsBefore, unit)) return null;

            // #006: a living joined hero takes morale on behalf of the unit (its Quality).
            // failureCausesShakenOrRout: this test's failure Shakens the unit, so a conversion rule
            // (#197 P7 No Retreat) may rescue it.
            if ((await TakeMoraleTest(gameContext, unit, HeroStatRules.GetMoraleQuality(unitBinding.GetValue()),
                    failureCausesShakenOrRout: true)).Passed) return true;

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
                        defaultAnswer: true));
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
