using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #022 — Vertical melee range handling. Melee range is the 2"-horizontal AND 4"-vertical cylinder
    // (GF v3.5.1). Before #022 the *defender-eligibility* gate (ChooseMeleeDefenderStage) and the
    // *charge-availability* gate (ChooseActionStage.GetCanCharge) applied only the horizontal half, so a
    // unit within 2" horizontally but beyond 4" vertically (e.g. on a tall ledge) was offered/engageable
    // even though no model could actually strike it. Both gates now share
    // MeleeRangeUtilities.AreUnitsInMeleeRange — the same cylinder the post-pile-in strike gate uses
    // (covered separately by MeleeInRangeIntegrationTests). These tests pin the unit-level predicate and
    // prove ChooseMeleeDefenderStage no longer offers a vertically-distant enemy as a valid defender.
    [TestFixture]
    public class VerticalMeleeRangeTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;
        private MeleeDefenderRequester _requester = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new MeleeDefenderRequester();
            _ctx = new WoundTestContext(_store, _requester);
        }

        [Test]
        public void AreUnitsInMeleeRange_HorizontallyAndVerticallyClose_True()
        {
            DataBinding<UnitData> a = MakeUnit(new Position(0f, 0f, 0f));
            DataBinding<UnitData> b = MakeUnit(new Position(1f, 2f, 0f)); // 1" horiz centre, 2" up

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(a.GetValue(), b.GetValue()), Is.True);
        }

        [Test]
        public void AreUnitsInMeleeRange_HorizontallyCloseButVerticallyTooFar_False()
        {
            // The whole point of #022: 1" horizontal centre distance (in range), but 5" up — beyond the
            // 4" vertical reach. No model can strike, so the units are not in melee range.
            DataBinding<UnitData> a = MakeUnit(new Position(0f, 0f, 0f));
            DataBinding<UnitData> b = MakeUnit(new Position(1f, 5f, 0f));

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(a.GetValue(), b.GetValue()), Is.False);
        }

        [Test]
        public void AreUnitsInMeleeRange_VerticalExactlyAtCeiling_True()
        {
            // Boundary: exactly 4" vertical is still in range (the check excludes only > 4").
            DataBinding<UnitData> a = MakeUnit(new Position(0f, 0f, 0f));
            DataBinding<UnitData> b = MakeUnit(new Position(1f, 4f, 0f));

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(a.GetValue(), b.GetValue()), Is.True);
        }

        [Test]
        public void AreUnitsInMeleeRange_HorizontallyTooFar_False()
        {
            // 10" horizontal centre distance (b2b 8.5") — out of range regardless of a 0" vertical gap.
            DataBinding<UnitData> a = MakeUnit(new Position(0f, 0f, 0f));
            DataBinding<UnitData> b = MakeUnit(new Position(10f, 0f, 0f));

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(a.GetValue(), b.GetValue()), Is.False);
        }

        [Test]
        public void AreUnitsInMeleeRange_IgnoresDeadModels()
        {
            // A corpse sits in base contact; the only living model is 20" away. A wiped/out-of-reach
            // unit must not register as in melee range.
            DataBinding<UnitData> a = MakeUnit(new Position(0f, 0f, 0f));
            DataBinding<UnitData> b = MakeUnit(new Position(1f, 0f, 0f), new Position(20f, 0f, 0f));

            ModelData corpse = b.GetValue().ModelBindings[0].GetValue();
            corpse.DealWounds(corpse.TotalWounds);

            Assert.That(MeleeRangeUtilities.AreUnitsInMeleeRange(a.GetValue(), b.GetValue()), Is.False,
                "The dead model at base contact must be ignored; the living model is out of range.");
        }

        [Test]
        public async Task ChooseMeleeDefender_VerticallyDistantEnemy_NotOfferedAndReturnsToChooseAction()
        {
            // Attacker at origin; the sole enemy is 1" horizontally away but 5" up. With no engageable
            // defender the stage routes back to Choose Action instead of attacking.
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(1f, 5f, 0f));

            var (defenderChosen, backToChooseAction) = await RunChooseMeleeDefender(attacker);

            Assert.That(defenderChosen, Is.False, "A vertically-distant enemy must not be a valid defender.");
            Assert.That(backToChooseAction, Is.True);
        }

        [Test]
        public async Task ChooseMeleeDefender_EnemyWithinCylinder_IsChosen()
        {
            // Same horizontal placement, but within the 4" vertical reach — now a valid defender.
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(1f, 2f, 0f));

            var (defenderChosen, backToChooseAction) = await RunChooseMeleeDefender(attacker);

            Assert.That(defenderChosen, Is.True, "An enemy inside the melee cylinder is a valid defender.");
            Assert.That(backToChooseAction, Is.False);
        }

        // The sole valid defender is still offered as a prompt rather than auto-selected: that prompt is the
        // charge's only Back button, and nothing has been mutated yet.
        [Test]
        public async Task ChooseMeleeDefender_SingleValidDefender_StillPrompts()
        {
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(1f, 2f, 0f));

            await RunChooseMeleeDefender(attacker);

            Assert.That(_requester.RequestCount, Is.EqualTo(1),
                "one valid defender must still be posed as a cancellable request, not auto-attacked.");
        }

        [Test]
        public async Task ChooseMeleeDefender_PlayerCancels_BacksOutWithoutChoosingDefender()
        {
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(1f, 2f, 0f));
            _requester.CancelEverything = true;

            var (defenderChosen, backToChooseAction) = await RunChooseMeleeDefender(attacker);

            Assert.That(defenderChosen, Is.False, "cancelling picks no defender.");
            Assert.That(backToChooseAction, Is.True, "cancelling routes to the back-out exit.");
        }

        private async Task<(bool defenderChosen, bool backToChooseAction)> RunChooseMeleeDefender(
            DataBinding<UnitData> attacker)
        {
            CombatActionContext context = new CombatActionContext(_ctx, attacker, isMelee: true, isCharging: true);

            ChooseMeleeDefenderStage stage =
                new ChooseMeleeDefenderStage(_ctx, new NoOpLayer<ICombatActionContext>());

            bool defenderChosen = false;
            bool backToChooseAction = false;
            stage.OnDefenderChosen.Bind("done");
            stage.BackToChooseAction.Bind("done");
            stage.OnDefenderChosen.OnWillActivate += _ => defenderChosen = true;
            stage.BackToChooseAction.OnWillActivate += _ => backToChooseAction = true;

            await stage.Enter(context);
            return (defenderChosen, backToChooseAction);
        }

        // Enemy units carry a distinct PlayerID and live in their own ArmyData so the defender scan finds them.
        private void MakeEnemyArmy(params Position[] modelPositions)
        {
            PlayerID enemyPlayer = new PlayerID(System.Guid.NewGuid());
            DataBinding<UnitData> enemyUnit = MakeUnit(enemyPlayer, modelPositions);
            ArmyData army = new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit });
            _store.Create(army);
        }

        private DataBinding<UnitData> MakeUnit(params Position[] modelPositions)
            => MakeUnit(new PlayerID(System.Guid.NewGuid()), modelPositions);

        // Each model carries a single melee weapon (RangeInches 0), base radius 0.75".
        private DataBinding<UnitData> MakeUnit(PlayerID playerID, params Position[] modelPositions)
        {
            List<DataBinding<ModelData>> modelBindings = new List<DataBinding<ModelData>>(modelPositions.Length);
            foreach (Position position in modelPositions)
            {
                List<Weapon> weapons = new List<Weapon> { new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0) };
                ModelData model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons,
                    initialPosition: position,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            UnitData unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    // Answers the defender pick by taking the first valid option, or by cancelling. ChooseMeleeDefenderStage
    // now always poses the request (even for a single defender), so these tests need a real reply.
    internal sealed class MeleeDefenderRequester : IPlayerRequestByID
    {
        public bool CancelEverything { get; set; }
        public int RequestCount { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is CancellableSelectionRequest<UnitData> selection)
            {
                RequestCount++;
                CancellableResult<DataBinding<UnitData>> result = CancelEverything
                    ? new Cancelled<DataBinding<UnitData>>()
                    : new Selected<DataBinding<UnitData>>(selection.ValidOptions[0].Option);
                return Task.FromResult((TReply)(object)result);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
