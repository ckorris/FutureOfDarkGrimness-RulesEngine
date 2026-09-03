using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #358 — the solo bot's decline-repick livelock. A wedged unit's main-move decline (#208)
    // bounces back to the action menu, where the deterministic Charge > Move > Shoot > Pass
    // policy re-picks Move and declines again, forever (observed as ~1.5M decisions until the
    // FdgLab watchdog killed the game - pool seed 1010, HDF vs Hives, in the #359 ledger).
    // The latch connects the solo set's two resolvers: decline of a MAIN activation move arms
    // it, the very next action pick consumes it and skips the movement family once. Declining
    // an optional TRIGGERED move (GameOperationServices) is final and must never arm it.
    [TestFixture]
    public class SoloMoveDeclineLatchTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private PlayerID _us;
        private PlayerID _them;

        private static readonly string[] FullMenu =
        {
            ChooseActionStage.CHARGE_CHOICE_NAME, ChooseActionStage.MOVEMENT_CHOICE_NAME,
            ChooseActionStage.SHOOT_CHOICE_NAME, ChooseActionStage.PASS_CHOICE_NAME,
        };

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // The #208 wedge: two models 6" apart (post-melee intermingling spread) with a budget too
        // small to advance or re-pack - the ladder bottoms at a cohesion-breaking hold, and a
        // cancellable request is declined.
        private DataBinding<UnitData> MakeWedgedUnit()
        {
            var a = _store.GetDataBinding<ModelData>(_store.Create(
                new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store)));
            var b = _store.GetDataBinding<ModelData>(_store.Create(
                new ModelData(0.5f, new List<Weapon>(), new Position(0f, 6f), _store)));
            var unit = new UnitData(_us, "Spread", 4, 4, new List<DataBinding<ModelData>> { a, b });
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            var enemy = _store.GetDataBinding<ModelData>(_store.Create(
                new ModelData(0.5f, new List<Weapon>(), new Position(0f, 100f), _store)));
            _store.Create(new UnitData(_them, "Enemies", 4, 4, new List<DataBinding<ModelData>> { enemy }));
            return binding;
        }

        private DefineMovementPathRequest WedgedRequest(DataBinding<UnitData> unit, bool mainActivationMove) =>
            new DefineMovementPathRequest(_us, "Moving Spread", unit,
                maxAdvanceDistance: 0.3f, maxRushDistance: 0.3f, maxDistanceInches: 0.3f,
                allowCancel: true, mainActivationMove: mainActivationMove);

        private ChooseActionRequest Menu() => new ChooseActionRequest(_us, new UnitID(Guid.NewGuid()),
            FullMenu, new List<StringSelectionRequest.InvalidOption>());

        [Test]
        public async Task MainMoveDecline_MakesTheNextActionPickSkipMovement_ThenClears()
        {
            var latch = new SoloMoveDeclineLatch();
            var moveResolver = new AiDefineMovementResolver(_tableState, _us, latch);
            var menuResolver = new AiStringSelectionResolver(_tableState, _us, latch);
            DataBinding<UnitData> unit = MakeWedgedUnit();

            CancellableResult<List<ModelMoveEntry>> declined =
                await moveResolver.Resolve(WedgedRequest(unit, mainActivationMove: true));
            Assert.That(declined, Is.InstanceOf<Cancelled<List<ModelMoveEntry>>>(),
                "scene check: the wedged unit's main move really is declined (#208)");

            string pick = await menuResolver.Resolve(Menu());
            Assert.That(pick, Is.Not.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME)
                .And.Not.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME),
                "the menu the decline reopened must not re-pick the movement family - that IS the livelock");

            string nextActivation = await menuResolver.Resolve(Menu());
            Assert.That(nextActivation, Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME),
                "the latch is one pick only - the next activation's policy is unchanged");
        }

        [Test]
        public async Task TriggeredMoveDecline_DoesNotArmTheLatch()
        {
            // GameOperationServices' optional post-combat move: declining is final ("no thanks"),
            // no menu reopens, and a later unrelated activation must keep its normal policy.
            var latch = new SoloMoveDeclineLatch();
            var moveResolver = new AiDefineMovementResolver(_tableState, _us, latch);
            var menuResolver = new AiStringSelectionResolver(_tableState, _us, latch);
            DataBinding<UnitData> unit = MakeWedgedUnit();

            CancellableResult<List<ModelMoveEntry>> declined =
                await moveResolver.Resolve(WedgedRequest(unit, mainActivationMove: false));
            Assert.That(declined, Is.InstanceOf<Cancelled<List<ModelMoveEntry>>>());

            string pick = await menuResolver.Resolve(Menu());
            Assert.That(pick, Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME),
                "a triggered-move decline is final - it must not bend the next activation's pick");
        }
    }
}
