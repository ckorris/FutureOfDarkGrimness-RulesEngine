using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #431 Crossing Strike (OPR 3.6): "Once per activation, when this model moves through enemy units, pick one of
    // them and attack it with this weapon as if it was shooting. This weapon may only be used in this way." That is
    // word for word the text of 3.5 Strafing, which 3.6 renamed, so the shipped definition is Strafing's activated
    // ability under the new name and runs on the existing StrafingStage (StrafingRuleIntegrationTests pins the stage
    // in depth). These pin that the RENAMED copy - authored as supplement data, not the catalog entry - gets the
    // whole treatment: offered off the weapon at the move-through hook, attacks with the weapon's own profile,
    // once per activation, kept out of both attack pools, and carries the shooting morale test (with Scourge's
    // counted wounds, #431). Like Strafing it grants no fly-over: the move-through needs Flying or Aircraft.
    [TestFixture]
    public class CrossingStrikeRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Crossing Strike");

        private GameDataStore _store = null!;
        private PlayerID _mover;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _mover = new PlayerID(System.Guid.NewGuid());
            _foe = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public void TheOfferComesFromTheWeapon_AndCostsTheActivationsUse()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", Bombs(), new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", null, new Position(5f, 0f));

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(new MoveThroughEnemyContext(mover.GetValue()));

            Assert.That(offers.Count, Is.EqualTo(1));
            Assert.That(offers[0].RuleName, Is.EqualTo("Crossing Strike"));
            Assert.That(offers[0].Weapon?.Name, Is.EqualTo("Flame Bombs"));

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0], new[] { (IUnit)enemy.GetValue() });
            Assert.That(ops.OfType<RuleOperation.InvokeWeaponAttack>().Single().Weapon?.Name, Is.EqualTo("Flame Bombs"));
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>().Any(op => op.TokenToGrant.Type == UsedMarker), Is.True,
                "once per activation");
        }

        [Test]
        public void OncePerActivation_NotOfferedAgain()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", Bombs(), new Position(0f, 0f));
            mover.GetValue().Tokens.AddToken(new Token(UsedMarker, 1, new TokenClearTrigger.ActivationEnd()));

            Assert.That(ctx.RuleEvaluator.GatherOffers(new MoveThroughEnemyContext(mover.GetValue())), Is.Empty);
        }

        [Test]
        public async Task TheStrikeAttacksWithTheWeaponsOwnProfile()
        {
            // A2 at Quality 4 = 1 hit; Defense 4, AP(0) = 0.5 wounds (ProbabilisticDiceRoller).
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", Bombs(attacks: 2), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, Column(5f));

            await RunMove(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Not.Null);
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public async Task Declined_NoAttack()
        {
            var requester = new StrafeRequester(accept: false);
            var ctx = new WoundTestContext(_store, requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", Bombs(attacks: 2), new Position(0f, 0f));
            MakeUnit(_foe, "Grunts", null, Column(5f));

            await RunMove(ctx, mover, new Position(10f, 0f));

            Assert.That(requester.WoundRequest, Is.Null, "the control");
        }

        [Test]
        public void TheWeapon_IsInNeitherAttackPool()
        {
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", Bombs(), new Position(0f, 0f));
            mover.GetValue().ModelBindings[0].GetValue().Weapons.Add(new Weapon("Claws", 0f, 2, 0));

            Assert.That(mover.GetValue().GetMeleeWeapons().Select(w => w.Name), Is.EquivalentTo(new[] { "Claws" }),
                "'This weapon may only be used in this way' - the range-0 bombs are not swung in melee");
            Assert.That(mover.GetValue().GetRangedWeapons(), Is.Empty);
            Assert.That(StrafingRules.IsStrafeOnly(Bombs()), Is.True, "detected by its effect, not by the old name");
        }

        [Test]
        public async Task AScourgeStrike_FeedsItsCountedWoundsIntoTheShootingMoraleTest()
        {
            // "as if it was shooting": the strike's morale test reads Scourge's counted wounds too. Scripted dice:
            // the one attack hits (6), its save is a natural 1 (a real wound AND two counted ones), and the morale
            // die is a 1. One real loss leaves the five-model unit at 4 - above half - but counting the two extra
            // wounds it is at 2 of 5, so it tests, fails and is Shaken.
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ScriptedFaceRoller(6, 1, 1));
            Weapon bombs = Bombs(attacks: 1);
            bombs.AttachRuleDefinition(new ResolvedRule("Scourge", WeaponRules36Definitions.Scourge));
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", bombs, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", null, Column(5f));

            await RunMove(ctx, mover, new Position(10f, 0f));

            Assert.That(enemy.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(4), "one real wound");
            Assert.That(enemy.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True,
                "the counted wounds triggered the test, and the 1 failed it");
            Assert.That(CountedWounds.Peek(enemy.GetValue()), Is.EqualTo(0f), "used up by it");
        }

        [Test]
        public async Task ThePlainStrike_SameDice_NoMoraleTest()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new ScriptedFaceRoller(6, 1, 1));
            DataBinding<UnitData> mover = MakeUnit(_mover, "Gunship", Bombs(attacks: 1), new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeUnit(_foe, "Grunts", null, Column(5f));

            await RunMove(ctx, mover, new Position(10f, 0f));

            Assert.That(enemy.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(4));
            Assert.That(enemy.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False,
                "the control: 4 of 5 is above half, so no test");
        }

        // --- helpers ------------------------------------------------------------------------------------------

        private static Position[] Column(float x) => new[]
        {
            new Position(x, 0f), new Position(x, 1f), new Position(x, 2f), new Position(x, 3f), new Position(x, 4f),
        };

        /// <summary> A 3.6 bomb weapon: range 0, carrying the supplement's Crossing Strike. </summary>
        private static Weapon Bombs(int attacks = 1)
        {
            var weapon = new Weapon("Flame Bombs", rangeInches: 0f, attacks: attacks, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Crossing Strike", WeaponRules36Definitions.CrossingStrike));
            return weapon;
        }

        private static async Task RunMove(WoundTestContext ctx, DataBinding<UnitData> mover, Position destination)
        {
            var moveContext = new MovementActionContext(ctx, mover);
            moveContext.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { destination }),
            });

            var stage = new StrafingStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnStrafeResolved.Bind("done");
            await stage.Enter(moveContext);
        }

        // Each Roll lands every die on the next scripted face (honouring the roll count); the last face repeats.
        private sealed class ScriptedFaceRoller : IDiceRoller
        {
            private readonly int[] _faces;
            private int _next;

            public ScriptedFaceRoller(params int[] faces) => _faces = faces;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                int face = _faces[System.Math.Min(_next++, _faces.Length - 1)];
                float[] perSide = new float[sideCount];
                perSide[face - 1] = rollCount;
                return new DiceResults(perSide, 1);
            }
        }

        // A unit given a strike weapon also gets Flying: Crossing Strike grants no fly-over of its own.
        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Weapon? weapon, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var weapons = weapon == null ? new List<Weapon>() : new List<Weapon> { weapon };
                var model = new ModelData(0.5f, weapons, pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (weapon != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Flying", CoreRuleCatalog.Flying));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
