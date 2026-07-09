using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #093 slice 4a (per-model movement budgets). MovementActionContext
    // now computes each living model's own Advance/Rush/Charge budget (unit base + unit rules + that model's
    // OWN movement rules), and MovementUtilities.ValidatePaths caps each model against its own budget while
    // coherency still reins a fast model in. Mirrors MovementRuleIntegrationTests (context budgets) and
    // ChargeReachValidationTests (path validation).
    [TestFixture]
    public class PerModelMovementBudgetTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public void Context_FastModel_GetsPerModelMoveBonuses_UnitmateDoesNot()
        {
            DataBinding<ModelData> fast = MakeModel(new Position(0, 0));
            DataBinding<ModelData> normal = MakeModel(new Position(2, 0));
            fast.GetValue().AttachRuleDefinition(new ResolvedRule("Fast", CoreRuleCatalog.Fast));
            DataBinding<UnitData> unit = MakeUnit(fast, normal);

            var context = new MovementActionContext(_ctx, unit);

            context.TryGetModelMoveBudget(fast.GetValue(), out float fastAdvance, out float fastRush, out _);
            context.TryGetModelMoveBudget(normal.GetValue(), out float normalAdvance, out float normalRush, out _);

            Assert.That(fastAdvance, Is.EqualTo(normalAdvance + 2f).Within(0.001f),
                "the Fast model's own Advance budget is +2; its unitmate's is the unit base.");
            Assert.That(fastRush, Is.EqualTo(normalRush + 4f).Within(0.001f),
                "the Fast model's own Rush budget is +4; its unitmate's is the unit base.");
        }

        [Test]
        public void PerModelBudget_ModelRangesAheadWithinItsBudget_Accepted()
        {
            DataBinding<ModelData> ahead = MakeModel(new Position(0, 0));
            DataBinding<ModelData> follower = MakeModel(new Position(2.5f, 0)); // 1" base-to-base
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(ahead, new List<Position> { new Position(15, 0) }),    // 15"
                new ModelMoveEntry(follower, new List<Position> { new Position(13, 0) }),  // 10.5"; ends 0.5" b2b of 'ahead'
            };

            bool ok = MovementUtilities.ValidatePaths(moves, BudgetPerModel(ahead, big: 18f, small: 12f),
                Array.Empty<EnemyModelFootprint>(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, terrain: null, out var errors);

            Assert.That(ok, Is.True,
                $"the lead model ranges ahead within its own 18\" budget and coherency holds. Errors: {Describe(errors)}");
        }

        [Test]
        public void SameMoves_UnderUniformUnitBudget_RejectTheLeadModel()
        {
            DataBinding<ModelData> ahead = MakeModel(new Position(0, 0));
            DataBinding<ModelData> follower = MakeModel(new Position(2.5f, 0));
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(ahead, new List<Position> { new Position(15, 0) }),
                new ModelMoveEntry(follower, new List<Position> { new Position(13, 0) }),
            };

            // The scalar overload = the same 12" budget for every model — the pre-#093 behavior.
            bool ok = MovementUtilities.ValidatePaths(moves, maxRushDistance: 12f, maxDistanceInches: 12f,
                enemyFootprints: new List<EnemyModelFootprint>(), terrain: null, out var errors);

            Assert.That(ok, Is.False, "under one unit-wide 12\" cap the lead model's 15\" move is out of range.");
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.OutOfMoveRange), Is.True);
        }

        [Test]
        public void PerModelBudget_ModelBeyondItsLowerBudget_Rejected()
        {
            DataBinding<ModelData> slow = MakeModel(new Position(0, 0));
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(slow, new List<Position> { new Position(6, 0) }), // 6" > its 4" budget
            };

            bool ok = MovementUtilities.ValidatePaths(moves, _ => new ModelMoveBudget(4f, 4f),
                Array.Empty<EnemyModelFootprint>(), false, false, false, terrain: null, out var errors);

            Assert.That(ok, Is.False, "a model that moves beyond its own (Slow) budget is out of range.");
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.OutOfMoveRange), Is.True);
        }

        [Test]
        public void PerModelBudget_FastModelBreaksCoherency_RejectedDespiteBudget()
        {
            DataBinding<ModelData> ahead = MakeModel(new Position(0, 0));
            DataBinding<ModelData> stay = MakeModel(new Position(2.5f, 0));
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(ahead, new List<Position> { new Position(20, 0) }), // within its huge budget
                new ModelMoveEntry(stay, new List<Position> { new Position(2.5f, 0) }), // holds position
            };

            bool ok = MovementUtilities.ValidatePaths(moves, _ => new ModelMoveBudget(30f, 30f),
                Array.Empty<EnemyModelFootprint>(), false, false, false, terrain: null, out var errors);

            Assert.That(ok, Is.False,
                "even within its budget, a model that outruns cohesion is rejected — coherency reins it in.");
        }

        // --- harness ---

        // Gives one model a big budget and everyone else a small one.
        private static Func<ModelMoveEntry, ModelMoveBudget> BudgetPerModel(
            DataBinding<ModelData> bigModel, float big, float small)
        {
            ModelID bigId = bigModel.GetValue().ID;
            return entry => entry.Model.GetValue().ID == bigId
                ? new ModelMoveBudget(big, big)
                : new ModelMoveBudget(small, small);
        }

        private static string Describe(List<ReasonForInvalidMove> errors) =>
            string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), initialPosition, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: models.ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
