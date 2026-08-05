using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #339: a unit that is charged, strikes back, and kills its attacker gets a consolidation move too.
    //
    // ConsolidateStage used to key off the ATTACKING seat alone: a dead attacker logged "attacker has no
    // living models - skipping" and the melee ended with the unit that actually won it standing exactly
    // where it started, while the mirror case (charger wipes out the defender) got its 3" move. The survivor
    // consolidates now, whichever seat it sat in.
    //
    // This drives the real MeleeStage graph - charge, swing, strike-back, the kill, fatigue, consolidation -
    // so the wiring between StrikeBackStage.OnAttackerKilled and the consolidation request is under test,
    // not just the stage in isolation (ConsolidateStageTests covers that).
    [TestFixture]
    public class StrikeBackConsolidationTests
    {
        [Test]
        public async Task DefenderKillsCharger_DefenderIsOfferedTheWipeoutConsolidation()
        {
            var (ctx, requester, charger, defender) = BuildMelee();

            var melee = new MeleeStage(ctx, new NoOpLayer<IUnitActionContext>());
            melee.OnFinishedMelee.Bind("finishedMelee");
            melee.BackToChooseAction.Bind("backToChooseAction");

            await melee.Enter(new UnitActionContext(ctx, charger));

            Assert.That(charger.GetValue().ModelBindings.Any(mb => mb.GetValue().GetIsAlive()), Is.False,
                "premise: the strike-back wiped out the charger.");
            Assert.That(requester.Consolidation, Is.Not.Null,
                "the surviving defender must be offered a consolidation move.");
            Assert.That(requester.Consolidation!.UnitDataBinding.Reference, Is.EqualTo(defender.Reference),
                "and it is the DEFENDER that moves - the charger is dead.");
            Assert.That(requester.Consolidation.Reason, Is.EqualTo(EConsolidationReason.Wipeout));
            Assert.That(requester.Consolidation.MaxDistanceInches,
                Is.EqualTo(ConsolidateStage.WIPEOUT_MAX_DISTANCE_INCHES));
            Assert.That(requester.Consolidation.TargetPlayerID, Is.EqualTo(defender.GetValue().PlayerID),
                "the prompt goes to the defender's player, out of turn.");
            Assert.That(defender.GetValue().ModelBindings[0].GetValue().Position.z, Is.EqualTo(18f).Within(0.0001f),
                "and the 2\" it asked for was actually executed.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        // Every die lands on a 4: both units hit (Quality 4+), the charger's swing bounces off the defender
        // (Defense 2, AP 0 - saved on a 4), and the defender's strike-back cannot be saved (Defense 4 + AP 3,
        // clamped to 6+ - failed on a 4), which kills the one-model charger outright.
        private static (WoundTestContext ctx, MeleeRequester requester,
            DataBinding<UnitData> charger, DataBinding<UnitData> defender) BuildMelee()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new MeleeRequester();
            var ctx = new WoundTestContext(store, requester, new FixedFaceDiceRoller(4));

            var chargerPlayer = new PlayerID(Guid.NewGuid());
            var defenderPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { chargerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { defenderPlayer }));

            DataBinding<UnitData> charger = MakeUnit(store, chargerPlayer, "Charger", quality: 4, defense: 4,
                weapon: new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0),
                positions: new[] { new Position(20f, 20f) });
            DataBinding<UnitData> defender = MakeUnit(store, defenderPlayer, "Defender", quality: 4, defense: 2,
                weapon: new Weapon("Halberd", rangeInches: 0f, attacks: 1, armorPenetration: 3),
                positions: new[] { new Position(22f, 20f) });

            store.Create(new ArmyData(chargerPlayer, new List<DataBinding<UnitData>> { charger }));
            store.Create(new ArmyData(defenderPlayer, new List<DataBinding<UnitData>> { defender }));

            return (ctx, requester, charger, defender);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID, string name,
            int quality, int defense, Weapon weapon, Position[] positions)
        {
            var modelBindings = positions.Select(position =>
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon> { weapon },
                    initialPosition: position,
                    gameDataStore: store);
                return store.GetDataBinding<ModelData>(store.Create(model));
            }).ToList();

            var unit = new UnitData(playerID, name, quality, defense, modelBindings);
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        // Answers every prompt the melee poses: take the first defender, swing the first weapon, always
        // strike back, and consolidate 2" (inside the wipeout cap) so the executed move is observable.
        private sealed class MeleeRequester : IPlayerRequestByID
        {
            public ConsolidationMoveRequest? Consolidation { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case CancellableSelectionRequest<UnitData> defenderPick:
                        return Reply<TReply>(new Selected<DataBinding<UnitData>>(defenderPick.ValidOptions[0].Option));

                    case StringSelectionRequest weaponPick:
                        return Reply<TReply>(weaponPick.ValidOptions[0]);

                    case YesNoRequest:
                        return Reply<TReply>(true);

                    case ConsolidationMoveRequest consolidation:
                        Consolidation = consolidation;
                        return Reply<TReply>(consolidation.UnitDataBinding.GetValue().ModelBindings
                            .Where(mb => mb.GetValue().GetIsAlive())
                            .Select(mb =>
                            {
                                Position p = mb.GetValue().Position;
                                return new ModelMoveEntry(mb,
                                    new List<Position> { new Position(p.x, p.z - 2f) });
                            })
                            .ToList());
                }

                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }

            private static Task<TReply> Reply<TReply>(object reply) => Task.FromResult((TReply)reply);
        }
    }
}
