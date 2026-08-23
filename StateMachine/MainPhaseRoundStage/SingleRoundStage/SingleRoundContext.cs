

using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;

namespace FDG.Stages
{

    public interface ISingleRoundContext : IGameContextAccessor
    {
        public int RoundCount { get; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits { get; }

        public IReadOnlyList<ITeam> TeamActivateOrder { get; }

        public TeamPlayerAlternationCursor Cursor { get; }

        public int CurrentActivatingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam { get; }

        public IReadOnlyList<ITeam> CurrentRoundTeamFinishOrder { get; }

        public PlayerID GetCurrentPlayerID();

        public void MarkUnitAsActivated(DataBinding<UnitData> activatedUnit);

        public void ReinstateUnitForActivation(DataBinding<UnitData> unit);

        public void CleanDeadUnitsFromUnactivated();

        public bool TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID);

        public bool DoesAnyTeamHaveRemainingActivations();

        public bool DoesTeamHaveRemainingActivations(ITeam team);

        public bool DoesPlayerHaveRemainingActivations(PlayerID playerID);
    }

    public class SingleRoundContext : ISingleRoundContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits => _unactivatedUnits;


        public Dictionary<PlayerID, List<DataBinding<UnitData>>> _unactivatedUnits { get; }

        public int RoundCount { get; private set; }

        public IReadOnlyList<ITeam> TeamActivateOrder => Cursor.TeamOrder;

        public TeamPlayerAlternationCursor Cursor { get; }

        public int CurrentActivatingTeamIndex
        {
            get => Cursor.CurrentTeamIndex;
            set => Cursor.CurrentTeamIndex = value;
        }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam => Cursor.CurrentPlayerIndexPerTeam;

        public IReadOnlyList<ITeam> CurrentRoundTeamFinishOrder => _currentRoundTeamFinishOrder;

        private List<ITeam> _currentRoundTeamFinishOrder = new List<ITeam>();

        public SingleRoundContext(IGameContext gameContext, List<ITeam> teamOrder, int roundCount = 0)
        {
            GameContext = gameContext;
            RoundCount = roundCount;
            Cursor = new TeamPlayerAlternationCursor(teamOrder);

            // The round advances the cursor BEFORE every activation, including the round's first, so it
            // starts parked one step short of teamOrder[0] - the team that won the deployment roll-off in
            // round 1, and thereafter the one that finished activating first last round. Without this the
            // opening activation of every round went to whoever should have gone SECOND.
            Cursor.ParkBeforeFirstTurn();

            _unactivatedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();

            SetUnactivatedUnits();
        }

        // Restore constructor for save/load resume: rebuilds the round's in-progress state from a
        // snapshot (cursor position, finish order, and the already-grouped unactivated units) instead
        // of scanning armies fresh. Does NOT call SetUnactivatedUnits.
        public SingleRoundContext(
            IGameContext gameContext,
            List<ITeam> teamOrder,
            int roundCount,
            int currentTeamIndex,
            IReadOnlyDictionary<ITeam, int> currentPlayerIndexPerTeam,
            List<ITeam> currentRoundTeamFinishOrder,
            Dictionary<PlayerID, List<DataBinding<UnitData>>> unactivatedUnits)
        {
            GameContext = gameContext;
            RoundCount = roundCount;
            Cursor = new TeamPlayerAlternationCursor(teamOrder);
            Cursor.CurrentTeamIndex = currentTeamIndex;
            foreach (KeyValuePair<ITeam, int> kvp in currentPlayerIndexPerTeam)
            {
                Cursor.CurrentPlayerIndexPerTeam[kvp.Key] = kvp.Value;
            }

            _currentRoundTeamFinishOrder = currentRoundTeamFinishOrder;
            _unactivatedUnits = unactivatedUnits;
        }

        public PlayerID GetCurrentPlayerID() => Cursor.GetCurrentPlayerID();

