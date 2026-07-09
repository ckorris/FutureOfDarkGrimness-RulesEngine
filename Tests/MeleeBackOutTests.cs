using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Backing out of a Charge must not spend the unit's attack.
    //
    // ChooseMeleeDefenderStage's back-out exit used to be bound to the same MeleeStage sibling as
    // "melee finished", whose OnWillActivate calls IUnitActionContext.RegisterAttackedFinished(). Because
    // TransitionToSibling re-fires OnWillActivate, merely declining the charge set HasAttacked, which
    // ChooseActionStage then reads to disable Move, Charge and Shoot — the unit could only Pass or Cast.
    // The back-out now leaves through its own sibling, mirroring ShootStage.BackToChooseAction.
    //
    // These drive the real MeleeStage (its transitions are built in the constructor) so the wiring itself
    // is under test, not just the child stage.
    [TestFixture]
    public class MeleeBackOutTests
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
        public async Task NoDefenderInRange_BacksOut_WithoutSpendingTheAttack()
        {
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f, 0f));
            // No enemy army at all — ChooseMeleeDefenderStage finds nothing and backs out immediately.

            var (context, backedOut, finishedMelee) = await RunMelee(attacker);

            Assert.That(backedOut, Is.True, "no reachable defender routes to the back-out exit.");
            Assert.That(finishedMelee, Is.False, "the back-out must not travel the melee-finished exit.");
            Assert.That(context.HasAttacked, Is.False,
                "a charge that never happened must leave the unit free to Move, Charge or Shoot.");
        }

        [Test]
        public async Task PlayerCancelsDefenderPick_BacksOut_WithoutSpendingTheAttack()
        {
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(1f, 0f, 0f));   // one valid defender, so the pick is posed
            _requester.CancelEverything = true;

            var (context, backedOut, finishedMelee) = await RunMelee(attacker);

            Assert.That(backedOut, Is.True, "cancelling the defender pick routes to the back-out exit.");
            Assert.That(finishedMelee, Is.False);
            Assert.That(context.HasAttacked, Is.False,
                "this is the reported bug: backing out of a mis-clicked charge used to burn the attack.");
        }

        private async Task<(IUnitActionContext Context, bool BackedOut, bool FinishedMelee)> RunMelee(
            DataBinding<UnitData> attacker)
        {
            var context = new UnitActionContext(_ctx, attacker);
            var melee = new MeleeStage(_ctx, new NoOpLayer<IUnitActionContext>());

            bool backedOut = false, finishedMelee = false;
            melee.BackToChooseAction.Bind("backToChooseAction");
            melee.OnFinishedMelee.Bind("finishedMelee");
            melee.BackToChooseAction.OnWillActivate += _ => backedOut = true;
            melee.OnFinishedMelee.OnWillActivate += _ => finishedMelee = true;

            await melee.Enter(context);
            return (context, backedOut, finishedMelee);
        }

        private void MakeEnemyArmy(params Position[] modelPositions)
        {
            PlayerID enemyPlayer = new PlayerID(System.Guid.NewGuid());
            DataBinding<UnitData> enemyUnit = MakeUnit(enemyPlayer, modelPositions);
            ArmyData army = new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit });
            _store.Create(army);
        }

        private DataBinding<UnitData> MakeUnit(params Position[] modelPositions)
            => MakeUnit(new PlayerID(System.Guid.NewGuid()), modelPositions);

        private DataBinding<UnitData> MakeUnit(PlayerID playerID, params Position[] modelPositions)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelPositions.Length);
            foreach (Position position in modelPositions)
            {
                var weapons = new List<Weapon> { new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0) };
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons,
                    initialPosition: position,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
