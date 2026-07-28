using System;
using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #299 "Alternating: Points" - the dealing, spend, debt and affordability rules behind the mode,
    // tested off the pure ledger (no stage, no requests). The affordability copy is asserted verbatim
    // where the design fixed it ("Playing this piece will take 1 point from your next turn").
    [TestFixture]
    public class TerrainPointsLedgerTests
    {
        private sealed class FakeTeam : ITeam
        {
            public int TeamNumber { get; }
            public IReadOnlyList<PlayerID> Players { get; }
            public FakeTeam(int teamNumber, params PlayerID[] players)
            {
                TeamNumber = teamNumber;
                Players = players;
            }
        }

        private static PlayerID NewPlayer() => new PlayerID(Guid.NewGuid());

        private static (TerrainPointsLedger ledger, PlayerID first, PlayerID second) TwoPlayerLedger(
            int total, int perTurn)
        {
            PlayerID first = NewPlayer();
            PlayerID second = NewPlayer();
            var teams = new List<ITeam> { new FakeTeam(1, first), new FakeTeam(2, second) };
            return (new TerrainPointsLedger(teams, total, perTurn), first, second);
        }

        [Test]
        public void Deal_TwentyTotalThreePerTurn_GivesElevenAndNine()
        {
            // The design's worked example: chunks of 3 alternate until 20 runs out (3,3,3,3,3,3,2),
            // so the roll-off winner gets 11 and the other player 9.
            var (ledger, first, second) = TwoPlayerLedger(total: 20, perTurn: 3);

            Assert.That(ledger.AllotmentOf(first), Is.EqualTo(11));
            Assert.That(ledger.AllotmentOf(second), Is.EqualTo(9));
        }

        [Test]
        public void Deal_DefaultThirtyTotal_SplitsEvenly()
        {
            var (ledger, first, second) = TwoPlayerLedger(total: 30, perTurn: 3);

            Assert.That(ledger.AllotmentOf(first), Is.EqualTo(15));
            Assert.That(ledger.AllotmentOf(second), Is.EqualTo(15));
        }

        [Test]
        public void Deal_TotalBelowOneChunk_AllGoesToTheFirstPlayer()
        {
            var (ledger, first, second) = TwoPlayerLedger(total: 2, perTurn: 3);

            Assert.That(ledger.AllotmentOf(first), Is.EqualTo(2));
            Assert.That(ledger.AllotmentOf(second), Is.EqualTo(0));
            Assert.That(ledger.HasPointsRemaining(second), Is.False,
                "a player dealt nothing never gets a turn");
        }

        [Test]
        public void Deal_TwoVsTwo_FollowsTheAlternationCursorExactly()
        {
            PlayerID a1 = NewPlayer(), a2 = NewPlayer(), b1 = NewPlayer(), b2 = NewPlayer();
            var teams = new List<ITeam> { new FakeTeam(1, a1, a2), new FakeTeam(2, b1, b2) };

            var ledger = new TerrainPointsLedger(teams, totalPoints: 20, pointsPerTurn: 3);

            // TeamPlayerAlternationCursor semantics: the very first placer is team 1's first player,
            // but every team visited via TryAdvance starts its round-robin at its SECOND listed
            // player. Visit order: a1, b2, a2, b1, a1, b2, a2(2). The ledger must deal in that exact
            // order because the live placement loop walks an identical cursor.
            Assert.That(ledger.AllotmentOf(a1), Is.EqualTo(6));
            Assert.That(ledger.AllotmentOf(b2), Is.EqualTo(6));
            Assert.That(ledger.AllotmentOf(a2), Is.EqualTo(5));
            Assert.That(ledger.AllotmentOf(b1), Is.EqualTo(3));
            Assert.That(new[] { a1, a2, b1, b2 }.Sum(ledger.AllotmentOf), Is.EqualTo(20));
        }

        [Test]
        public void Turn_WithinBudget_SpendsDownToZero()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 20, perTurn: 3);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(first);
            Assert.That(turn.BudgetRemaining, Is.EqualTo(3));

            TerrainPieceAffordability one = turn.Evaluate(1);
            Assert.That(one.Playable, Is.True);
            Assert.That(one.DebtIncurred, Is.Zero);
            Assert.That(one.WarningText, Is.Null);

            turn.RecordPlacement(1);
            turn.RecordPlacement(2);
            Assert.That(turn.BudgetRemaining, Is.Zero, "3 points spent - the turn is over");
            Assert.That(ledger.RemainingOf(first), Is.EqualTo(8));
            Assert.That(ledger.DebtOf(first), Is.Zero);
        }

        [Test]
        public void Turn_BudgetIsCappedByTheRemainingAllotment()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 2, perTurn: 3);

            Assert.That(ledger.BeginTurn(first).BudgetRemaining, Is.EqualTo(2),
                "the final partial chunk offers only what is left of the allotment");
        }

        [Test]
        public void Overspend_WarnsWithTheExactDesignCopy_ThenReducesTheNextTurn()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 20, perTurn: 3);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(first);
            TerrainPieceAffordability fourCost = turn.Evaluate(4);
            Assert.That(fourCost.Playable, Is.True);
            Assert.That(fourCost.DebtIncurred, Is.EqualTo(1));
            Assert.That(fourCost.WarningText,
                Is.EqualTo("Playing this piece will take 1 point from your next turn"));

            turn.RecordPlacement(4);
            Assert.That(turn.BudgetRemaining, Is.Zero, "an over-budget piece ends the turn");
            Assert.That(ledger.DebtOf(first), Is.EqualTo(1));

            TerrainPointsLedger.Turn next = ledger.BeginTurn(first);
            Assert.That(next.DebtPaidThisTurn, Is.EqualTo(1));
            Assert.That(next.BudgetRemaining, Is.EqualTo(2));
            Assert.That(next.Snapshot().DebtNoticeLine, Is.EqualTo("1 point spent on terrain last turn"));
            Assert.That(ledger.DebtOf(first), Is.Zero, "debt is consumed when the turn opens");
        }

        [Test]
        public void Overspend_MustBeTheFirstPlacementOfTheTurn()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 20, perTurn: 3);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(first);
            turn.RecordPlacement(1);

            TerrainPieceAffordability verdict = turn.Evaluate(4);
            Assert.That(verdict.Playable, Is.False);
            Assert.That(verdict.BlockedReason, Does.Contain("must be placed first"));
        }

        [Test]
        public void PayingDebt_BlocksTakingNewDebt()
        {
            // Chris's rule: no going into debt two turns in a row - a turn that is repaying debt
            // cannot open with another over-budget piece.
            var (ledger, first, _) = TwoPlayerLedger(total: 20, perTurn: 3);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(first);
            turn.RecordPlacement(5);   // debt 2

            TerrainPointsLedger.Turn next = ledger.BeginTurn(first);
            Assert.That(next.BudgetRemaining, Is.EqualTo(1));

            TerrainPieceAffordability verdict = next.Evaluate(3);
            Assert.That(verdict.Playable, Is.False);
            Assert.That(verdict.BlockedReason, Does.Contain("new debt").And.Contain("paying debt"));
        }

        [Test]
        public void NothingMayExceedTheRemainingAllotment()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 2, perTurn: 3);

            TerrainPieceAffordability verdict = ledger.BeginTurn(first).Evaluate(3);
            Assert.That(verdict.Playable, Is.False);
            Assert.That(verdict.BlockedReason, Does.Contain("only 2 of your 2 terrain points remain"));
        }

        [Test]
        public void DeepDebt_ConsumesWholeTurns_ThenBleedsIntoTheNext()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 20, perTurn: 3);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(first);
            turn.RecordPlacement(9);   // 3 budget + 6 debt
            Assert.That(ledger.DebtOf(first), Is.EqualTo(6));

            TerrainPointsLedger.Turn second = ledger.BeginTurn(first);
            Assert.That(second.DebtPaidThisTurn, Is.EqualTo(3));
            Assert.That(second.BudgetRemaining, Is.Zero, "the whole turn goes to debt - skipped");

            TerrainPointsLedger.Turn third = ledger.BeginTurn(first);
            Assert.That(third.DebtPaidThisTurn, Is.EqualTo(3));
            Assert.That(third.BudgetRemaining, Is.Zero, "still skipped");

            TerrainPointsLedger.Turn fourth = ledger.BeginTurn(first);
            Assert.That(fourth.DebtPaidThisTurn, Is.Zero);
            Assert.That(fourth.BudgetRemaining, Is.EqualTo(2),
                "debt repaid; what is left is capped by the remaining allotment (11 - 9)");
        }

        [Test]
        public void RecordPlacement_RefusesAnUnaffordablePiece()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 2, perTurn: 3);

            TerrainPointsLedger.Turn turn = ledger.BeginTurn(first);
            Assert.Throws<InvalidOperationException>(() => turn.RecordPlacement(3),
                "the ledger is the last line of defense behind the stage's re-prompt loop");
        }

        [Test]
        public void Forfeit_ZeroesRemainingPointsAndDebt()
        {
            var (ledger, first, _) = TwoPlayerLedger(total: 20, perTurn: 3);
            ledger.BeginTurn(first).RecordPlacement(5);   // 5 spent, 2 debt

            ledger.ForfeitRemaining(first);

            Assert.That(ledger.RemainingOf(first), Is.Zero);
            Assert.That(ledger.DebtOf(first), Is.Zero);
            Assert.That(ledger.HasPointsRemaining(first), Is.False);
        }

        [Test]
        public void TeamHasPointsRemaining_TracksItsPlayers()
        {
            PlayerID a1 = NewPlayer(), a2 = NewPlayer();
            var teamA = new FakeTeam(1, a1, a2);
            var teams = new List<ITeam> { teamA, new FakeTeam(2, NewPlayer()) };
            var ledger = new TerrainPointsLedger(teams, totalPoints: 9, pointsPerTurn: 3);

            Assert.That(ledger.TeamHasPointsRemaining(teamA), Is.True);
            foreach (PlayerID p in teamA.Players)
                ledger.ForfeitRemaining(p);
            Assert.That(ledger.TeamHasPointsRemaining(teamA), Is.False);
        }

        [Test]
        public void CostOf_FloorsAtOnePoint()
        {
            var freeloader = new TerrainPieceEntry { Points = 0 };
            Assert.That(TerrainPointsBudget.CostOf(freeloader), Is.EqualTo(1),
                "a hand-authored 0-point piece must never be placeable for free (infinite placement)");
        }

        [Test]
        public void TerrainPieceEntry_WithoutPointsInItsJson_DefaultsToOne()
        {
            // Layout files written before #299 have no Points field - every piece prices at the minimum.
            var entry = JsonConvert.DeserializeObject<TerrainPieceEntry>(
                "{\"Name\":\"Old fence\",\"HeightInches\":1.0}");

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Points, Is.EqualTo(1));
        }
    }
}
