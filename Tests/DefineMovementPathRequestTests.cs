using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #093 slice 4b: DefineMovementPathRequest carries per-model move budgets so the resolvers cap/preview
    // each model against its own reach. This pins the lookup contract the GUI/CLI/AI resolvers all use.
    [TestFixture]
    public class DefineMovementPathRequestTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void BudgetFor_ReturnsPerModelEntry_WhenPresent()
        {
            DataBinding<ModelData> hero = MakeModel();
            DataBinding<UnitData> unit = MakeUnit(hero);
            var perModel = new List<ModelMoveBudgetInfo>
            {
                new ModelMoveBudgetInfo(hero.GetValue().ID, MaxAdvanceDistance: 8f, MaxRushDistance: 12f, MaxDistanceInches: 12f),
            };
            var request = new DefineMovementPathRequest(new PlayerID(Guid.NewGuid()), "Move", unit,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f, modelMoveBudgets: perModel);

            (float advance, float rush, float maxDistance) = request.BudgetFor(hero.GetValue().ID);

            Assert.That(advance, Is.EqualTo(8f), "the hero's own Advance budget (Fast +2) is returned.");
            Assert.That(rush, Is.EqualTo(12f));
            Assert.That(maxDistance, Is.EqualTo(12f));
        }

        [Test]
        public void BudgetFor_FallsBackToUnitScalars_WhenModelAbsent()
        {
            DataBinding<ModelData> grunt = MakeModel();
            DataBinding<UnitData> unit = MakeUnit(grunt);
            var request = new DefineMovementPathRequest(new PlayerID(Guid.NewGuid()), "Move", unit,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f); // no per-model budgets

            (float advance, float rush, float maxDistance) = request.BudgetFor(grunt.GetValue().ID);

            Assert.That(advance, Is.EqualTo(6f), "a model without a per-model entry uses the unit scalars.");
            Assert.That(rush, Is.EqualTo(12f));
            Assert.That(maxDistance, Is.EqualTo(12f));
        }

        private DataBinding<ModelData> MakeModel()
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0, 0), _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>>(models));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
