using FDG.Data;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    // #331 — the #257 pooled tally, extracted out of VictoryCalculationStage so the front end can colour a
    // victory celebration by the winning side. VictoryCalculationStageTests already pins the end-of-game
    // contract through the stage; these pin the pieces the RENDERER leans on and the stage does not
    // exercise — the ordering, the roster (which becomes the colour list), and TopTeams' three outcomes.
    //
    // The reason this is shared rather than reimplemented: GameResult is host-side only and never crosses
    // the wire, so a client cannot be told who won. Objectives and player slots do replicate, so both sides
    // run this same code over the same state and cannot disagree.
    [TestFixture]
    public class TeamScoreTallyTests
    {
        private GameDataStore _store = null!;
        private readonly List<IPlayerSlotInfo> _slots = new();
        private readonly List<IObjective> _objectives = new();

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _slots.Clear();
            _objectives.Clear();
        }

        [Test]
        public void TeammatesPoolTheirObjectives()
        {
            PlayerID a1 = Slot("Alpha", slotID: 0, team: 1);
            PlayerID a2 = Slot("Bravo", slotID: 1, team: 1);
            PlayerID b = Slot("Charlie", slotID: 2, team: 2);
            Objective(a1);
            Objective(a2);
            Objective(b);
            Objective(b);

            IReadOnlyList<TeamScore> scores = TeamScoreTally.Build(_slots, _objectives);

            // Alpha and Bravo individually hold 1 each; pooled, team 1 matches team 2's 2. Equal scores
            // keep slot-registration order, so team 1 (slot 0) leads - deterministic, not dictionary order.
            Assert.That(scores.Select(s => (s.TeamNumber, s.ObjectiveCount)),
                Is.EqualTo(new[] { (1, 2), (2, 2) }));
        }

        [Test]
        public void ScoresComeBackHighestFirst()
        {
            PlayerID a = Slot("Alpha", slotID: 0, team: 1);
            PlayerID b = Slot("Bravo", slotID: 1, team: 2);
            Objective(b);
            Objective(a);
            Objective(a);
            Objective(a);

            IReadOnlyList<TeamScore> scores = TeamScoreTally.Build(_slots, _objectives);

            Assert.That(scores[0].TeamNumber, Is.EqualTo(1));
            Assert.That(scores[0].ObjectiveCount, Is.EqualTo(3));
        }

        [Test]
        public void TeamsHoldingNothingAreOmitted()
        {
            PlayerID a = Slot("Alpha", slotID: 0, team: 1);
            Slot("Bravo", slotID: 1, team: 2);
            Objective(a);

            IReadOnlyList<TeamScore> scores = TeamScoreTally.Build(_slots, _objectives);

            Assert.That(scores.Select(s => s.TeamNumber), Is.EqualTo(new[] { 1 }),
                "'who is winning' never wants a wall of zeroes.");
        }

        [Test]
        public void RosterIsEveryPlayerOnTheTeamInSlotOrder()
        {
            PlayerID a1 = Slot("Alpha", slotID: 0, team: 1);
            PlayerID a2 = Slot("Bravo", slotID: 1, team: 1);
            Slot("Charlie", slotID: 2, team: 2);
            Objective(a2); // only the SECOND teammate holds anything...

            IReadOnlyList<TeamScore> scores = TeamScoreTally.Build(_slots, _objectives);

            Assert.That(scores[0].Players, Is.EqualTo(new[] { a1, a2 }),
                "...but the roster is the whole team, so a celebration shows both members' colours.");
        }

        [Test]
        public void TopTeams_SingleLeader_IsTheOutrightWinner()
        {
            PlayerID a = Slot("Alpha", slotID: 0, team: 1);
            PlayerID b = Slot("Bravo", slotID: 1, team: 2);
            Objective(a);
            Objective(a);
            Objective(b);

            IReadOnlyList<TeamScore> top = TeamScoreTally.TopTeams(TeamScoreTally.Build(_slots, _objectives));

            Assert.That(top.Count, Is.EqualTo(1));
            Assert.That(top[0].TeamNumber, Is.EqualTo(1));
        }

        [Test]
        public void TopTeams_LevelAtTheTop_ReturnsEveryTiedTeam()
        {
            PlayerID a = Slot("Alpha", slotID: 0, team: 1);
            PlayerID b = Slot("Bravo", slotID: 1, team: 2);
            Objective(a);
            Objective(b);

            IReadOnlyList<TeamScore> top = TeamScoreTally.TopTeams(TeamScoreTally.Build(_slots, _objectives));

            Assert.That(top.Count, Is.EqualTo(2), "a tie hands back both, so a celebration can show both.");
        }

        [Test]
        public void TopTeams_NothingOwned_IsEmpty()
        {
            Slot("Alpha", slotID: 0, team: 1);
            Objective(owner: null);

            IReadOnlyList<TeamScore> top = TeamScoreTally.TopTeams(TeamScoreTally.Build(_slots, _objectives));

            Assert.That(top, Is.Empty, "0-0 has no leader to celebrate.");
        }

        [Test]
        public void OwnerWithNoSlot_GetsItsOwnTeamAndIsItsOwnRoster()
        {
            // The edge VictoryCalculationStage has always handled: an objective owner with no player slot
            // keeps a private bucket rather than merging into someone else's team.
            var ghost = new PlayerID(Guid.NewGuid());
            PlayerID a = Slot("Alpha", slotID: 0, team: 1);
            Objective(ghost);
            Objective(ghost);
            Objective(a);

            IReadOnlyList<TeamScore> top = TeamScoreTally.TopTeams(TeamScoreTally.Build(_slots, _objectives));

            Assert.That(top.Count, Is.EqualTo(1));
            Assert.That(top[0].ObjectiveCount, Is.EqualTo(2));
            Assert.That(top[0].Players, Is.EqualTo(new[] { ghost }),
                "the unslotted owner is its own one-player roster, so callers need no null branch.");
            Assert.That(top[0].TeamNumber, Is.LessThan(int.MinValue / 2),
                "pseudo-teams count down from int.MinValue so they cannot collide with a real team number.");
        }

        [Test]
        public void TwoUnslottedOwners_DoNotMergeIntoOneTeam()
        {
            var ghostA = new PlayerID(Guid.NewGuid());
            var ghostB = new PlayerID(Guid.NewGuid());
            Objective(ghostA);
            Objective(ghostB);

            IReadOnlyList<TeamScore> scores = TeamScoreTally.Build(_slots, _objectives);

            Assert.That(scores.Count, Is.EqualTo(2));
            Assert.That(scores.Select(s => s.TeamNumber).Distinct().Count(), Is.EqualTo(2));
        }

        // Helpers

        private PlayerID Slot(string name, int slotID, int team)
        {
            var playerID = new PlayerID(Guid.NewGuid());
            _slots.Add(new PlayerSlotInfo(playerID, slotID: slotID, teamNumber: team, name: name,
                isFilled: true));
            return playerID;
        }

        private void Objective(PlayerID? owner)
        {
            var objective = new ObjectiveData(new Position(0, 0), _store);
            _store.Create(objective);
            if (owner.HasValue) objective.SetOwner(owner.Value);
            _objectives.Add(objective);
        }
    }
}
