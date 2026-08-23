using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Reproduction scaffold: a team whose last unactivated unit is KILLED (rather than activated) never
    // reaches MarkUnitAsActivated, so it is never appended to the round's finish order. The finish order
    // becomes the next round's COMPLETE team list, so that team drops out of the alternation cursor
    // entirely - for that round and every round after it.
    [TestFixture]
    public class RoundFinishOrderCompletenessTests
    {
        private GameDataStore _store = null!;
        private PlayerID _playerA;
        private PlayerID _playerB;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _playerA = new PlayerID(System.Guid.NewGuid());
            _playerB = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public void TeamWhoseLastUnitDiesBeforeActivating_StillReachesTheFinishOrder()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> a1 = MakeUnit(_playerA, "A1", new Position(5f, 5f));
            DataBinding<UnitData> a2 = MakeUnit(_playerA, "A2", new Position(9f, 5f));
            DataBinding<UnitData> b1 = MakeUnit(_playerB, "B1", new Position(25f, 5f));
            DataBinding<UnitData> b2 = MakeUnit(_playerB, "B2", new Position(29f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 3);

            round.TryAdvanceToNextPlayer(out _, out _);
            round.MarkUnitAsActivated(a1);
            round.TryAdvanceToNextPlayer(out _, out _);
            round.MarkUnitAsActivated(b1);

            // B's second activation wipes out A's last unactivated unit.
            Kill(a2);
            round.TryAdvanceToNextPlayer(out _, out _);
            round.MarkUnitAsActivated(b2);

            Assert.That(round.DoesAnyTeamHaveRemainingActivations(), Is.False,
                "precondition: the round is over - A's remaining pool holds only a corpse");
            Assert.That(round.CurrentRoundTeamFinishOrder, Does.Contain(teamA),
                "a team that ran out of units to activate has finished activating, however it ran out");
        }

        [Test]
        public void PartialFinishOrder_DoesNotDropATeamFromTheNextRound()
        {
            var teamA = new TeamData(1, new List<PlayerID> { _playerA });
            var teamB = new TeamData(2, new List<PlayerID> { _playerB });
            var initialOrder = new List<ITeam> { teamA, teamB };

            var ctx = new MainPhaseContext(gameContext: null!, firstDeploymentRollOrder: initialOrder);

            // Only team B was recorded as finishing.
            ctx.OnEndOfRound(new List<ITeam> { teamB });

            Assert.That(ctx.TeamActivateOrder, Does.Contain(teamA),
                "a team missing from the finish order must not vanish from the activation order");
            Assert.That(ctx.TeamActivateOrder![0], Is.SameAs(teamB), "the reported finisher still leads");
        }

        [Test]
        public void AfterATeamsLastUnitDies_ItStillActivatesNextRound()
        {
            // End to end: the symptom as reported - one side gets every activation in the round and the
            // other is skipped outright.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> a1 = MakeUnit(_playerA, "A1", new Position(5f, 5f));
            DataBinding<UnitData> a2 = MakeUnit(_playerA, "A2", new Position(9f, 5f));
            DataBinding<UnitData> b1 = MakeUnit(_playerB, "B1", new Position(25f, 5f));
            DataBinding<UnitData> b2 = MakeUnit(_playerB, "B2", new Position(29f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var mainPhase = new MainPhaseContext(ctx, new List<ITeam> { teamA, teamB });
            var round3 = new SingleRoundContext(ctx, mainPhase.TeamActivateOrder!, mainPhase.RoundCount);

            round3.TryAdvanceToNextPlayer(out _, out _);
            round3.MarkUnitAsActivated(a1);
            round3.TryAdvanceToNextPlayer(out _, out _);
            round3.MarkUnitAsActivated(b1);
            Kill(a2);
            round3.TryAdvanceToNextPlayer(out _, out _);
            round3.MarkUnitAsActivated(b2);

            mainPhase.OnEndOfRound(round3.CurrentRoundTeamFinishOrder);

            // Round 4: A still has a living unit (A1), B has both of its own.
            var round4 = new SingleRoundContext(ctx, mainPhase.TeamActivateOrder!, mainPhase.RoundCount);

            var activatingTeams = new List<ITeam>();
            while (round4.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player))
            {
                activatingTeams.Add(team!);
                DataBinding<UnitData> next = round4.UnactivatedUnits[player!.Value]
                    .First(unit => unit.GetValue().GetIsAlive());
                round4.MarkUnitAsActivated(next);
            }

            Assert.That(activatingTeams, Does.Contain(teamA),
                "team A has a living, on-table unit and must get to activate it");
        }

        // ── Fixture ──────────────────────────────────────────────────────────────────────────────────────

        private static void Kill(DataBinding<UnitData> unit)
        {
            foreach (IModel model in unit.GetValue().Models)
            {
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }
        }

        private ITeam MakeTeam(int number, params PlayerID[] players)
        {
            var team = new TeamData(number, players.ToList());
            _store.Create(team);
            return team;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Position position)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
