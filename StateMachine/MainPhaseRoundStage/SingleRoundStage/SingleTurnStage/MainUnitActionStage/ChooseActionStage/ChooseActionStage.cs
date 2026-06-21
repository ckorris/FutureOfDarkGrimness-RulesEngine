using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FDG.Stages
{

    public class ChooseActionStage : StageBase<IUnitActionContext>
    {
        public StageBinding ToMovement;
        public StageBinding ToCharge;
        public StageBinding ToShoot;
        public StageBinding ToCustomAction;
        public StageBinding ToDisembark;
        public StageBinding ToEmbark;
        public StageBinding ToReconcileEndOfActivation;

        public const string MOVEMENT_CHOICE_NAME = "Move";
        public const string CHARGE_CHOICE_NAME = "Charge";
        public const string SHOOT_CHOICE_NAME = "Shoot";
        public const string PASS_CHOICE_NAME = "Pass";

        public ChooseActionStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            ToMovement = new StageBinding(this);
            ToCharge = new StageBinding(this);
            ToShoot = new StageBinding(this);
            ToCustomAction = new StageBinding(this);
            ToDisembark = new StageBinding(this);
            ToEmbark = new StageBinding(this);
            ToReconcileEndOfActivation = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {

            GameContext.Log("Entered Choose Action.");

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
                GameContext.Log($"{activatingUnit.Name} is Shaken — staying idle this activation and recovering.");
                await ToReconcileEndOfActivation.Activate(context);
                return;
            }

            //Note that in the future, this should get optional actions somehow, like spellcasting.

            bool canMove = GetCanMove(context, out string cantMoveReason);
            bool canCharge = GetCanCharge(context, out string cantChargeReason);
            bool canShoot = GetCanShoot(context, out string cantShootReason);
            bool canPass = GetCanPass(GameContext, context, out string cantPassReason);

            // #010 — special rules contribute custom actions (e.g. a Caster's spell) by carrying an
            // ActivatedAbility that triggers at this hook. GatherOffers returns one offer per affordable,
            // available such ability; each surfaces below as its own action.
            IReadOnlyList<AbilityOffer> customActionOffers = GameContext.RuleEvaluator
                .GatherOffers(new ActionChoiceContext(context.ActivatingUnit.GetValue()));
            bool hasCustomActionsAvailable = customActionOffers.Count > 0;

            //If we have no available actions 
            if ((canMove || canCharge || canShoot || hasCustomActionsAvailable) == false)
            {
                GameContext.Log($"No more available actions left in {nameof(ChooseActionStage)}. Passing.");
                await ToReconcileEndOfActivation.Activate(context);
                return;
            }

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

            // #010 custom actions: one option per offer, labelled by the rule. Choosing it stashes the
            // offer on the context and routes to CustomActionStage, which resolves it and loops back here
            // WITHOUT setting HasMoved/HasAttacked — casting is a layered overlay, not an action that ends
            // the turn. A once-per-activation cost keeps it from being re-offered after use. A custom action
            // whose name collides with a built-in choice (or another custom action) is skipped and logged
            // rather than overwriting the outcome map.
            foreach (AbilityOffer offer in customActionOffers)
            {
                // #035 — Disembark is a custom action whose effect is movement (place within 6" of the
                // transport + un-embark), not a token-op, so it routes to DisembarkStage rather than the
                // generic CustomActionStage. Its name is engine-controlled (never collides) and it needs no
                // pending-offer stash — DisembarkStage reads the embarked token directly.
                if (offer.RuleName == CoreRuleCatalog.DisembarkRuleName)
                {
                    if (!outcomes.ContainsKey(offer.RuleName))
                    {
                        validOptions.Add(offer.RuleName);
                        outcomes.Add(offer.RuleName, () => ToDisembark.Activate(context));
                    }
                    continue;
                }

                // #035 slice D — Embark is offered only when it's still a move action (the unit hasn't moved
                // or attacked) AND an engine spatial check finds a friendly transport with room within
                // move-range (the availability gate can't be a data condition). Routed to EmbarkStage.
                if (offer.RuleName == CoreRuleCatalog.EmbarkRuleName)
                {
                    bool canEmbark = !context.HasMoved && !context.HasAttacked
                        && EmbarkStage.GetEmbarkableTransports(GameContext, context.ActivatingUnit.GetValue()).Count > 0;

                    if (canEmbark && !outcomes.ContainsKey(offer.RuleName))
                    {
                        validOptions.Add(offer.RuleName);
                        outcomes.Add(offer.RuleName, () => ToEmbark.Activate(context));
                    }
                    continue;
                }

                bool collides = offer.RuleName == MOVEMENT_CHOICE_NAME
                    || offer.RuleName == CHARGE_CHOICE_NAME
                    || offer.RuleName == SHOOT_CHOICE_NAME
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

            StringSelectionRequest request = new StringSelectionRequest(context.ActivatingPlayer(), "Choose Action", validOptions, invalidOptions);

            string choice = await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            
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
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} is embarked — must disembark first.";
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
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} is embarked — must disembark first.";
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

            bool anyInRange = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID))
                .SelectMany(a => a.UnitBindings)
                .Any(enemyUnit => UnitCompareUtilities.MinDistanceBetweenUnits(
                    context.ActivatingUnit.GetValue(), enemyUnit.GetValue(),
                    out _, out _, includeVertical: false)
                    <= GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL);

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
            //If the unit moved further than its Rush distance, the move was only legal
            //because it ended in melee — so it must follow through and engage. Pass is gated off.
            MovementContextPrecursor precursor = MovementContextPrecursor.GetDefault(gameContext);

            if(context.MoveDistance > precursor.MaxRushDistance + 0.0001f)
            {
                reasonIfCant = $"Moved {context.MoveDistance:F2}\" — beyond Rush range; must engage in melee.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        private bool GetCanShoot(IUnitActionContext context, out string reasonIfCant)
        {
            if (TransportUtilities.IsEmbarked(context.ActivatingUnit.GetValue()))
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} is embarked — must disembark first.";
                return false;
            }

            if (context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            context.ActivatingUnit.GetValue().GetMobility(out float moveShootDistanceInches, out _);

            if (context.MoveDistance.LessThanOrAlmostEqual(moveShootDistanceInches) == false)
            {
                reasonIfCant = $"Moved {context.MoveDistance} inches, when max to move and shoot for {context.ActivatingUnit.GetValue().Name} " + 
                    $" is {moveShootDistanceInches}.";
                return false;
            }

            if (context.ActivatingUnit.GetValue().GetRangedWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has no ranged weapons.";
                return false;
            }

            if (!ChooseRangedAttackStage.HasAnyFireableTarget(context.ActivatingUnit, context.GameContext))
            {
                reasonIfCant = "No enemies in range or line of sight.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }


    }
}