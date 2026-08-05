using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FDG.Stages
{

    public class ChooseActionStage : StageBase<IUnitActionContext>
    {
        // The amber the Shaken banners use (MoraleUtilities' / SpilloutExecutor's copies are private).
        private static readonly TextColor ShakenRecoveryBannerColor = new TextColor(255, 170, 60, 255);

        public StageBinding ToMovement;
        public StageBinding ToCharge;
        public StageBinding ToShoot;
        public StageBinding ToCast;
        public StageBinding ToCustomAction;
        public StageBinding ToDisembark;
        public StageBinding ToEmbark;
        public StageBinding ToTeleport;
        public StageBinding ToStorm;
        public StageBinding ToBeforeAttackAction;
        public StageBinding ToReconcileEndOfActivation;

        // #248: un-pick the activated unit and return to unit selection. Offered (AllowCancel) only while
        // the activation is pristine — nothing moved, attacked, or otherwise committed — so re-activating
        // this unit later replays ActivationStartStage against unchanged state.
        public StageBinding ToBackOut;

        public const string MOVEMENT_CHOICE_NAME = "Move";
        public const string CHARGE_CHOICE_NAME = "Charge";
        public const string SHOOT_CHOICE_NAME = "Shoot";
        public const string CAST_CHOICE_NAME = "Cast";
        public const string PASS_CHOICE_NAME = "Pass";

        public ChooseActionStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            ToMovement = new StageBinding(this);
            ToCharge = new StageBinding(this);
            ToShoot = new StageBinding(this);
            ToCast = new StageBinding(this);
            ToCustomAction = new StageBinding(this);
            ToDisembark = new StageBinding(this);
            ToEmbark = new StageBinding(this);
            ToTeleport = new StageBinding(this);
            ToStorm = new StageBinding(this);
            ToBeforeAttackAction = new StageBinding(this);
            ToReconcileEndOfActivation = new StageBinding(this);
            ToBackOut = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            // #203: Choose Action is the hub every action loop passes through (shoot -> back, cast ->
            // back, layered actions...), so a yield here bounds the stack WITHIN an activation too - a
            // buggy action ping-pong (#200's livelock) then idles at constant depth until the watchdog
            // reports it, instead of killing the whole process.
            await Task.Yield();

            GameContext.LogDebug("Entered Choose Action.");

            // A unit that began its activation Shaken must stay idle for the whole activation and
            // recovers (the token clears) at the end of it. The decision turns on whether it was
            // Shaken when the activation *started* (snapshotted in UnitActionContext.Reset), not on
            // what it's done since: a unit that becomes Shaken mid-activation (charge then lose the
            // melee) keeps the token for its next activation. Clearing here is observably the same as
            // clearing at end of activation — nothing reads Shaken between now and end-of-turn, and
            // objectives reconcile at end of round. (#008)
            if (context.StartedActivationShaken)
            {
                IUnit activatingUnit = context.ActivatingUnit.GetValue();
                activatingUnit.Tokens.RemoveTokens(TokenType.Shaken);
                // #278: recovery gets a Toast (tier-2) banner, not just a log line — amber, matching the
                // Shaken banners it bookends. Announce logs the same text, so the log record is kept.
                await GameContext.Announce(
                    $"{activatingUnit.Name} is Shaken - stays idle this activation and recovers.",
                    ShakenRecoveryBannerColor, EBannerTier.Toast);
                await ToReconcileEndOfActivation.Activate(context);
                return;
            }

            //Note that in the future, this should get optional actions somehow, like spellcasting.

            bool canMove = GetCanMove(context, out string cantMoveReason);
            bool canCharge = GetCanCharge(context, out string cantChargeReason);
            bool canShoot = GetCanShoot(GameContext, context, out string cantShootReason);
            bool canPass = GetCanPass(GameContext, context, out string cantPassReason);

            // #033 — a unit with Caster(X) gets a "Cast" action whenever its army has an affordable spell.
            // Like custom actions, casting is layered (it doesn't end the turn), so moving does not consume
            // it — but #234 gates it on HasAttacked, per Caster(X)'s "at any point before attacking"
            // (GF v3.5.1). Only shown for casters (no point graying it out for every non-caster).
            bool isCaster = SpellTargeting.IsCaster(GameContext, context.ActivatingUnit.GetValue());
            string cantCastReason = null;
            bool canCast = isCaster && GetCanCast(context, out cantCastReason);

            // #100 — RestrictActions (Immobile, Artillery's Hold-only facet): a passive rule at this hook may
            // limit which action types the unit can declare. The Move menu option covers Advance/Rush, Charge
            // covers Charge; shooting is a sub-step of Hold and isn't gated here. A unit restricted to [Hold]
            // (Immobile) thus loses Move and Charge but may still Hold-and-shoot.
            IReadOnlySet<EActionType>? allowedActions = CollectAllowedActions(context);
            if (allowedActions != null)
            {
                if (canMove && !allowedActions.Contains(EActionType.Advance) && !allowedActions.Contains(EActionType.Rush))
                {
                    canMove = false;
                    cantMoveReason = "Immobile.";
                }

                if (canCharge && !allowedActions.Contains(EActionType.Charge))
                {
                    canCharge = false;
                    cantChargeReason = "Immobile.";
                }
            }

            // #197 Instinctive — "WHEN THIS MODEL IS ACTIVATED, if it is able to shoot/charge an enemy
            // unit, then it must immediately attack the closest valid target". The condition is read ONCE,
            // on this stage's first visit of the activation (which is the moment of activation: the only
            // stages before it are ActivationStart and the Surprise Attack burst). If the unit could not
            // attack then, it is unconstrained for the whole activation - it moves and attacks normally,
            // even if the move brings an enemy into range. That is the owner's rules clarification of
            // 2026-07-31, and it is why the decision is STAMPED rather than re-derived: re-checking each
            // menu visit would compel a unit the rule never touched.
            //
            // While compelled: Move, Pass, Cast and every ability action drop out until the attack is
            // taken; BOTH attack kinds stay offered when both apply - the rule compels the target, not
            // which kind of attack. Once the unit has attacked, canShoot/canCharge are false (their gates
            // read HasAttacked) so the menu is normal again. The target half - WHICH enemy - lives in the
            // choosers, narrowed before the request is issued so the AI complies by construction (P20).
            IUnit compelCandidate = context.ActivatingUnit.GetValue();
            if (!context.HasMoved && !context.HasAttacked
                && !compelCandidate.Tokens.HasToken(TokenType.CompelledToAttack)
                && (canShoot || canCharge)
                && Rules.Dispatch.CapabilityRuleQueries.MustAttackClosestSource(
                       compelCandidate, GameContext.RuleEvaluator) != null)
            {
                // Cleared at end of activation; its presence is what the choosers and the authored +1
                // rider both read, so a unit that merely HAS the rule gets neither.
                compelCandidate.Tokens.AddToken(new Rules.Tokens.Token(TokenType.CompelledToAttack, 1,
                    new TokenClearTrigger.ActivationEnd()));
            }

            string? compelSource = compelCandidate.Tokens.HasToken(TokenType.CompelledToAttack)
                ? Rules.Dispatch.CapabilityRuleQueries.MustAttackClosestSource(
                    compelCandidate, GameContext.RuleEvaluator)
                : null;
            bool compelledToAttackNow = compelSource != null && (canShoot || canCharge);
            if (compelledToAttackNow)
            {
                string mustAttackReason = $"{compelSource} - must attack the closest target first.";
                if (canMove) { canMove = false; cantMoveReason = mustAttackReason; }
                if (canPass) { canPass = false; cantPassReason = mustAttackReason; }
                if (canCast) { canCast = false; cantCastReason = mustAttackReason; }
            }

            // #010 — special rules contribute custom actions (e.g. a Caster's spell) by carrying an
            // ActivatedAbility that triggers at this hook. GatherOffers returns one offer per affordable,
            // available such ability; each surfaces below as its own action. A unit compelled to attack
            // (#197 Instinctive, above) gets none of them - the menu is the attack, so the offers are not
            // gathered at all rather than greyed one by one.
            IReadOnlyList<AbilityOffer> customActionOffers = compelledToAttackNow
                ? Array.Empty<AbilityOffer>()
                : GameContext.RuleEvaluator.GatherOffers(
                    new ActionChoiceContext(context.ActivatingUnit.GetValue()));

            List<string> validOptions = new List<string>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();

            Dictionary<string, Func<Task>> outcomes = new Dictionary<string, Func<Task>>();

            if(canMove)
            {
                validOptions.Add(MOVEMENT_CHOICE_NAME);
                outcomes.Add(MOVEMENT_CHOICE_NAME, () => ToMovement.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(MOVEMENT_CHOICE_NAME, cantMoveReason));
            }

            if(canCharge)
            {
                validOptions.Add(CHARGE_CHOICE_NAME);
                outcomes.Add(CHARGE_CHOICE_NAME, () => ToCharge.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(CHARGE_CHOICE_NAME, cantChargeReason));
            }

            if(canShoot)
            {
                validOptions.Add(SHOOT_CHOICE_NAME);
                outcomes.Add(SHOOT_CHOICE_NAME, () => ToShoot.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(SHOOT_CHOICE_NAME, cantShootReason));
            }

            // #033 — Cast is only listed for casters (valid when an affordable spell exists, invalid with a
            // reason otherwise). Routes to CastSpellStage, which loops back here without ending the turn.
            if(isCaster)
            {
                if(canCast)
                {
                    validOptions.Add(CAST_CHOICE_NAME);
                    outcomes.Add(CAST_CHOICE_NAME, () => ToCast.Activate(context));
                }
                else
                {
                    invalidOptions.Add(new StringSelectionRequest.InvalidOption(CAST_CHOICE_NAME, cantCastReason));
                }
            }

            // #010 custom actions: one option per offer, labelled by the rule. Choosing it stashes the
            // offer on the context and routes to CustomActionStage, which resolves it and loops back here
            // WITHOUT setting HasMoved/HasAttacked — casting is a layered overlay, not an action that ends
            // the turn. A once-per-activation cost keeps it from being re-offered after use. A custom action
            // whose name collides with a built-in choice (or another custom action) is skipped and logged
            // rather than overwriting the outcome map.
            foreach (AbilityOffer offer in customActionOffers)
            {
                // "Cast" is only a reserved menu entry for casters (it isn't shown for anyone else), so a
                // non-caster custom action may still legitimately use that name; for a caster the first-class
                // Cast wins (it's already in outcomes, caught below).

                // #035 — Disembark is a custom action whose effect is movement (place within 6" of the
                // transport + un-embark), not a token-op, so it routes to DisembarkStage rather than the
                // generic CustomActionStage. It needs no pending-offer stash — DisembarkStage reads the
                // embarked token directly.
                //
                // Routed on the ability's EFFECT, not its rule NAME. Effect.Disembark exists for exactly
                // this purpose (a no-op marker the stage recognises), so matching the name instead was
                // reading the label off the thing while ignoring the thing — and it silently tied the
                // routing to one rule, when any rule authoring the effect should route the same way.
                if (offer.Ability.Effect is Effect.Disembark)
                {
                    if (!outcomes.ContainsKey(offer.RuleName))
                    {
                        validOptions.Add(offer.RuleName);
                        outcomes.Add(offer.RuleName, () => ToDisembark.Activate(context));
                    }
                    continue;
                }

                // #035 slice D / #097 — Embark's availability is spatial, so the gate is engine code rather
                // than a data condition. Boarding is the END of an entering move ("units may enter ... by
                // using any move action"), not a teleport: it is offered from contact only, and the approach
                // is an ordinary Move. So unlike every other action here, having MOVED does not disqualify
                // the unit — the move it just made IS the entering move. Attacking still does.
                //
                // A transport further off is listed GREYED with a "move up first" reason rather than
                // omitted, so the option stays discoverable from across the table: without it a player 5"
                // from their Rhino would see no Embark entry at all and no hint that walking over produces
                // one. The hint's reach is the unit's own Rush — the furthest it could close this activation.
                if (offer.Ability.Effect is Effect.Embark)
                {
                    if (context.HasAttacked || outcomes.ContainsKey(offer.RuleName)) continue;

                    UnitData self = context.ActivatingUnit.GetValue();

                    List<DataBinding<UnitData>> boardable = EmbarkStage.GetEmbarkableTransports(
                        GameContext, self, TransportUtilities.EmbarkContactDistanceInches);
                    if (boardable.Count > 0)
                    {
                        validOptions.Add(offer.RuleName);
                        outcomes.Add(offer.RuleName, () => ToEmbark.Activate(context));
                        continue;
                    }

                    List<DataBinding<UnitData>> withinReach = EmbarkStage.GetEmbarkableTransports(
                        GameContext, self,
                        MovementRuleQueries.EffectiveMaxRushDistance(self, GameContext.RuleEvaluator));
                    if (withinReach.Count > 0)
                    {
                        // Two different situations, and the difference matters to the player: one is an
                        // instruction, the other is an explanation. A unit that moved up and stopped an inch
                        // short must not just watch the entry disappear with no account of why.
                        string transportName = withinReach[0].GetValue().Name;
                        invalidOptions.Add(new StringSelectionRequest.InvalidOption(offer.RuleName,
                            context.HasMoved
                                ? $"Not in contact with {transportName}, and the move is spent."
                                : $"Move into contact with {transportName} first."));
                    }
                    continue;
                }

                // #197 Teleport — a book-listed rule (not engine-internal like Disembark/Embark) whose effect
                // is a placement, so it routes to TeleportStage rather than the token-op resolver. Available
                // "before attacking": gated on not-having-attacked here (its once-per-activation is enforced by
                // the ability's own cost). The offer is stashed like a generic custom action so TeleportStage
                // can resolve it to pay that cost. Fully layered - does not consume the move.
                if (offer.Ability.Effect is Effect.Teleport)
                {
                    if (!context.HasAttacked && !outcomes.ContainsKey(offer.RuleName))
                    {
                        AbilityOffer teleportOffer = offer;
                        validOptions.Add(offer.RuleName);
                        outcomes.Add(offer.RuleName, () =>
                        {
                            context.SetPendingCustomAction(teleportOffer);
                            return ToTeleport.Activate(context);
                        });
                    }
                    continue;
                }

                // #197 P10 Storm of X - a rolled multi-target hit burst. Routed to StormStage (like Teleport)
                // so its decisive pool roll, per-success target picks, and looping hit batches run as real
                // child pipelines. Detected by effect type (four rule-name variants share it). Available
                // "before attacking"; its once-per-game gate is the ability's own cost. Fully layered.
                if (offer.Ability.Effect is Effect.StormOfHits)
                {
                    if (!context.HasAttacked && !outcomes.ContainsKey(offer.RuleName))
                    {
                        AbilityOffer stormOffer = offer;
                        validOptions.Add(offer.RuleName);
                        outcomes.Add(offer.RuleName, () =>
                        {
                            context.SetPendingCustomAction(stormOffer);
                            return ToStorm.Activate(context);
                        });
                    }
                    continue;
                }

                bool collides = offer.RuleName == MOVEMENT_CHOICE_NAME
                    || offer.RuleName == CHARGE_CHOICE_NAME
                    || offer.RuleName == SHOOT_CHOICE_NAME
                    || (isCaster && offer.RuleName == CAST_CHOICE_NAME)
                    || offer.RuleName == PASS_CHOICE_NAME
                    || outcomes.ContainsKey(offer.RuleName);

                if (collides)
                {
                    GameContext.Log($"Custom action '{offer.RuleName}' collides with an existing action " +
                        $"name and was skipped.");
                    continue;
                }

                AbilityOffer capturedOffer = offer;
                validOptions.Add(offer.RuleName);
                outcomes.Add(offer.RuleName, () =>
                {
                    context.SetPendingCustomAction(capturedOffer);
                    return ToCustomAction.Activate(context);
                });
            }

            // "Before attacking" activated abilities (Piercing Spotter, Regeneration Buff, Precision Fighting
            // Mark, Mend, Breath Attack, ...): authored at Activation_OnBeforeAttackAction, each surfaces as
            // its OWN menu action - like Cast - so it can be used even when the unit cannot attack anything.
            // "Before attacking" means the window closes once the unit attacks: gated on !HasAttacked here
            // (moving does NOT close it). Only offered when the ability has enough eligible targets to fire
            // (a "pick an enemy" ability with no enemy in range isn't shown). Routes to
            // BeforeAttackActionStage, which resolves the one chosen ability and loops back here WITHOUT
            // setting HasMoved/HasAttacked (layered), so a used ability drops off via its own once-per-X cost.
            if (!context.HasAttacked && !compelledToAttackNow)
            {
                IReadOnlyList<AbilityOffer> beforeAttackOffers = GameContext.RuleEvaluator
                    .GatherOffers(new BeforeAttackActionContext(context.ActivatingUnit.GetValue()));

                foreach (AbilityOffer offer in beforeAttackOffers)
                {
                    if (AbilityTargeting.EligibleTargets(context.ActivatingUnit, offer.Ability.TargetSelector,
                            GameContext).Count < offer.Ability.TargetSelector.MinCount)
                    {
                        continue;
                    }

                    bool collides = offer.RuleName == MOVEMENT_CHOICE_NAME
                        || offer.RuleName == CHARGE_CHOICE_NAME
                        || offer.RuleName == SHOOT_CHOICE_NAME
                        || (isCaster && offer.RuleName == CAST_CHOICE_NAME)
                        || offer.RuleName == PASS_CHOICE_NAME
                        || outcomes.ContainsKey(offer.RuleName);

                    if (collides)
                    {
                        GameContext.Log($"Before-attack ability '{offer.RuleName}' collides with an existing " +
                            "action name and was skipped.");
                        continue;
                    }

                    AbilityOffer capturedOffer = offer;
                    validOptions.Add(offer.RuleName);
                    outcomes.Add(offer.RuleName, () =>
                    {
                        context.SetPendingCustomAction(capturedOffer);
                        return ToBeforeAttackAction.Activate(context);
                    });
                }
            }

            // If no real action survived the gating above, the only thing left would be Pass — there's no
            // decision to make, so end the activation instead of prompting with a lone Pass option. (Custom
            // actions like Embark are gathered with AvailableWhen=Always but gated above, so this can't be
            // decided up front from the raw offer count.)
            if (validOptions.Count == 0)
            {
                GameContext.Log($"No actions available for {context.ActivatingUnit.GetValue().Name} - passing.");
                await ToReconcileEndOfActivation.Activate(context);
                return;
            }

            //Add pass option.
            if(canPass)
            {
                validOptions.Add(PASS_CHOICE_NAME);
                outcomes.Add(PASS_CHOICE_NAME, () => ToReconcileEndOfActivation.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(PASS_CHOICE_NAME, cantPassReason));
            }

            // #248: while the activation is pristine, the menu is cancellable — a null reply un-picks the
            // unit and returns to unit selection. Deliberately NOT a listed option: the AI resolvers and
            // the CLI EOF default only ever pick real options, so an automated player can never loop the
            // turn by backing out forever.
            bool canBackOut = !context.HasMoved && !context.HasAttacked && !context.IrreversibleActionTaken;

            StringSelectionRequest request = new StringSelectionRequest(context.ActivatingPlayer(),
                "Choose Action", validOptions, invalidOptions, allowCancel: canBackOut,
                displayName: "Choosing an Action");

            string choice = await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);

            if (choice == null)
            {
                if (!canBackOut)
                {
                    throw new ArgumentException(
                        "Choose Action received a cancel reply, but the activation was not cancellable.");
                }

                GameContext.Log($"{context.ActivatingUnit.GetValue().Name}: activation backed out - " +
                    "choose a unit to activate.");
                await ToBackOut.Activate(context);
                return;
            }

            if(outcomes.ContainsKey(choice) == false)
            {
                throw new ArgumentException($"Request option was {choice}, but that wasn't an option.");
            }

            await outcomes[choice].Invoke();
        }


        private bool GetCanMove(IUnitActionContext context, out string reasonIfCant)
        {
            if (TransportUtilities.IsEmbarked(context.ActivatingUnit.GetValue()))
            {
                reasonIfCant = "Embarked; disembark first.";
                return false;
            }

            if (context.HasMoved == true)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has already moved.";
                return false;
            }

            if (context.HasAttacked == true)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has already attacked.";
                return false;
            }

            bool canMoveFromUnit = context.ActivatingUnit.GetValue().GetMobility(out _, out _);

            if (canMoveFromUnit == false)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} is immobile.";

                return false;
            }

            reasonIfCant = null;
            return true;
        }

        private bool GetCanCharge(IUnitActionContext context, out string reasonIfCant)
        {
            if (TransportUtilities.IsEmbarked(context.ActivatingUnit.GetValue()))
            {
                reasonIfCant = "Embarked; disembark first.";
                return false;
            }

            // #029: an Aircraft that flew off the table this activation is out of play — no charging from limbo.
            if (context.ActivatingUnit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.OffTableFromForcedMove))
            {
                reasonIfCant = "Flew off the table - it redeploys from an edge next round.";
                return false;
            }

            if (context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            if (context.ActivatingUnit.GetValue().GetMeleeWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has no melee weapons.";
                return false;
            }

            PlayerID attackingPlayer = context.ActivatingPlayer();
            TeamData? playerTeam = GameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(attackingPlayer));
            IReadOnlyList<PlayerID> alliedPlayers = playerTeam != null
                ? playerTeam.Players
                : new List<PlayerID> { attackingPlayer };

            // #022: melee range is the 2"-horizontal AND 4"-vertical cylinder (MeleeRangeUtilities),
            // not the horizontal half alone — so Charge grays out exactly when no enemy unit could be
            // selected as a defender, keeping this gate consistent with ChooseMeleeDefenderStage.
            // #029: an Aircraft can't be charged, so it isn't a valid charge target — exclude it here so Charge
            // grays out when the only in-range enemies are Aircraft (consistent with ChooseMeleeDefenderStage).
            bool anyInRange = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID))
                .SelectMany(a => a.UnitBindings)
                .Where(enemyUnit => !Rules.Dispatch.AircraftRules.IsAircraft(enemyUnit.GetValue()))
                .Any(enemyUnit => MeleeRangeUtilities.AreUnitsInMeleeRange(
                    context.ActivatingUnit.GetValue(), enemyUnit.GetValue()));

            if (!anyInRange)
            {
                reasonIfCant = "No enemies within melee range.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        public static bool GetCanPass(IGameContext gameContext, IUnitActionContext context, out string reasonIfCant)
        {
            //A unit that has already attacked has followed through on the engage obligation — the flow loops
            //back here after the melee (layered actions may remain), and the proximity gate below must not
            //re-fire. Without this, a caster that charged into contact would be locked out of Pass AFTER
            //winning its melee, forced to Cast until its spell tokens ran dry before the zero-options fallback.
            if (context.HasAttacked)
            {
                reasonIfCant = null;
                return true;
            }

            //#206 — forced-charge is a PROXIMITY obligation, not a distance-moved one, and it is enforced here
            //rather than at move time (the movement validator no longer rejects ending in the standoff band).
            //A unit standing within ENEMY_STANDOFF_DISTANCE_INCHES (base-to-base) of an enemy can't idle in its
            //face: Pass is gated so it must Charge (or reposition, e.g. Teleport, then re-evaluate). The "can
            //charge" band is wider (melee range, 2"), so a unit at 1"-2" may Charge but is NOT forced — it may
            //still Pass. Teleporting out past the standoff restores Pass; teleporting but staying inside it does
            //not. Measured over living models only, against non-allied units.
            IUnit unit = context.ActivatingUnit.GetValue();

            //#029 — an Aircraft MUST make its forced Advance every activation, so it can't Pass without moving.
            //On a fresh activation the only offered action is Move; Pass only becomes available once the forced
            //move is done (after which it may decline to shoot). HasAttacked already short-circuited above, so a
            //unit reaching here that has attacked has necessarily moved - this only gates the pristine aircraft.
            if (!context.HasMoved && AircraftRules.IsAircraft(unit))
            {
                reasonIfCant = "Aircraft must Advance - it cannot pass without moving.";
                return false;
            }

            //#334: the predicate itself lives in ForcedChargeUtilities so the movement resolvers can show the
            //player this obligation WHILE the move is being aimed, measuring exactly what this gate measures.
            if (ForcedChargeUtilities.AnyEnemyWithinStandoff(gameContext, context.ActivatingPlayer(), unit))
            {
                reasonIfCant = "Within 1\" of an enemy - must charge (or reposition) rather than stand idle.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        // #033 — true when the caster's army has at least one spell it can currently CAST: affordable AND
        // with a legal target (range/LoS/affinity). The target check matters — offering Cast for a spell
        // with no target would loop forever under a deterministic resolver (pick Cast -> no target -> back),
        // the same reason GetCanShoot gates on HasAnyFireableTarget.
        private bool GetCanCast(IUnitActionContext context, out string reasonIfCant)
        {
            IUnit unit = context.ActivatingUnit.GetValue();

            // #234 — Caster(X) is "at any point before attacking" (GF v3.5.1), so shooting or melee closes
            // the casting window for the rest of the activation. HasAttacked covers both (ShootStage and
            // MeleeStage are its only setters). Moving does not close it — casting brackets the move.
            if (context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            if (TransportUtilities.IsEmbarked(unit))
            {
                reasonIfCant = "Embarked; disembark first.";
                return false;
            }

            // The purse (own tokens + nearby friendly Spell Accumulators), so the Cast action is offered
            // whenever the unit could actually pay - CastSpellStage's picker prices spells the same way.
            int tokens = SpellPurse.Available(GameContext.TableState, GameContext.RuleEvaluator, unit);
            PlayerID player = context.ActivatingPlayer();

            ArmyData army = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .FirstOrDefault(a => a.PlayerID == player);

            if (army == null || army.Spells.Count == 0)
            {
                reasonIfCant = "No spells available.";
                return false;
            }

            // #249 — "only one try per spell": a spell already tried this activation is out of the running
            // for the rest of it. Filtered here as well as in CastSpellStage's picker because Cast must stop
            // being offered once nothing castable remains — otherwise picking it enters a stage that finds no
            // castable spell and loops straight back, the same no-progress cycle the target gate guards.
            List<RuntimeSpell> untried = army.Spells
                .Where(spell => spell.Threshold > 0 && !context.HasAttemptedSpell(spell.Name))
                .ToList();

            if (untried.Count == 0)
            {
                reasonIfCant = "Every spell has been tried this activation.";
                return false;
            }

            bool anyAffordable = untried.Any(spell => spell.Threshold <= tokens);
            if (!anyAffordable)
            {
                reasonIfCant = $"Not enough spell tokens ({tokens}).";
                return false;
            }

            // Reach is judged from every origin the cast could use (#197 P23 Spell Conduit), matching the
            // picker: a spell only a relay can reach still makes Cast a real option.
            IReadOnlyList<SpellRelay.CastOrigin> origins = SpellRelay.OriginsFor(
                GameContext.TableState, GameContext.RuleEvaluator, unit);
            bool anyCastable = untried.Any(spell => spell.Threshold <= tokens
                && SpellTargeting.HasAnyEligibleTargetFromAny(GameContext, context.ActivatingUnit, player,
                    spell.Target, origins));
            if (!anyCastable)
            {
                reasonIfCant = "No spell has a target in range.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        // Public + static like its sibling GetCanPass: the shoot gate is the whole of #197 P20's Quick
        // Shot, and a rushed unit with nothing else to do never reaches the menu (zero valid options
        // auto-pass), so there is no menu to read the answer out of.
        public static bool GetCanShoot(IGameContext gameContext, IUnitActionContext context,
            out string reasonIfCant)
        {
            if (TransportUtilities.IsEmbarked(context.ActivatingUnit.GetValue()))
            {
                reasonIfCant = "Embarked; disembark first.";
                return false;
            }

            // #029: an Aircraft that flew off the table this activation is out of play — flying off skips
            // its shooting (its models sit at origin until the edge redeploy next round).
            if (context.ActivatingUnit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.OffTableFromForcedMove))
            {
                reasonIfCant = "Flew off the table - it redeploys from an edge next round.";
                return false;
            }

            // #029: an Aircraft is forced to Advance every activation, and shooting comes AFTER the move
            // ("you can't move after you shoot"). So on a fresh activation the only offered action is Move -
            // Shoot stays unavailable until the forced Advance is done. Once it has moved this gate clears and
            // the normal target/range checks below decide whether it can shoot.
            if (!context.HasMoved && Rules.Dispatch.AircraftRules.IsAircraft(context.ActivatingUnit.GetValue()))
            {
                reasonIfCant = "Aircraft must Advance before it can shoot.";
                return false;
            }

            if (context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            // The advance-and-shoot cap must include the unit's movement rules (Fast/Quick/Rapid Advance/...)
            // so a unit that legally advanced past the base 6" isn't wrongly blocked from shooting.
            //
            // #290: the allowance is the one RECORDED when the move resolved, not a fresh query. Re-deriving
            // it here asks "what could this unit advance right now", which is a different question the moment
            // a one-shot (NextTrigger) granted movement rule is involved: ExecuteMoveStage spends the grant as
            // the move resolves, so a Slow unit that legally advanced 8" under a granted Rapid Advance was
            // measured against its un-granted 4" and told it had rushed. The move context is the only place
            // that knows the number the move was actually authorised against.
            //
            // Aircraft only ever Advance — a forced 30-36" straight-line move — and may shoot after it, so the
            // normal advance-and-shoot distance cap (which distinguishes Advance from Rush) doesn't apply (#029).
            bool isAircraft = Rules.Dispatch.AircraftRules.IsAircraft(context.ActivatingUnit.GetValue());
            bool overMoveShootCap = !isAircraft && context.HasMoved
                && context.MoveDistance.LessThanOrAlmostEqual(context.MoveShootAllowance) == false;

            // #197 P20 Quick Shot: "this model may shoot after using Rush actions" waives the cap outright.
            // Quick Shot MARK is the same permission bound to one enemy ("friendly units get Quick Shot
            // AGAINST it once"), so it does not waive the cap - it narrows the target list to marked units,
            // and the shot is only possible at all if one of those is fireable. markedTargetsOnly threads
            // that narrowing into the shared target-gating pipeline below, which is also what the shoot
            // STAGE runs (#200: the action gate and the stage must never disagree).
            bool markedTargetsOnly = overMoveShootCap
                && !Rules.Dispatch.ShootAfterRushRules.CanShootAfterRush(
                    context.ActivatingUnit.GetValue(), gameContext.RuleEvaluator);

            if (context.ActivatingUnit.GetValue().GetRangedWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has no ranged weapons.";
                return false;
            }

            if (!ChooseRangedAttackStage.HasAnyFireableTarget(context.ActivatingUnit, context.GameContext,
                    markedTargetsOnly))
            {
                if (markedTargetsOnly)
                {
                    reasonIfCant = $"Moved {context.MoveDistance} inches, when max to move and shoot for " +
                        $"{context.ActivatingUnit.GetValue().Name}  is {context.MoveShootAllowance}.";
                    return false;
                }
                reasonIfCant = "No enemies in range or line of sight.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        // The action types the unit may declare, per its RestrictActions rules firing at
        // Activation_OnActionChoice — intersected across rules (two restrictions both bind). Null when no
        // rule restricts (the common case), so the menu is unchanged.
        private IReadOnlySet<EActionType>? CollectAllowedActions(IUnitActionContext context)
        {
            IUnit unit = context.ActivatingUnit.GetValue();
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.EvaluateAll(
                new ActionChoiceContext(unit), RuleParticipant.Actor(unit));

            HashSet<EActionType>? allowed = null;
            foreach (RuleOperation op in ops)
            {
                if (op is RuleOperation.RestrictActions restrict)
                {
                    HashSet<EActionType> these = new HashSet<EActionType>(restrict.Allowed);
                    allowed = allowed == null
                        ? these
                        : new HashSet<EActionType>(allowed.Where(these.Contains));
                }
            }

            return allowed;
        }
    }
}