        public void MarkUnitAsActivated(DataBinding<UnitData> activatedUnit)
        {
            PlayerID playerID = activatedUnit.GetValue().PlayerID;

            // Sweep BEFORE this unit leaves the pool. A team whose last unactivated unit was KILLED during
            // the activation that is ending here ran out of activations before this one did, and the finish
            // order means "who finished first" - so it has to be recorded ahead of the team activating now.
            RecordTeamsThatHaveRunOut();

            if (_unactivatedUnits[playerID].Remove(activatedUnit) == false)
            {
                throw new ArgumentOutOfRangeException($"Unit not found as unactivated when marking activated: {activatedUnit.GetValue().Name}");
            }

            // #197 P19: the same fact, on the unit. The pool stays authoritative - this is the readable
            // half, for the code that cannot see the round context (an ability's targeting deciding which
            // friendly units "haven't activated yet", including an ALLY's, which no per-player pool a turn
            // context carries would cover). Stamped and cleared in lockstep here and in
            // ReinstateUnitForActivation, the only two places the pool moves.
            activatedUnit.GetValue().Tokens.AddToken(
                TokenDefinitionCatalog.Create(TokenType.ActivatedThisRound));

            //If we've removed the last living unit from the list, clean that player's list just in case
            //there are dead units left in it.
            if (_unactivatedUnits[playerID].Where(unit => unit.GetValue().GetIsAlive()).Count() == 0)
            {
                _unactivatedUnits[playerID].Clear();
            }

            //If that player's team is now all done, mark the team as finished.
            RecordTeamsThatHaveRunOut();
        }

        /// <summary>
        /// Appends every team that has no activations left and is not already in the round's finish order.
        /// <para>
        /// The finish order is not just a record of who led: it becomes the next round's COMPLETE team
        /// list (<see cref="MainPhaseContext.OnEndOfRound"/> hands it straight to the next
        /// <see cref="TeamPlayerAlternationCursor"/>). It used to be written in exactly one place - the tail
        /// of <see cref="MarkUnitAsActivated"/> - so a team only counted as finished if it ACTIVATED its way
        /// to an empty pool. A team whose last unactivated unit was killed first never got there, dropped out
        /// of the next round's team order, and was then skipped for every remaining round of the game: the
        /// opponent took every activation while that player was never offered one.
        /// </para>
        /// <para>
        /// Running out is running out, however it happened, so this is swept at both seams that can empty a
        /// pool - an activation ending, and the cursor being asked who is next - rather than being tied to
        /// the one path that happens to activate.
        /// </para>
        /// </summary>
        private void RecordTeamsThatHaveRunOut()
        {
            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {
                if (_currentRoundTeamFinishOrder.Contains(team)) continue;
                if (DoesTeamHaveRemainingActivations(team)) continue;

                _currentRoundTeamFinishOrder.Add(team);
            }
        }

        /// <summary>
        /// Re-adds an already-activated unit to its player's unactivated pool so it can be chosen again
        /// this round (Martial Prowess reactivation). No-op if the unit is already pending. The unit must
        /// be back in the master pool before its next activation, or <see cref="MarkUnitAsActivated"/>
        /// would fail to find it on the way out.
        /// </summary>
        public void ReinstateUnitForActivation(DataBinding<UnitData> unit)
        {
            PlayerID playerID = unit.GetValue().PlayerID;

            if (_unactivatedUnits[playerID].Contains(unit) == false)
            {
                _unactivatedUnits[playerID].Add(unit);
            }

            // Back in the pool means "has not activated" again - the lockstep half of MarkUnitAsActivated.
            unit.GetValue().Tokens.RemoveTokens(TokenType.ActivatedThisRound);
        }

        public void CleanDeadUnitsFromUnactivated()
        {
            foreach(KeyValuePair<PlayerID, List<DataBinding<UnitData>>> kvp in _unactivatedUnits)
            {
                kvp.Value.RemoveAll(unit => unit.GetIsDead());
            }
        }

