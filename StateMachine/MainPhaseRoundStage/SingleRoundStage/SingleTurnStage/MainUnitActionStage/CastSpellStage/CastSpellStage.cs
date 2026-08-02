using System.Collections.Generic;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #033 — the Caster "Cast" action. Reached from <see cref="ChooseActionStage"/> when the activating
    /// unit carries Caster(X) and its army has an affordable spell. The player picks a spell from the army's
    /// list, picks target(s) within the spell's range / line of sight / affinity, spends the spell's token
    /// cost (paid on the *attempt*, whether or not it succeeds), and rolls one die — on a 4+ the spell is
    /// cast and its effect applied. Then it loops back to Choose Action via <see cref="OnFinished"/>,
    /// **layered** like a custom action (it never sets HasMoved/HasAttacked), so the unit may still
    /// Move/Shoot and may cast again while it can afford another spell.
    ///
    /// Three effect archetypes:
    /// <list type="bullet">
    ///   <item><b>Non-damage</b> (<see cref="Effect.AddRule"/> &amp; other token effects, plus the imperative
    ///         <see cref="Effect.TriggeredMove"/>): the effect is applied to each target via the polymorphic
    ///         <see cref="Effect.Apply"/>; the resulting token operations are committed (the "gets RULE once
    ///         (next time)" buff/debuff shape) and any <see cref="ExecutableOperation"/> is run through the
    ///         <c>IOperationServices</c> seam — #034's "reposition an enemy unit" spell moves the target with
    ///         the caster directing. No child pipeline.</item>
    ///   <item><b>Conditional</b> (<see cref="Effect.MoraleTestThen"/>, #034 #5): each target takes a morale
    ///         test and the nested on-fail effect (a forced move or fatigue) lands only on a failure. Enacted
    ///         stage-side because the test is async; the on-fail effect reuses the non-damage application
    ///         path above. No child pipeline.</item>
    ///   <item><b>Damage</b> (<see cref="Effect.DealHits"/>): resolved through the looped child
    ///         <see cref="ResolveSpellDamageStage"/> — once per chosen target, each with its own fresh
    ///         <see cref="CombatMetadata"/> (the <see cref="ShootStage"/>/<see cref="FireStage"/> pattern).
    ///         Each target's hits run the hit-complete fold (Blast multiply, on-6 extra hits, Rending AP)
    ///         then the shared save→wound→assign→apply pipeline. AP + the spell's pre-resolved weapon rules
    ///         ride the synthetic weapon; #034 single-model spells confine wounds to one chosen model.</item>
    /// </list>
    ///
    /// #103 — before the roll, other Caster units within 18" may spend their own spell tokens to sway the
    /// cast: friendly Casters add +1 each, enemy Casters subtract 1 each. The net modifier shifts the 4+
    /// success threshold (clamped to [2, 6] — a natural 1 always fails, a natural 6 always succeeds);
    /// assisters' tokens are spent whether or not the cast succeeds.
    ///
    /// #244 — the caster may also boost their OWN roll: the spell picker (<see cref="ChooseSpellRequest"/>)
    /// returns extra tokens to spend at +1 each, spent together with the cast cost (so cancelling at target
    /// selection still spends nothing) and announced like a friendly assist so enemy hinderers decide with
    /// the boost visible. #233 — the cast roll rides a <see cref="DiceRolledBeat"/> before the result banner.
    /// </summary>
    public class CastSpellStage : ParentStage<IUnitActionContext, SpellDamageRunContext>
    {
        public StageBinding OnFinished;

        private const int CAST_SUCCESS_THRESHOLD = 4;

        // #103 assist text-beat colors - match the GUI highlight: blue for a friendly boost (+), orange for
        // an enemy disruption (-).
        private static readonly TextColor AssistBannerColor = new TextColor(77, 153, 255, 255);
        private static readonly TextColor HinderBannerColor = new TextColor(255, 153, 38, 255);

        // Cast-result text-beat colors: blue on a successful cast, red on a failure.
        private static readonly TextColor CastSuccessColor = new TextColor(77, 153, 255, 255);
        private static readonly TextColor CastFailColor = new TextColor(220, 40, 40, 255);

        // #293 effect-report color: violet, distinct from the blue cast-result line above so "the spell
        // went off" and "here is what it did" don't read as one repeated announcement.
        private static readonly TextColor EffectBannerColor = new TextColor(178, 132, 255, 255);

        // The damage run set up in Enter (synthetic weapon + chosen targets + base hits + any single-model
        // pick) and handed to the looped child pipeline by GetNewChildContext. Null on the buff path, which
        // applies its effect inline and never enters the children.
        private SpellDamageRunContext _pendingRun;

        public CastSpellStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            _pendingRun = null;

            IUnit caster = context.ActivatingUnit.GetValue();
            PlayerID player = context.ActivatingPlayer();

            // #234 — mirror of ChooseActionStage.GetCanCast's HasAttacked gate: Caster(X) is "at any point
            // before attacking" (GF v3.5.1). ChooseActionStage won't offer Cast once the unit has attacked,
            // but this stage is reachable directly, so the rule is enforced here too rather than trusted
            // to the menu.
            if (context.HasAttacked)
            {
                GameContext.Log($"{caster.Name} has already attacked and can no longer cast this activation.");
                await OnFinished.Activate(context);
                return;
            }

            // Only castable spells are selectable: affordable AND with at least one legal target. Gating on
            // castability here (and the same way in ChooseActionStage.GetCanCast) is what keeps a no-target
            // cast from looping forever under a deterministic resolver — the same reason
            // ChooseRangedAttackStage filters weapons to those with a fireable target. Non-castable spells
            // still appear in the picker as disabled rows with the reason (#244).
            // #197 P23 — the purse, not the unit's own pool: a nearby friendly Spell Accumulator's tokens
            // are spendable "as if they were their own spell tokens", so they count toward affordability
            // everywhere an owned token does.
            int tokens = SpellPurse.Available(GameContext.TableState, GameContext.RuleEvaluator, caster);

            // #197 P23 — where the cast may originate: the caster's own position, plus any Spell Conduit
            // relaying for it. Relays come first, so preferring the head of the viable list prefers the
            // bonus. Computed once and threaded through castability, targeting and the roll.
            IReadOnlyList<SpellRelay.CastOrigin> origins = SpellRelay.OriginsFor(
                GameContext.TableState, GameContext.RuleEvaluator, caster);

            IReadOnlyList<SpellOffer> offer = BuildSpellOffer(context, context.ActivatingUnit, player, tokens,
                origins);
            if (!offer.Any(o => o.Castable))
            {
                GameContext.Log($"{caster.Name} has no castable spell (none affordable with a legal target).");
                await OnFinished.Activate(context);
                return;
            }

            // 1. Pick a spell + a #244 self-boost count (or cancel back to Choose Action). The boost is only
            //    committed at step 3, with the cast cost, so cancelling target selection spends nothing.
            (RuntimeSpell? chosen, int boost) = await PickSpell(context.ActivatingUnit, player, offer, tokens,
                origins);
            if (chosen == null)
            {
                await OnFinished.Activate(context);
                return;
            }

            // 2. Build the eligible targets — the union over every origin — and let the player pick (up to
            //    the spell's MaxCount), narrowing the viable origins as they do. What comes back is the
            //    targets AND the origin the cast will actually be made from.
            var targeting = new RelayedTargeting(this, context.ActivatingUnit, player, chosen, origins);
            if (targeting.Candidates.Count == 0)
            {
                GameContext.Log($"{chosen.Name} has no valid target in range or line of sight.");
                await OnFinished.Activate(context);
                return;
            }

            IReadOnlyList<DataBinding<UnitData>> targets = await PickTargets(player, chosen, targeting);
            if (targets.Count == 0)
            {
                // Cancelled before meeting the minimum target count — nothing spent.
                await OnFinished.Activate(context);
                return;
            }

            SpellRelay.CastOrigin castOrigin = targeting.ChosenOrigin;

            // 3. Spend the spell's token cost + the #244 self-boost to attempt (spent whether or not the
            //    cast succeeds). A nonzero boost is announced like a friendly assist so the #103 hinderers
            //    below decide with the boost visible (open information, matching the tabletop).
            // #248: tokens are spent whether or not the cast succeeds — from here the activation can no
            // longer be backed out of. Every cancel path above returns before this line, so a browsed-and-
            // cancelled spell menu stays pristine.
            context.MarkIrreversibleAction();
            // #249 — "only one try per spell": recorded with the cost, before the roll, so a failed cast
            // burns the try exactly as a successful one does. Every cancel path above returns first, so a
            // browsed-and-cancelled spell is not consumed.
            context.RegisterSpellAttempt(chosen.Name);
            IReadOnlyList<SpellPurse.Loan> loans = SpellPurse.Spend(GameContext.TableState,
                GameContext.RuleEvaluator, caster, chosen.Threshold + boost);
            if (loans.Count > 0)
            {
                // Borrowing is worth surfacing: the tokens left someone else's pool, and that player
                // needs to see it happen rather than notice the shortfall later. A toast, though -- it
                // is one of up to four lines of cast bookkeeping that precede the roll everyone is
                // actually waiting on, and stacking pauses in front of that roll is what #275 fixed.
                await GameContext.Announce(
                    $"{caster.Name} draws on nearby accumulators to cast {chosen.Name} " +
                    $"({SpellPurse.Describe(loans)}).", AssistBannerColor, EBannerTier.Toast);
            }
            if (boost > 0)
            {
                await GameContext.Announce(
                    $"{caster.Name} boosts their own cast of {chosen.Name} (+{boost}).", AssistBannerColor,
                    EBannerTier.Toast);
            }

            if (!castOrigin.IsSelf)
            {
                // Announced, not merely logged: the relay is why the spell reached and why the roll is
                // easier, and a player who never sees it happen cannot learn to set it up.
                await GameContext.Announce(
                    $"{caster.Name} casts {chosen.Name} through {castOrigin.Unit.Name} " +
                    $"(+{castOrigin.RollBonus}).", AssistBannerColor, EBannerTier.Toast);
            }

            // 4. #103 — other Caster units within 18" may spend their own tokens to sway the cast: friendly
            //    Casters add +1 each, enemy Casters subtract 1 each. Their tokens are spent regardless of the
            //    cast's outcome (like the cast cost above). Boost + assists shift the success threshold.
            CastAssistResult assistResult = await CollectCastAssist(context.ActivatingUnit, player, chosen.Name);
            int assist = assistResult.Net;

            // #197 P6 — granted cast-roll modifiers (Casting Debuff / Casting Buff): a signed delta the
            // caster is carrying as a token. Consumed here, once, whether or not the cast succeeds, exactly
            // as the hit/save/morale sites consume theirs at their own roll; "once" (NextTrigger) grants are
            // removed by ConsumeNet, duration grants are left for the token sweep. Read after the cost has
            // been spent so a browsed-and-cancelled spell never burns the debuff.
            int granted = GrantedRollModifiers.ConsumeNet(caster, ERollKind.Cast);
            int netModifier = boost + assist + granted + castOrigin.RollBonus;

            // #274 — the caster's own models, reused by every spell visual below.
            List<Position> casterPositions = AttackBeatPositions.AlivePlaced(context.ActivatingUnit);

            // #274 — every token spend that swayed this roll, shown as one boost beat and one hinder
            // beat immediately before the die is cast, so the odds visibly move before the result.
            await PresentAssistVisuals(chosen.Name, casterPositions, boost, assistResult);

            // 5. Cast roll: one die, base 4+ succeeds, shifted by boost + assists. RollDecisive so it's a
            //    real outcome under the probabilistic roller; a threshold shift (not a post-roll adjustment)
            //    keeps it a single decisive comparison. The shared clamp keeps the GDF core principle (a
            //    natural 1 always fails, a natural 6 always succeeds - [2, 6]) no matter how far
            //    boost/assists (or hinders) swing the roll, same as the hit/save/morale sites.
            int threshold = DiceUtilities.ClampSuccessRollNeeded(CAST_SUCCESS_THRESHOLD - netModifier);
            IDiceResults castRoll = GameContext.DiceRoller.RollDecisive();
            bool success = castRoll.AtOrAbove(threshold) >= 1f;

            // #233 — show the die itself: tumbling roll with the shifted threshold, settling into a short
            // outcome summary. The banner below carries the full math. #289: FromDecisive - the cast die is
            // one concrete face in either roller mode, so it draws as a die, never an expected-value bar.
            await GameContext.Presenter.Present(DiceRolledBeat.FromDecisive(castRoll, threshold,
                "Roll to Cast", success ? "Cast!" : "Failed", category: ERollBeatCategory.Magic));

            // Spell out the roll so the boost/assist math is visible: what came up, what it needed, and how
            // the base 4+ was shifted. Assisters' own contributions were announced as they spent. The result
            // rides an on-screen text beat: blue on success, red on failure. ASCII only (the log font has no
            // em-dash glyph, #151).
            string breakdown = BuildRollBreakdown(boost, assist, granted, castOrigin.RollBonus);
            string tokensSpent = boost > 0
                ? $"spent {chosen.Threshold + boost} tokens ({chosen.Threshold} cost + {boost} boost)"
                : $"spent {chosen.Threshold} token{(chosen.Threshold == 1 ? "" : "s")}";
            string rollDesc = $"rolled {DecisiveFace(castRoll)}, needed {threshold}+{breakdown}; {tokensSpent}";

            if (!success)
            {
                await GameContext.Announce($"{caster.Name} failed to cast {chosen.Name}: {rollDesc}.", CastFailColor);
                // #274 — the spell guttering out on the caster. Always plays on a failure.
                await PresentSpellVisual(ESpellVisual.CastFailure, casterPositions, chosen.Name);
                await OnFinished.Activate(context);
                return;
            }

            await GameContext.Announce($"{caster.Name} cast {chosen.Name}: {rollDesc}.", CastSuccessColor);

            // #274 — the spell taking hold on the caster, then washing over everything it was aimed
            // at. Emitted here, before any of the three effect paths below, so the target landing
            // always follows the caster's success immediately: on the damage path the child pipeline's
            // own attack/dice/wound beats then play out the damage the landing depicted.
            await PresentSpellVisual(ESpellVisual.CastSuccess, casterPositions, chosen.Name);
            await PresentTargetVisuals(chosen, targets);

            // 6a. Damage spell → resolve each chosen target through the looped child pipeline.
            if (chosen.Effect is Effect.DealHits dealHits)
            {
                // Synthetic spell weapon: the spell's AP + its pre-resolved weapon rules (shared across targets).
                Weapon spellWeapon = new Weapon(chosen.Name, rangeInches: 0f, attacks: 0,
                    armorPenetration: dealHits.ArmorPenetration);
                foreach (ResolvedRule rule in chosen.WeaponRules)
                {
                    spellWeapon.AttachRuleDefinition(rule);
                }

                // #034 single-model targeting: pick the one model now (the cast is committed, so it's
                // mandatory). Single-model spells use MaxCount = 1, so there is exactly one target.
                DataBinding<ModelData> individualModel = null;
                if (chosen.Target.SingleModel)
                {
                    individualModel = await PickIndividualModel(player, chosen.Name, targets[0]);
                }

                // #293 — the damage path reports too, so every successful cast says what it does. Emitted
                // HERE (after any single-model pick, before the child pipeline) rather than after the
                // damage resolves: the stage hands off to the children and returns, so there is no
                // after. That is also why this one line is present tense - it announces the incoming
                // hits and their type, and the pipeline's own dice/wound beats then play them out.
                await AnnounceEffect(SpellText.DescribeApplied(chosen.Name, dealHits, TargetNames(targets)));

                _pendingRun = new SpellDamageRunContext(context.ActivatingUnit, spellWeapon, dealHits.Count,
                    targets, individualModel);

                await base.Enter(context); // loops ResolveSpellDamageStage per target, then OnFinished
                return;
            }

            // 6b. Conditional spell (#034 #5) → each target takes a morale test; the on-fail effect lands
            //     only on a failure. Stage-side because the test is async and rolls against live state.
            if (chosen.Effect is Effect.MoraleTestThen conditional)
            {
                await ResolveConditionalSpell(caster, chosen, conditional, targets);
                await OnFinished.Activate(context);
                return;
            }

            // 6c. Non-damage spell → apply the effect to each target (no child pipeline): token grants
            //     (buff/debuff) and inline engine operations (forced enemy move, #034) both flow through here.
            foreach (DataBinding<UnitData> target in targets)
            {
                await ApplyEffectToTarget(caster, chosen.Effect, target);
            }
            // #293 — say what the spell DID, not just that it went off. The cast banner above reports the
            // roll; without this the buff/debuff/move/mark paths announce nothing at all and the player has
            // to infer the effect from token chips.
            await AnnounceEffect(SpellText.DescribeApplied(chosen.Name, chosen.Effect, TargetNames(targets)));
            GameContext.Log($"{chosen.Name} affected {targets.Count} unit(s).");
            await OnFinished.Activate(context);
        }

        // #293 — the effect report. A Notice (tier 1): it is worth reading, but the cast Headline already
        // stopped play once for this spell and a second full-stop for its consequence is what #275 set out
        // to remove. ASCII only (CLAUDE.md) - the composers never emit anything above U+00FF.
        private async Task AnnounceEffect(string text) =>
            await GameContext.Announce($"{text}.", EffectBannerColor, EBannerTier.Notice);

        // The unit names an effect banner reports, in the order the player picked them.
        private static IReadOnlyList<string> TargetNames(IEnumerable<DataBinding<UnitData>> targets) =>
            targets.Select(t => t.GetValue().Name).ToList();

        // #274 — one spell visual at a set of model positions. A unit whose models are all dead or
        // still in reserve yields no positions; the beat is dropped rather than emitted placeless
        // (an empty beat would pace real time for nothing to draw).
        private async Task PresentSpellVisual(ESpellVisual visual, IReadOnlyList<Position> positions,
            string spellName, IReadOnlyList<Position> sources = null, int magnitude = 0)
        {
            if (positions.Count == 0) return;
            await GameContext.Presenter.Present(
                new SpellEffectBeat(visual, positions, spellName, sources, magnitude));
        }

        // #274 — the spell landing on each chosen target, in one beat carrying every target model so
        // a multi-target spell lands on all of them together (the front-end staggers them cosmetically
        // within the beat's envelope). Which variant plays is the spell's disposition, not the
        // target's allegiance: an Any-affinity heal cast on an enemy is still a boon for them.
        private async Task PresentTargetVisuals(RuntimeSpell spell, IReadOnlyList<DataBinding<UnitData>> targets)
        {
            List<Position> positions = new List<Position>();
            foreach (DataBinding<UnitData> target in targets)
            {
                positions.AddRange(AttackBeatPositions.AlivePlaced(target));
            }

            ESpellVisual visual = SpellDisposition.IsBeneficial(spell)
                ? ESpellVisual.TargetBoon
                : ESpellVisual.TargetBane;
            await PresentSpellVisual(visual, positions, spell.Name);
        }

        // #274 — the token spends that moved the odds, batched into at most two beats (one per
        // direction) right before the roll. The caster's own #244 boost joins the boost beat with no
        // source of its own: it is a spend on the same side of the same roll, and the front-end draws
        // the pulse alone when there is nothing to stream from.
        private async Task PresentAssistVisuals(string spellName, IReadOnlyList<Position> casterPositions,
            int selfBoost, CastAssistResult assists)
        {
            int boostTokens = selfBoost + assists.BoostTokens;
            if (boostTokens > 0)
            {
                await PresentSpellVisual(ESpellVisual.AssistBoost, casterPositions, spellName,
                    assists.BoostSources, boostTokens);
            }

            if (assists.HinderTokens > 0)
            {
                await PresentSpellVisual(ESpellVisual.AssistHinder, casterPositions, spellName,
                    assists.HinderSources, assists.HinderTokens);
            }
        }

        protected override SpellDamageRunContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            // Only the damage path enters children, and it set _pendingRun before base.Enter.
            return _pendingRun;
        }

        // #034 #5 — each target takes a morale test (its morale Quality, rule-aware via MoraleUtilities);
        // the spell's on-fail effect is applied only to targets that fail. Deep Hypnosis (move on fail) and
        // Terrifying Fury (fatigue on fail) share this shape.
        private async Task ResolveConditionalSpell(IUnit caster, RuntimeSpell spell,
            Effect.MoraleTestThen conditional, IReadOnlyList<DataBinding<UnitData>> targets)
        {
            // #293 — collected rather than announced per target, so a multi-target conditional spell
            // reports its outcome as ONE banner instead of a pile of them mid-resolution.
            List<string> failed = new List<string>();
            List<string> passed = new List<string>();

            foreach (DataBinding<UnitData> target in targets)
            {
                UnitData targetUnit = target.GetValue();
                MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                    GameContext, targetUnit, HeroStatRules.GetMoraleQuality(targetUnit));

                if (outcome.Passed)
                {
                    GameContext.Log($"{targetUnit.Name} passed {spell.Name}'s morale test - no effect.");
                    passed.Add(targetUnit.Name);
                    continue;
                }

                GameContext.Log($"{targetUnit.Name} failed {spell.Name}'s morale test.");
                failed.Add(targetUnit.Name);
                await ApplyEffectToTarget(caster, conditional.OnFailure, target);
            }

            await AnnounceEffect(SpellText.DescribeConditionalApplied(
                spell.Name, conditional.OnFailure, failed, passed));
        }

        // Applies one spell effect to one target by running the effect's polymorphic Apply against a
        // per-target invocation (caster = bearer), then enacting the resulting operations. Two disjoint
        // operation kinds come out: token grants (AddRule "gets RULE once", StatModifier buffs) committed by
        // ApplyTokenOperations, and imperative ExecutableOperations (TriggeredMove — #034's forced enemy
        // move; ApplyFatigue) run by OperationExecutor. The two filters don't overlap, so applying both is
        // safe for any effect. The caster is the bearer, so a TriggeredMove targeting an enemy routes the
        // move request to the caster. Shared by the plain non-damage path and the conditional on-fail branch.
        private async Task ApplyEffectToTarget(IUnit caster, Effect effect, DataBinding<UnitData> target)
        {
            RuleInvocation invocation = new RuleInvocation(
                Hook: null, Bearer: caster, Arguments: System.Array.Empty<RuleArgument>(),
                Target: target.GetValue(), DiceRoller: GameContext.DiceRoller);

            List<RuleOperation> operations = new List<RuleOperation>();
            effect.Apply(invocation, operations);
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, new GameOperationServices(GameContext));
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<SpellDamageRunContext> startingChild)
        {
            OnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ResolveSpellDamageStage(GameContext, this), out var resolveSpellDamage)
                .AddChild(new DetermineMoreSpellTargetsStage(GameContext, this), out var determineMoreTargets)
                .AddSibling(nameof(OnFinished), OnFinished, out string finishedEvent)
                .Build();

            startingChild = resolveSpellDamage;

            // resolve one target → ask whether more remain → loop back to resolve the next, or finish.
            resolveSpellDamage.OnFinished.Bind(determineMoreTargets);
            determineMoreTargets.ResolveNextTarget.Bind(resolveSpellDamage);
            determineMoreTargets.ToFinished.Bind(finishedEvent);

            return dictionary;
        }

        // One row of the spell picker: the spell plus whether it can be cast right now (affordable AND has a
        // legal target) and, when it can't, the reason shown on the disabled row.
        internal readonly record struct SpellOffer(RuntimeSpell Spell, bool Castable, string? Reason);

        // Every army spell in stable order, each marked castable or carrying its unavailability reason.
        // Affordability is checked before targets so the cheaper check names the blocking condition first.
        // #249: already-tried spells are checked first — that one is permanent for the activation, so it
        // outranks the transient "need N tokens" / "no valid target" reasons.
        private IReadOnlyList<SpellOffer> BuildSpellOffer(IUnitActionContext context,
            DataBinding<UnitData> caster, PlayerID player, int tokens,
            IReadOnlyList<SpellRelay.CastOrigin> origins)
        {
            ArmyData army = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .FirstOrDefault(a => a.PlayerID == player);
            if (army == null)
            {
                return System.Array.Empty<SpellOffer>();
            }

            List<SpellOffer> offer = new List<SpellOffer>();
            foreach (RuntimeSpell spell in army.Spells)
            {
                if (spell.Threshold <= 0) continue; // malformed — never offered, matching the old filter
                if (context.HasAttemptedSpell(spell.Name))
                {
                    offer.Add(new SpellOffer(spell, false, "already tried this activation"));
                }
                else if (spell.Threshold > tokens)
                {
                    offer.Add(new SpellOffer(spell, false, $"need {spell.Threshold} tokens"));
                }
                else if (!SpellTargeting.HasAnyEligibleTargetFromAny(GameContext, caster, player,
                             spell.Target, origins))
                {
                    // From ANY origin: a spell the caster cannot reach itself is still castable through a
                    // relay that can, so gating on the caster's own reach would hide it from the picker.
                    offer.Add(new SpellOffer(spell, false, "no valid target"));
                }
                else
                {
                    offer.Add(new SpellOffer(spell, true, null));
                }
            }
            return offer;
        }

        // #244 — one request returns both the spell and the caster's own boost spend. The reply's boost is
        // clamped to what remains after the spell's cost; an out-of-range or non-castable index cancels.
        private async Task<(RuntimeSpell? spell, int boost)> PickSpell(DataBinding<UnitData> casterBinding,
            PlayerID player, IReadOnlyList<SpellOffer> offer, int tokens,
            IReadOnlyList<SpellRelay.CastOrigin> origins)
        {
            List<ChooseSpellRequest.SpellOption> options = offer
                .Select(o => new ChooseSpellRequest.SpellOption(SpellOptionLabel(o.Spell),
                    SpellText.Describe(o.Spell.Definition), o.Spell.Threshold, o.Castable, o.Reason))
                .ToList();

            // How much the roll could still be hindered after the caster commits — enemy Casters in assist
            // range and their tokens — so the picker can gray out boost past the point it can matter.
            // Each hinderer's whole purse, summed. Deliberately an over-estimate: it assumes every enemy
            // Caster spends everything, and if two of them can reach one accumulator it counts that pool
            // twice. Both errors point the same way (the picker grays out boost slightly early) and the
            // alternative is solving an allocation the enemy hasn't made yet.
            int hinderTokens = FindEligibleAssisters(casterBinding, player)
                .Where(entry => !entry.friendly)
                .Sum(entry => SpellPurse.Available(GameContext.TableState, GameContext.RuleEvaluator,
                    entry.unit.GetValue()));

            // #197 P23 — tell the picker a relay is in range so the player knows the bonus is on the table
            // before choosing. Which targets actually get it is stated per row in the target list, where
            // the answer is definite; here it is only "this exists and here is what it does".
            IReadOnlyList<ChooseSpellRequest.RelayOption> relays = origins
                .Where(origin => !origin.IsSelf)
                .Select(origin => new ChooseSpellRequest.RelayOption(origin.Unit.Name, origin.RollBonus))
                .ToList();

            ChooseSpellRequest request = new ChooseSpellRequest(player, casterBinding, tokens,
                CAST_SUCCESS_THRESHOLD, hinderTokens, options, relays);

            ChooseSpellReply reply = await GameContext.PlayerRequester
                .RequestDecision<ChooseSpellRequest, ChooseSpellReply>(request);

            if (reply == null || reply.SpellIndex < 0 || reply.SpellIndex >= offer.Count
                || !offer[reply.SpellIndex].Castable)
            {
                return (null, 0);
            }

            RuntimeSpell spell = offer[reply.SpellIndex].Spell;
            int boost = System.Math.Clamp(reply.BoostTokens, 0, tokens - spell.Threshold);
            return (spell, boost);
        }

        // "(base 4+, relay +1, self +1, assists -2, granted -1)" — only the parts that apply; empty when the
        // roll is unmodified. "granted" is the #197 P6 token delta (Casting Debuff / Casting Buff); "relay"
        // is the #197 P23 Spell Conduit bonus, listed first because it is the one the player did not ask
        // for by name.
        private static string BuildRollBreakdown(int boost, int assist, int granted, int relay)
        {
            if (boost == 0 && assist == 0 && granted == 0 && relay == 0) return "";
            List<string> parts = new List<string> { $"base {CAST_SUCCESS_THRESHOLD}+" };
            if (relay != 0) parts.Add($"relay +{relay}");
            if (boost != 0) parts.Add($"self +{boost}");
            if (assist != 0) parts.Add($"assists {(assist > 0 ? "+" : "")}{assist}");
            if (granted != 0) parts.Add($"granted {(granted > 0 ? "+" : "")}{granted}");
            return $" ({string.Join(", ", parts)})";
        }

        private async Task<IReadOnlyList<DataBinding<UnitData>>> PickTargets(PlayerID player, RuntimeSpell spell,
            RelayedTargeting targeting)
        {
            List<DataBinding<UnitData>> chosen = new List<DataBinding<UnitData>>();

            for (int picked = 0; picked < spell.Target.MaxCount; picked++)
            {
                IReadOnlyList<DataBinding<UnitData>> remaining = targeting.Remaining(chosen);
                if (remaining.Count == 0) break;

                // Each row says which origin the cast would use if that target is picked — "(via Synaptic
                // Relay, +1)". That is the honest place for it: at spell-pick time the origin is not yet
                // decided, and this is exactly the information a player needs to aim for the bonus.
                List<SelectionRequest<UnitData>.ValidOption> validOptions = remaining
                    .Select(u => new SelectionRequest<UnitData>.ValidOption(u,
                        TargetLabel(u, targeting.OriginFor(chosen, u))))
                    .ToList();

                SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(player,
                    $"Choose target for {spell.Name} ({chosen.Count + 1} of up to {spell.Target.MaxCount})",
                    validOptions, System.Array.Empty<SelectionRequest<UnitData>.InvalidOption>(),
                    allowCancel: true, displayName: $"Choosing a Target for {spell.Name}");

                DataBinding<UnitData> target = await GameContext.PlayerRequester
                    .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);

                if (target == null)
                {
                    // Cancel stops target selection: proceed with what's chosen if the minimum is met,
                    // otherwise the caller treats it as cancelling the cast (nothing spent).
                    break;
                }

                chosen.Add(target);
            }

            if (chosen.Count < spell.Target.MinCount) return new List<DataBinding<UnitData>>();

            targeting.Commit(chosen);
            return chosen;
        }

        // "Dummies (via Synaptic Relay, +1)" for a relayed target, the plain name for one the caster
        // reaches itself. ASCII only (#151).
        private static string TargetLabel(DataBinding<UnitData> target, SpellRelay.CastOrigin origin) =>
            origin.IsSelf
                ? target.GetValue().Name
                : $"{target.GetValue().Name} (via {origin.Unit.Name}, +{origin.RollBonus})";

        // #034 single-model targeting: pick one living model in the target unit ("a unit of [1]"). The cast
        // has already succeeded and its tokens are spent, so the pick is mandatory (no cancel) — mirroring
        // Takedown's BuildTargetListStage.MaybePickIndividualTarget.
        private async Task<DataBinding<ModelData>> PickIndividualModel(PlayerID player, string spellName,
            DataBinding<UnitData> targetUnit)
        {
            List<DataBinding<ModelData>> living = targetUnit.ModelBindings()
                .Where(model => model.GetIsAlive())
                .ToList();

            // A legal damage target always has at least one living model; guard anyway so a degenerate
            // case falls back to whole-unit allocation rather than raising an option-less request.
            if (living.Count == 0)
            {
                return null;
            }

            List<SelectionRequest<ModelData>.ValidOption> validOptions = new List<SelectionRequest<ModelData>.ValidOption>();
            for (int i = 0; i < living.Count; i++)
            {
                validOptions.Add(new SelectionRequest<ModelData>.ValidOption(living[i], $"Model {i + 1}"));
            }

            SelectionRequest<ModelData> request = new SelectionRequest<ModelData>(player,
                $"{spellName}: choose the target model",
                validOptions, System.Array.Empty<SelectionRequest<ModelData>.InvalidOption>(), allowCancel: false,
                displayName: $"Choosing a Target for {spellName}");

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<ModelData>, DataBinding<ModelData>>(request);
        }

        // #274 — what CollectCastAssist gathered: the net roll modifier the roll actually uses, plus who
        // spent on each side and how much, for the batched assist visuals. Positions are captured as the
        // assisters commit; the roll only ever reads Net.
        private readonly record struct CastAssistResult(int Net, int BoostTokens, int HinderTokens,
            IReadOnlyList<Position> BoostSources, IReadOnlyList<Position> HinderSources);

        // #103 — offer every eligible Caster within 18" the chance to spend tokens on this cast, sum the
        // result into a net roll modifier (friendly +1/token, enemy -1/token) and spend the tokens. Friendly
        // helpers declare first — the casting side commits support, then the enemy responds. Tokens are spent
        // whether or not the cast then succeeds, matching the cast cost. Returns a zero net when no one
        // assists, so a game with no nearby Casters sees no prompts and no behaviour change.
        private async Task<CastAssistResult> CollectCastAssist(DataBinding<UnitData> casterBinding,
            PlayerID casterPlayer, string spellName)
        {
            string casterName = casterBinding.GetValue().Name;
            int net = 0;
            int boostTokens = 0, hinderTokens = 0;
            List<Position> boostSources = new List<Position>();
            List<Position> hinderSources = new List<Position>();
            foreach ((DataBinding<UnitData> unitBinding, bool friendly) in FindEligibleAssisters(casterBinding, casterPlayer))
            {
                IUnit assister = unitBinding.GetValue();

                // Re-read per assister rather than reusing FindEligibleAssisters' snapshot: an earlier
                // assister this round may have drained the accumulator they share.
                int available = SpellPurse.Available(GameContext.TableState, GameContext.RuleEvaluator,
                    assister);
                if (available <= 0) continue;

                int spent = await AskAssistCount(unitBinding, casterBinding, friendly, available, spellName);
                if (spent <= 0) continue;

                IReadOnlyList<SpellPurse.Loan> loans = SpellPurse.Spend(GameContext.TableState,
                    GameContext.RuleEvaluator, assister, spent);
                net += friendly ? spent : -spent;

                // #274 — record the spender for the batched visual played just before the roll.
                if (friendly)
                {
                    boostTokens += spent;
                    boostSources.AddRange(AttackBeatPositions.AlivePlaced(unitBinding));
                }
                else
                {
                    hinderTokens += spent;
                    hinderSources.AddRange(AttackBeatPositions.AlivePlaced(unitBinding));
                }

                if (loans.Count > 0)
                {
                    GameContext.Log($"{assister.Name} funds that with borrowed accumulator tokens " +
                                    $"({SpellPurse.Describe(loans)}).");
                }

                // Text beat: announce who assisted/hindered and by how much — an on-screen toast plus the
                // log line, blue for a friendly boost and orange for an enemy disruption (matches the GUI
                // highlight). Only fires when a Caster actually spends (declines skip via the guard above).
                // Toast because this loop runs once per nearby Caster: a table with four of them would
                // otherwise stack four full pauses before the cast roll.
                await GameContext.Announce(
                    $"{assister.Name} {(friendly ? "assists" : "hinders")} {casterName}'s cast of {spellName} " +
                    $"({(friendly ? "+" : "-")}{spent}).",
                    friendly ? AssistBannerColor : HinderBannerColor, EBannerTier.Toast);
            }
            return new CastAssistResult(net, boostTokens, hinderTokens, boostSources, hinderSources);
        }

        // Living, on-battlefield Caster units (other than the caster) that hold at least one spell token and
        // whose unit is within CASTER_ASSIST_RANGE_INCHES (base-to-base, 3D) of the casting unit. Friendly
        // Casters (same team, or same player when no team is registered — mirroring SpellTargeting) come
        // first, then enemy Casters, each group in store order for a deterministic prompt sequence.
        private List<(DataBinding<UnitData> unit, bool friendly)> FindEligibleAssisters(
            DataBinding<UnitData> casterBinding, PlayerID casterPlayer)
        {
            TeamData team = GameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(casterPlayer));
            bool IsFriendly(PlayerID p) => team != null ? team.IsPlayerOnTeam(p) : p == casterPlayer;

            IUnit castingUnit = casterBinding.GetValue();
            List<(DataBinding<UnitData>, bool)> friends = new List<(DataBinding<UnitData>, bool)>();
            List<(DataBinding<UnitData>, bool)> enemies = new List<(DataBinding<UnitData>, bool)>();

            IEnumerable<DataBinding<UnitData>> allUnits = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .SelectMany(a => a.UnitBindings);

            foreach (DataBinding<UnitData> unitBinding in allUnits)
            {
                if (unitBinding.Reference.Equals(casterBinding.Reference)) continue; // not the casting unit
                UnitData unit = unitBinding.GetValue();
                if (!unit.GetIsAlive() || !unit.GetIsOnBattlefield()) continue;
                if (!SpellTargeting.IsCaster(GameContext, unit)) continue;
                if (SpellPurse.Available(GameContext.TableState, GameContext.RuleEvaluator, unit) <= 0) continue;

                float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                    castingUnit, unit, out _, out _, includeVertical: true);
                if (distance > GameWideConstants.CASTER_ASSIST_RANGE_INCHES) continue;

                bool friendly = IsFriendly(unit.PlayerID);
                (friendly ? friends : enemies).Add((unitBinding, friendly));
            }

            friends.AddRange(enemies);
            return friends;
        }

        // Ask one Caster's controller how many tokens to spend, via a CastAssistRequest that carries both
        // units so the GUI can highlight the assister and draw a line to the caster (blue friendly / orange
        // enemy) and show the token count. The reply is clamped to what the assister actually holds. CLI and
        // AI resolvers default to spending nothing.
        private async Task<int> AskAssistCount(DataBinding<UnitData> assistingUnit, DataBinding<UnitData> castingUnit,
            bool friendly, int available, string spellName)
        {
            CastAssistRequest request = new CastAssistRequest(
                assistingUnit.GetValue().PlayerID, assistingUnit, castingUnit, friendly, available, spellName);

            int spent = await GameContext.PlayerRequester
                .RequestDecision<CastAssistRequest, int>(request);

            return System.Math.Clamp(spent, 0, available);
        }

        /// <summary>
        /// #197 P23 — target selection once a <c>Spell Conduit</c> may be relaying the cast. A spell is cast
        /// from ONE position, so the origin and the target set are a single decision; but a relay origin is
        /// never worse than the caster's own (it can only add reach, and it adds the bonus), so the origin
        /// is derived from the targets rather than prompted for.
        ///
        /// <para>It works by keeping each origin's eligible set: the player is offered the UNION, and each
        /// pick narrows the origins to those that still cover everything chosen. <see cref="ChosenOrigin"/>
        /// is the best survivor. With no relay in range there is one origin and this degrades exactly to the
        /// old single-list behaviour.</para>
        /// </summary>
        private sealed class RelayedTargeting
        {
            private readonly List<(SpellRelay.CastOrigin origin, List<DataBinding<UnitData>> targets)> _byOrigin
                = new List<(SpellRelay.CastOrigin, List<DataBinding<UnitData>>)>();

            public IReadOnlyList<DataBinding<UnitData>> Candidates { get; }

            /// <summary>The origin the cast will be made from. Valid after <see cref="Commit"/>; before
            /// that it holds the best origin for an empty target set (any relay in range).</summary>
            public SpellRelay.CastOrigin ChosenOrigin { get; private set; }

            public RelayedTargeting(CastSpellStage stage, DataBinding<UnitData> caster, PlayerID player,
                RuntimeSpell spell, IReadOnlyList<SpellRelay.CastOrigin> origins)
            {
                foreach (SpellRelay.CastOrigin origin in origins)
                {
                    List<DataBinding<UnitData>> targets = SpellTargeting.GetEligibleTargets(
                        stage.GameContext, caster, player, spell.Target, origin.Unit);
                    if (targets.Count > 0) _byOrigin.Add((origin, targets));
                }

                // Origins arrive relays-first, so the first entry is already the preferred one; fall back to
                // the caster's own position when nothing reaches anything (Candidates is then empty and the
                // caller bails before this matters).
                ChosenOrigin = _byOrigin.Count > 0 ? _byOrigin[0].origin : origins[origins.Count - 1];

                List<DataBinding<UnitData>> union = new List<DataBinding<UnitData>>();
                foreach ((_, List<DataBinding<UnitData>> targets) in _byOrigin)
                {
                    foreach (DataBinding<UnitData> target in targets)
                    {
                        if (!Contains(union, target)) union.Add(target);
                    }
                }

                Candidates = union;
            }

            /// <summary>Targets still pickable given what is already chosen: reachable from some origin that
            /// covers every earlier pick, and not already picked.</summary>
            public IReadOnlyList<DataBinding<UnitData>> Remaining(IReadOnlyList<DataBinding<UnitData>> chosen)
            {
                List<DataBinding<UnitData>> remaining = new List<DataBinding<UnitData>>();
                foreach ((_, List<DataBinding<UnitData>> targets) in Viable(chosen))
                {
                    foreach (DataBinding<UnitData> target in targets)
                    {
                        if (!Contains(chosen, target) && !Contains(remaining, target)) remaining.Add(target);
                    }
                }

                return remaining;
            }

            /// <summary>The origin the cast would use if <paramref name="candidate"/> joined
            /// <paramref name="chosen"/> — what the target row reports.</summary>
            public SpellRelay.CastOrigin OriginFor(IReadOnlyList<DataBinding<UnitData>> chosen,
                DataBinding<UnitData> candidate)
            {
                foreach ((SpellRelay.CastOrigin origin, List<DataBinding<UnitData>> targets) in Viable(chosen))
                {
                    if (Contains(targets, candidate)) return origin;
                }

                return ChosenOrigin;
            }

            /// <summary>Fixes the origin once the target set is final.</summary>
            public void Commit(IReadOnlyList<DataBinding<UnitData>> chosen)
            {
                foreach ((SpellRelay.CastOrigin origin, _) in Viable(chosen))
                {
                    ChosenOrigin = origin;
                    return;
                }
            }

            // Origins whose set covers every already-chosen target, in preference order (relays first).
            private IEnumerable<(SpellRelay.CastOrigin origin, List<DataBinding<UnitData>> targets)> Viable(
                IReadOnlyList<DataBinding<UnitData>> chosen)
            {
                foreach ((SpellRelay.CastOrigin origin, List<DataBinding<UnitData>> targets) in _byOrigin)
                {
                    bool covers = true;
                    foreach (DataBinding<UnitData> already in chosen)
                    {
                        if (!Contains(targets, already)) { covers = false; break; }
                    }

                    if (covers) yield return (origin, targets);
                }
            }

            private static bool Contains(IReadOnlyList<DataBinding<UnitData>> list, DataBinding<UnitData> unit)
            {
                foreach (DataBinding<UnitData> entry in list)
                {
                    if (entry.Reference.Equals(unit.Reference)) return true;
                }

                return false;
            }
        }

        private static string SpellOptionLabel(RuntimeSpell spell) => $"{spell.Name} ({spell.Threshold})";

        // The single face that came up on a decisive (one-die) roll, for logging. A decisive roll has exactly
        // one face with weight, so this returns it; falls back to the minimum face if none is set.
        private static int DecisiveFace(IDiceResults roll)
        {
            for (int face = roll.SideMin; face <= roll.SideMax; face++)
            {
                if (roll.At(face) > 0f) return face;
            }
            return roll.SideMin;
        }
    }
}
