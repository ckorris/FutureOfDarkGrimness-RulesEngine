using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class MainPhaseContextTests
    {
        [Test]
        public void OnEndOfRound_EmptyFinishOrder_PreservesExistingOrder()
        {
            // Regression: prior to the fix, an empty finish order (i.e. a round in which
            // no team activated anything — every unit was already dead) overwrote the
            // team activation order, leaving the next round's SingleRoundContext with
            // an empty team list and crashing the first TryAdvanceToNextPlayer call.

            var teamA = new TeamData(1, new List<PlayerID> { new PlayerID(Guid.NewGuid()) });
            var teamB = new TeamData(2, new List<PlayerID> { new PlayerID(Guid.NewGuid()) });
            var initialOrder = new List<ITeam> { teamA, teamB };

            var ctx = new MainPhaseContext(gameContext: null!, firstDeploymentRollOrder: initialOrder);

            ctx.OnEndOfRound(new List<ITeam>());

            Assert.That(ctx.TeamActivateOrder, Is.Not.Null);
            Assert.That(ctx.TeamActivateOrder!.Count, Is.EqualTo(2));
            Assert.That(ctx.TeamActivateOrder, Is.EqualTo(initialOrder).AsCollection);
            Assert.That(ctx.RoundCount, Is.EqualTo(2), "RoundCount should still increment.");
        }

        [Test]
        public void OnEndOfRound_NonEmptyFinishOrder_ReplacesOrder()
        {
            var teamA = new TeamData(1, new List<PlayerID> { new PlayerID(Guid.NewGuid()) });
            var teamB = new TeamData(2, new List<PlayerID> { new PlayerID(Guid.NewGuid()) });
            var initialOrder = new List<ITeam> { teamA, teamB };

            var ctx = new MainPhaseContext(gameContext: null!, firstDeploymentRollOrder: initialOrder);

            // Team B finished first this round → next round leads with team B.
            ctx.OnEndOfRound(new List<ITeam> { teamB, teamA });

            Assert.That(ctx.TeamActivateOrder![0], Is.SameAs(teamB));
            Assert.That(ctx.TeamActivateOrder![1], Is.SameAs(teamA));
        }
    }
}