        public bool DoesAnyTeamHaveRemainingActivations()
        {
            AdoptMidRoundUnits();
            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {
                if (DoesTeamHaveRemainingActivations(team))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DoesTeamHaveRemainingActivations(ITeam team)
        {
            AdoptMidRoundUnits();
            foreach (PlayerID playerID in team.Players)
            {
                if (DoesPlayerHaveRemainingActivations(playerID))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DoesPlayerHaveRemainingActivations(PlayerID playerID)
        {
            AdoptMidRoundUnits();
            return _unactivatedUnits[playerID].Where(unit => unit.GetValue().GetIsAlive()).Count() > 0;
        }

        /// <summary>
        /// #197 P17: folds units created MID-ROUND (Spawn/Split — marked
        /// <see cref="Rules.Foundation.TokenType.JoinsRoundInProgress"/> by the creation service) into
        /// their owner's pool, so they may activate this round (owner-ruled 2026-07-28). Runs at this
        /// context's own query seams rather than at the creation site, because a creation can fire from
        /// code that cannot see the round context (the destruction seam a Split rides). Clears the
        /// marker; the round-start snapshot sweeps strays so nothing ever joins twice.
        /// </summary>
        private void AdoptMidRoundUnits()
        {
            foreach (ArmyData army in GameContext.GameDataStore().GetAllValues<ArmyData>())
            {
                foreach (DataBinding<UnitData> unitBinding in army.UnitBindings)
                {
                    UnitData unit = unitBinding.GetValue();
                    if (!unit.Tokens.HasToken(Rules.Foundation.TokenType.JoinsRoundInProgress))
                    {
                        continue;
                    }

                    unit.Tokens.RemoveTokens(Rules.Foundation.TokenType.JoinsRoundInProgress);
                    if (!unit.GetIsAlive())
                    {
                        continue;
                    }

                    if (_unactivatedUnits.TryGetValue(unit.PlayerID, out List<DataBinding<UnitData>>? pool)
                        && !pool.Contains(unitBinding))
                    {
                        pool.Add(unitBinding);
                    }
                }
            }
        }

        private void SetUnactivatedUnits()
        {
            IEnumerable<ArmyData> armies = GameContext.GameDataStore().GetAllValues<ArmyData>();

            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {
                foreach (PlayerID playerID in team.Players)
                {
                    List<DataBinding<UnitData>> playerUnits = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(playerID)))
                    {
                        // Reserve units (Ambush) that haven't arrived are alive but off-table — they
                        // don't activate until they're placed (from a later round's start). Embarked units
                        // (#035) are also off-table, but they DO activate — to disembark on their own turn —
                        // so they're admitted to the pool even though GetIsOnBattlefield is false.
                        playerUnits.AddRange(army.UnitBindings.Where(unit =>
                            unit.GetValue().GetIsAlive()
                            && (unit.GetValue().GetIsOnBattlefield() || TransportUtilities.IsEmbarked(unit.GetValue()))));

                        // #197 P17: this fresh scan already includes every living unit, so a leftover
                        // mid-round join marker grants nothing here — sweep it.
                        foreach (DataBinding<UnitData> unit in army.UnitBindings)
                        {
                            unit.GetValue().Tokens.RemoveTokens(
                                Rules.Foundation.TokenType.JoinsRoundInProgress);
                        }
                    }
                    _unactivatedUnits[playerID] = playerUnits;
                }
            }
        }

        public bool TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID)
        {
            // The round-end seam: when this returns false the round is over, and any team that quietly ran
            // out of units since the last activation has to be on the books before that happens.
            RecordTeamsThatHaveRunOut();

            return Cursor.TryAdvance(
                teamHasRemainingWork: DoesTeamHaveRemainingActivations,
                playerHasRemainingWork: DoesPlayerHaveRemainingActivations,
                out nextTeam,
                out nextPlayerID);
        }
    }
}
