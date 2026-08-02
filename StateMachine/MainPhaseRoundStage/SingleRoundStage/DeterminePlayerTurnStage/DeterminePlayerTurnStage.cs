using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class DeterminePlayerTurnStage : StageBase<ISingleRoundContext>
    {
        public StageBinding OnDeterminedPlayerTurn;
        public StageBinding OnNoPlayersLeft;

        // The player whose "<name>'s Activation" banner we last showed, so we don't repeat it when the
        // same player activates twice in a row (e.g. a Martial Prowess reactivation, or an opponent with
        // nothing left to activate).
        private PlayerID? _lastAnnouncedPlayer;

        public DeterminePlayerTurnStage(IGameContext gameContext, IStateMachineLayer<ISingleRoundContext> parent) : base(gameContext, parent)
        {
            OnDeterminedPlayerTurn = new StageBinding(this);
            OnNoPlayersLeft = new StageBinding(this);
        }

        public override async Task Enter(ISingleRoundContext context)
        {
            //NOTE: See DetermineNextDeployPlayerStage for code that iterates through teams and players to see who should deploy next.
            //Might be able to move that code to a utility somehow, though differences exist.

            context.LogDebug("Entering Determine Next Player Turn stage.");

            // Rolling save point (#052): snapshot the flow state at the start of each activation
            // cycle, before the next unit is chosen and before it is marked activated. A load taken
            // at any moment resumes from the most recent snapshot, re-playing the activation that was
            // in progress. Guarded so minimal-store unit tests (no GameProgressData type) are unaffected.
            if (GameContext.GameDataStore.IsTypeAssigned<GameProgressData>())
            {
                GameProgressUtilities.WriteProgress(
                    GameContext.GameDataStore,
                    GameProgressUtilities.Capture(context, GameContext.Settings, EResumeStage.MainPhase));
            }

            // #197 P19 turn-order override: a unit flagged to take the next activation displaces the normal
            // alternation, and the cursor goes to ITS OWNER - who may be an ally, so that player resolves
            // the activation with their own controller rather than the granting player driving it.
            PlayerID? nextPlayerID = FindPlayerWithUnitActivatingNext(context);
            if (nextPlayerID != null)
            {
                context.Cursor.PointAt(nextPlayerID.Value);
            }
            else if (context.TryAdvanceToNextPlayer(out ITeam? _, out nextPlayerID) == false)
            {
                context.Log("No players left to activate. Ending round.");
                await OnNoPlayersLeft.Activate(context);
                return;
            }

            if (_lastAnnouncedPlayer != nextPlayerID.Value)
            {
                _lastAnnouncedPlayer = nextPlayerID.Value;
                await context.Announce($"{context.GetPlayerName(nextPlayerID.Value)}'s Activation",
                    new TextColor(130, 220, 130, 255));
            }

            // #042 Martial Prowess: before the active player picks their next unit, offer a second
            // activation to any of their already-activated units bearing the rule. Accepting re-adds the
            // unit to the round's unactivated pool so it appears as a choice in ChooseUnitToActivateStage.
            await OfferReactivations(context, nextPlayerID!.Value);

            await OnDeterminedPlayerTurn.Activate(context);
        }

        /// <summary>
        /// #197 P19: the owner of a living, on-table unit that is flagged to take the next activation, or
        /// null when nothing is flagged and the alternation should proceed normally.
        ///
        /// <para>The POOL stays authoritative: a flag is honoured only for a unit that is genuinely still
        /// unactivated, so a marker left behind by a unit that died, was removed from the table, or somehow
        /// already activated degrades to "no override" instead of stalling the round on a unit that can
        /// never be picked. That is also what makes the marker safe to read after a resume.</para>
        /// </summary>
        private static PlayerID? FindPlayerWithUnitActivatingNext(ISingleRoundContext context)
        {
            foreach (KeyValuePair<PlayerID, List<DataBinding<UnitData>>> pool in context.UnactivatedUnits)
            {
                foreach (DataBinding<UnitData> unit in pool.Value)
                {
                    UnitData value = unit.GetValue();
                    if (value.GetIsAlive() && value.GetIsOnBattlefield()
                        && value.Tokens.HasToken(TokenType.ActivatesNext))
                    {
                        return pool.Key;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Fires <see cref="Rules.Foundation.EHookID.Activation_OnNextActivatorRequested"/> for each of
        /// the active player's already-activated, on-table units and offers any activated ability gathered
        /// there (Martial Prowess's reactivation). The reactivate operation is a marker the stage applies
        /// directly — re-adding the unit to the pool — because the round's activation state isn't reachable
        /// through the <c>IOperationServices</c> seam (mirrors how DeferDeployment is applied stage-side).
        /// The once-per-game cost marker is granted here too, since the imperative-op executor only runs
        /// ExecutableOperations and never applies cost tokens.
        /// </summary>
        private async Task OfferReactivations(ISingleRoundContext context, PlayerID playerID)
        {
            // Candidates: living, on-table units of this player that have already activated this round
            // (i.e. are no longer in the unactivated pool). Snapshot up front — accepting an offer mutates
            // the pool.
            List<DataBinding<UnitData>> alreadyActivated = GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(army => army.IsOwnedBy(playerID))
                .SelectMany(army => army.UnitBindings)
                .Where(unit => unit.GetValue().GetIsAlive()
                    && unit.GetValue().GetIsOnBattlefield()
                    && context.UnactivatedUnits[playerID].Contains(unit) == false)
                .ToList();

            foreach (DataBinding<UnitData> unit in alreadyActivated)
            {
                var hookContext = new NextActivatorRequestedContext(unit.GetValue());

                foreach (AbilityOffer offer in GameContext.RuleEvaluator.GatherOffers(hookContext))
                {
                    // #197 Inquisitorial Agent: "only up to one third of the units in the army with this
                    // rule at the beginning of the game (rounding up) may use it in a single round". The
                    // unit's own once-per-game gate is already paid by its Cost; this is the ARMY's cap,
                    // and it is checked before the player is asked so a full quota is silently unavailable
                    // rather than offered and then refused.
                    if (!ArmyRoundQuotaAllows(playerID, offer))
                    {
                        continue;
                    }

                    var question = new YesNoRequest(playerID,
                        $"Reactivate {unit.GetValue().Name} with {offer.RuleName}?",
                        defaultAnswer: true,
                        displayName: $"Deciding Whether to Reactivate {unit.GetValue().Name}");
                    bool accepted = await GameContext.PlayerRequester
                        .RequestDecision<YesNoRequest, bool>(question);

                    if (!accepted) continue;

                    GameContext.Log($"{unit.GetValue().Name} used {offer.RuleName} to activate again.");

                    IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                        .ResolveAbility(offer, new[] { (IUnit)unit.GetValue() });

                    await ApplyReactivationOps(context, unit, ops);
                }
            }
        }

        /// <summary>
        /// Applies the operation queue produced by an accepted reactivation: the shared token applier grants
        /// the cost marker the once-per-game gate reads, and the <see cref="RuleOperation.InvokeReactivate"/>
        /// marker re-adds the unit to the unactivated pool.
        /// </summary>
        private async Task ApplyReactivationOps(ISingleRoundContext context, DataBinding<UnitData> unit,
            IReadOnlyList<RuleOperation> ops)
        {
            OperationApplier.ApplyTokenOperations(ops);
            await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

            RuleOperation.InvokeReactivate? reactivate =
                ops.OfType<RuleOperation.InvokeReactivate>().FirstOrDefault();
            if (reactivate == null)
            {
                return;
            }

            // #197: "stops being fatigued when activated for the second time" - cleared BEFORE the unit
            // re-enters the pool, so the melee it is about to fight sees a fresh unit. Martial Prowess
            // carries no such clause and leaves the token alone.
            if (reactivate.ClearsFatigue)
            {
                unit.GetValue().Tokens.RemoveTokens(TokenType.Fatigued);
                GameContext.Log($"{unit.GetValue().Name} stops being fatigued for its second activation.");
            }

            // The army-wide per-round counter. Stamped on acceptance rather than on the offer, so a
            // declined offer costs the army nothing. Round-end cleared: the cap is per round, while the
            // unit's own once-per-game marker (granted by the Cost) is permanent.
            unit.GetValue().Tokens.AddToken(
                TokenDefinitionCatalog.Create(TokenType.ReactivatedThisRound));

            context.ReinstateUnitForActivation(unit);
        }

        /// <summary>
        /// #197 Inquisitorial Agent's army-wide cap: at most ceil(roster / divisor) units may take a second
        /// activation from this rule in one round, where roster is the number of units in the army carrying
        /// the rule AT THE BEGINNING OF THE GAME.
        /// <para>
        /// That game-start count needs no snapshot: <c>ArmyData.UnitBindings</c> is append-only — a
        /// destroyed unit stays in the list, marked not-alive — so counting every binding whose rules carry
        /// the offered ability yields exactly the starting roster, casualties included. (A rule that CREATES
        /// units mid-game could inflate it; no book pairs one with this rule, and the shipped-data test
        /// pins that.) Matching is on the ability itself rather than a rule name, so an army-flavored
        /// rename counts the same way.
        /// </para>
        /// Abilities with no declared quota (Martial Prowess) are always allowed.
        /// </summary>
        private bool ArmyRoundQuotaAllows(PlayerID playerID, AbilityOffer offer)
        {
            if (offer.Ability.Effect is not Effect.Reactivate { ArmyRoundQuotaDivisor: > 0 } reactivate)
            {
                return true;
            }

            List<UnitData> armyUnits = GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(army => army.IsOwnedBy(playerID))
                .SelectMany(army => army.UnitBindings)
                .Select(binding => binding.GetValue())
                .ToList();

            int roster = armyUnits.Count(unit => unit.RuleDefinitions
                .Any(rule => rule.Definition.Activated.Contains(offer.Ability)));
            int usedThisRound = armyUnits.Count(unit => unit.Tokens.HasToken(TokenType.ReactivatedThisRound));

            int allowed = (roster + reactivate.ArmyRoundQuotaDivisor - 1) / reactivate.ArmyRoundQuotaDivisor;
            if (usedThisRound < allowed)
            {
                return true;
            }

            GameContext.LogDebug($"{offer.RuleName}: {usedThisRound} of {roster} units have already used a " +
                $"second activation this round (limit {allowed}) - not offered.");
            return false;
        }
    }
}
