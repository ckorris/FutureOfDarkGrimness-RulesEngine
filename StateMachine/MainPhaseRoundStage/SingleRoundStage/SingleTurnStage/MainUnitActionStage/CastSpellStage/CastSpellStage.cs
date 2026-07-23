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
            IReadOnlyList<SpellOffer> offer = BuildSpellOffer(context, context.ActivatingUnit, player, tokens);
            if (!offer.Any(o => o.Castable))
            {
                GameContext.Log($"{caster.Name} has no castable spell (none affordable with a legal target).");
                await OnFinished.Activate(context);
                return;
            }

            // 1. Pick a spell + a #244 self-boost count (or cancel back to Choose Action). The boost is only
            //    committed at step 3, with the cast cost, so cancelling target selection spends nothing.
            (RuntimeSpell? chosen, int boost) = await PickSpell(context.ActivatingUnit, player, offer, tokens);
            if (chosen == null)
            {
                await OnFinished.Activate(context);
                return;
            }

            // 2. Build the eligible targets and let the player pick (up to the spell's MaxCount).
            List<DataBinding<UnitData>> candidates = SpellTargeting.GetEligibleTargets(
                GameContext, context.ActivatingUnit, player, chosen.Target);
            if (candidates.Count == 0)
            {
                GameContext.Log($"{chosen.Name} has no valid target in range or line of sight.");
                await OnFinished.Activate(context);
                return;
            }

            IReadOnlyList<DataBinding<UnitData>> targets = await PickTargets(player, chosen, candidates);
            if (targets.Count == 0)
            {
                // Cancelled before meeting the minimum target count — nothing spent.
                await OnFinished.Activate(context);
                return;
            }

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
                // Borrowing is worth its own banner: the tokens left someone else's pool, and that player
                // needs to see it happen rather than notice the shortfall later.
                await GameContext.Announce(
                    $"{caster.Name} draws on nearby accumulators to cast {chosen.Name} " +
                    $"({SpellPurse.Describe(loans)}).", AssistBannerColor);
            }
            if (boost > 0)
            {
                await GameContext.Announce(
                    $"{caster.Name} boosts their own cast of {chosen.Name} (+{boost}).", AssistBannerColor);
            }

            // 4. #103 — other Caster units within 18" may spend their own tokens to sway the cast: friendly
            //    Casters add +1 each, enemy Casters subtract 1 each. Their tokens are spent regardless of the
            //    cast's outcome (like the cast cost above). Boost + assists shift the success threshold.
            int assist = await CollectCastAssist(context.ActivatingUnit, player, chosen.Name);

            // #197 P6 — granted cast-roll modifiers (Casting Debuff / Casting Buff): a signed delta the
            // caster is carrying as a token. Consumed here, once, whether or not the cast succeeds, exactly
            // as the hit/save/morale sites consume theirs at their own roll; "once" (NextTrigger) grants are
            // removed by ConsumeNet, duration grants are left for the token sweep. Read after the cost has
            // been spent so a browsed-and-cancelled spell never burns the debuff.
            int granted = GrantedRollModifiers.ConsumeNet(caster, ERollKind.Cast);
            int netModifier = boost + assist + granted;

            // 5. Cast roll: one die, base 4+ succeeds, shifted by boost + assists. RollDecisive so it's a
            //    real outcome under the probabilistic roller; a threshold shift (not a post-roll adjustment)
            //    keeps it a single decisive comparison. The shared clamp keeps the GDF core principle (a
            //    natural 1 always fails, a natural 6 always succeeds - [2, 6]) no matter how far
            //    boost/assists (or hinders) swing the roll, same as the hit/save/morale sites.
            int threshold = DiceUtilities.ClampSuccessRollNeeded(CAST_SUCCESS_THRESHOLD - netModifier);
            IDiceResults castRoll = GameContext.DiceRoller.RollDecisive();
            bool success = castRoll.AtOrAbove(threshold) >= 1f;

            // #233 — show the die itself: tumbling roll with the shifted threshold, settling into a short
            // outcome summary. The banner below carries the full math.
            await GameContext.Presenter.Present(DiceRolledBeat.From(castRoll, threshold,
                GameContext.Settings.RandomnessType, "Roll to Cast", success ? "Cast!" : "Failed"));

            // Spell out the roll so the boost/assist math is visible: what came up, what it needed, and how
            // the base 4+ was shifted. Assisters' own contributions were announced as they spent. The result
            // rides an on-screen text beat: blue on success, red on failure. ASCII only (the log font has no
            // em-dash glyph, #151).
            string breakdown = BuildRollBreakdown(boost, assist, granted);
            string tokensSpent = boost > 0
                ? $"spent {chosen.Threshold + boost} tokens ({chosen.Threshold} cost + {boost} boost)"
                : $"spent {chosen.Threshold} token{(chosen.Threshold == 1 ? "" : "s")}";
            string rollDesc = $"rolled {DecisiveFace(castRoll)}, needed {threshold}+{breakdown}; {tokensSpent}";

            if (!success)
            {
                await GameContext.Announce($"{caster.Name} failed to cast {chosen.Name}: {rollDesc}.", CastFailColor);
                await OnFinished.Activate(context);
                return;
            }

            await GameContext.Announce($"{caster.Name} cast {chosen.Name}: {rollDesc}.", CastSuccessColor);

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
            GameContext.Log($"{chosen.Name} affected {targets.Count} unit(s).");
            await OnFinished.Activate(context);
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
            foreach (DataBinding<UnitData> target in targets)
            {
                UnitData targetUnit = target.GetValue();
                MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                    GameContext, targetUnit, HeroStatRules.GetMoraleQuality(targetUnit));

                if (outcome.Passed)
                {
                    GameContext.Log($"{targetUnit.Name} passed {spell.Name}'s morale test - no effect.");
                    continue;
                }

                GameContext.Log($"{targetUnit.Name} failed {spell.Name}'s morale test.");
                await ApplyEffectToTarget(caster, conditional.OnFailure, target);
            }
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
            DataBinding<UnitData> caster, PlayerID player, int tokens)
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
                else if (!SpellTargeting.HasAnyEligibleTarget(GameContext, caster, player, spell.Target))
                {
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
            PlayerID player, IReadOnlyList<SpellOffer> offer, int tokens)
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

            ChooseSpellRequest request = new ChooseSpellRequest(player, casterBinding, tokens,
                CAST_SUCCESS_THRESHOLD, hinderTokens, options);

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

        // "(base 4+, self +1, assists -2, granted -1)" — only the parts that apply; empty when the roll
        // is unmodified. "granted" is the #197 P6 token delta (Casting Debuff / Casting Buff).
        private static string BuildRollBreakdown(int boost, int assist, int granted)
        {
            if (boost == 0 && assist == 0 && granted == 0) return "";
            List<string> parts = new List<string> { $"base {CAST_SUCCESS_THRESHOLD}+" };
            if (boost != 0) parts.Add($"self +{boost}");
            if (assist != 0) parts.Add($"assists {(assist > 0 ? "+" : "")}{assist}");
            if (granted != 0) parts.Add($"granted {(granted > 0 ? "+" : "")}{granted}");
            return $" ({string.Join(", ", parts)})";
        }

        private async Task<IReadOnlyList<DataBinding<UnitData>>> PickTargets(PlayerID player, RuntimeSpell spell,
            List<DataBinding<UnitData>> candidates)
        {
            List<DataBinding<UnitData>> chosen = new List<DataBinding<UnitData>>();
            List<DataBinding<UnitData>> remaining = new List<DataBinding<UnitData>>(candidates);

            for (int picked = 0; picked < spell.Target.MaxCount && remaining.Count > 0; picked++)
            {
                List<SelectionRequest<UnitData>.ValidOption> validOptions = remaining
                    .Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name))
                    .ToList();

                SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(player,
                    $"Choose target for {spell.Name} ({chosen.Count + 1} of up to {spell.Target.MaxCount})",
                    validOptions, System.Array.Empty<SelectionRequest<UnitData>.InvalidOption>(),
                    allowCancel: true);

                DataBinding<UnitData> target = await GameContext.PlayerRequester
                    .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);

                if (target == null)
                {
                    // Cancel stops target selection: proceed with what's chosen if the minimum is met,
                    // otherwise the caller treats it as cancelling the cast (nothing spent).
                    break;
                }

                chosen.Add(target);
                remaining.RemoveAll(u => u.Reference.Equals(target.Reference));
            }

            return chosen.Count >= spell.Target.MinCount ? chosen : new List<DataBinding<UnitData>>();
        }

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
                validOptions, System.Array.Empty<SelectionRequest<ModelData>.InvalidOption>(), allowCancel: false);

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<ModelData>, DataBinding<ModelData>>(request);
        }

        // #103 — offer every eligible Caster within 18" the chance to spend tokens on this cast, sum the
        // result into a net roll modifier (friendly +1/token, enemy -1/token) and spend the tokens. Friendly
        // helpers declare first — the casting side commits support, then the enemy responds. Tokens are spent
        // whether or not the cast then succeeds, matching the cast cost. Returns 0 when no one assists, so a
        // game with no nearby Casters sees no prompts and no behaviour change.
        private async Task<int> CollectCastAssist(DataBinding<UnitData> casterBinding, PlayerID casterPlayer,
            string spellName)
        {
            string casterName = casterBinding.GetValue().Name;
            int net = 0;
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

                if (loans.Count > 0)
                {
                    GameContext.Log($"{assister.Name} funds that with borrowed accumulator tokens " +
                                    $"({SpellPurse.Describe(loans)}).");
                }

                // Text beat: announce who assisted/hindered and by how much — an on-screen banner plus the
                // log line, blue for a friendly boost and orange for an enemy disruption (matches the GUI
                // highlight). Only fires when a Caster actually spends (declines skip via the guard above).
                await GameContext.Announce(
                    $"{assister.Name} {(friendly ? "assists" : "hinders")} {casterName}'s cast of {spellName} " +
                    $"({(friendly ? "+" : "-")}{spent}).",
                    friendly ? AssistBannerColor : HinderBannerColor);
            }
            return net;
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
