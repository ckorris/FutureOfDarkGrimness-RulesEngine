using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #028: Deadly (wound-multiplier) weapons must be resolved before the unit's other weapons, so a
    // multiplied clump removes whole models before normal wounds spread. The wound-confinement half lives
    // in AssignWoundsStage (see WoundRuleIntegrationTests); these cover the *ordering* half — the shared
    // WoundPriorityQueries predicate and the melee weapon-choice gating it drives. (Ranged gating is
    // covered in ChooseRangedAttackStageTests.)
    [TestFixture]
    public class DeadlyWeaponPriorityTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // WoundPriorityQueries.MustResolveFirst — the capability predicate.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void MustResolveFirst_TrueForDeadlyWeapon()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            var attacker = MakeUnit(store, MakeModel(store, MeleeDeadlyWeapon("Axe", x: 3)));

            var weapon = attacker.GetValue().GetMeleeWeapons().Single();
            Assert.That(WoundPriorityQueries.MustResolveFirst(attacker.GetValue(), weapon, evaluator), Is.True);
        }

        [Test]
        public void MustResolveFirst_FalseForPlainWeapon()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            var attacker = MakeUnit(store, MakeModel(store, new Weapon("Blade", 0f, 1, 0)));

            var weapon = attacker.GetValue().GetMeleeWeapons().Single();
            Assert.That(WoundPriorityQueries.MustResolveFirst(attacker.GetValue(), weapon, evaluator), Is.False);
        }

        // ──────────────────────────────────────────────────────────────────────
        // ChooseMeleeWeaponStage — Deadly-first gating in the melee picker.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task MeleeChoose_DeadlyAvailable_GatesNonDeadlyWeapons()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingStringSelectionRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);

            var model = MakeModel(store, MeleeDeadlyWeapon("Axe", x: 3), new Weapon("Blade", 0f, 1, 0));
            var attacker = MakeUnit(store, model);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: true);
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChosen.Bind("test-on-chosen");
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null, "Resolver should have been called.");
            var req = requester.Captured!;
            Assert.That(req.ValidOptions.Any(o => o.Contains("Axe")), Is.True,
                "The Deadly weapon must be a valid choice.");
            var bladeInvalid = req.InvalidOptions.SingleOrDefault(io => io.Option.Contains("Blade"));
            Assert.That(bladeInvalid, Is.Not.Null,
                "The non-Deadly weapon must be gated while a Deadly weapon is available.");
            Assert.That(bladeInvalid!.Reason, Does.Contain("Deadly"));
        }

        [Test]
        public async Task MeleeChoose_NoDeadlyWeapon_AllWeaponsSelectable()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingStringSelectionRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);

            var model = MakeModel(store, new Weapon("Blade", 0f, 1, 0), new Weapon("Spear", 0f, 1, 0));
            var attacker = MakeUnit(store, model);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: true);
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChosen.Bind("test-on-chosen");
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            Assert.That(requester.Captured!.InvalidOptions, Is.Empty,
                "With no Deadly weapon present, no weapon should be gated.");
            Assert.That(requester.Captured!.ValidOptions.Count, Is.EqualTo(2));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static Weapon MeleeDeadlyWeapon(string name, int x)
        {
            var weapon = new Weapon(name, rangeInches: 0f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(x) }));
            return weapon;
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, params Weapon[] weapons)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons.ToList(),
                initialPosition: new Position(0, 0, 0), gameDataStore: store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Attacker", quality: 4, defense: 4,
                modelBindings: models.ToList());
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        internal class CapturingStringSelectionRequester : IPlayerRequestByID
        {
            public StringSelectionRequest? Captured { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest ssr)
                {
                    Captured = ssr;
                    // Reply with the first valid option so the stage completes without throwing.
                    object reply = ssr.ValidOptions.First();
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
