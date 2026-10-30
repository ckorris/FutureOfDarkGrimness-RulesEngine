using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #431 - the save-side half of the OPR 3.6 weapon-rule family, through the REAL AssignWoundsStage (the
    // RerollThresholdRuleIntegrationTests setup): the "re-roll unmodified Defense results of 6" rules (Smash,
    // Decimate), the "re-roll successful unmodified Defense results of 5-6" rules (Fragment, Obliterate, Reap) and
    // the "ignores Regeneration" facet (Anti-Regen, Puncture, Obliterate, Disintegrate). Each rule gets a case where
    // its gate is met and a control where it is not.
    //
    // The fixture hands the stage one failed save (a natural 1) and two successful ones (a natural 5 and a natural
    // 6) at save-needed 4. ProbabilisticDiceRoller makes every re-roll exact: N re-rolled dice at save-needed 4
    // yield N x 3/6 new wounds. So 1.0 = nothing re-rolled, 1.5 = the 6 re-rolled, 2.0 = the 5 and the 6 re-rolled.
    // That is the histogram-mode (#100) run for the family.
    [TestFixture]
    public class WeaponRules36SaveRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
        }

        private CombatMetadata? _last;

        // The wounds the attack lands after re-rolls and Regeneration: the AssignWoundsResults the stage posts,
        // asked of the player or auto-filled alike (a single-model target is never asked).
        private float Wounds
        {
            get
            {
                Assert.That(_last!.QueryForResult(out AssignWoundsResults result), Is.True);
                return result.TotalWoundsToAssign;
            }
        }

        // --- Smash: over half of the weapon's range ---------------------------------------------------------

        [Test]
        public async Task Smash_At13InchesOfA24InchGun_RerollsTheSix()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Smash, range: 24f), MakeDefender(), distance: 13f);

            Assert.That(Wounds, Is.EqualTo(1.5f).Within(0.0001f), "13\" is over half of 24\" - the saved 6 is re-rolled");
        }

        [Test]
        public async Task Smash_At11InchesOfA24InchGun_RerollsNothing()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Smash, range: 24f), MakeDefender(), distance: 11f);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f), "11\" is inside half range - nothing is re-rolled");
        }

        [Test]
        public async Task Smash_NeverRerollsTheFive()
        {
            // "unmodified Defense results of 6" - the 5 stands even far out.
            await RunSaves(WeaponWith(WeaponRules36Definitions.Smash, range: 24f), MakeDefender(), distance: 20f);

            Assert.That(Wounds, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public async Task Smash_InMelee_RerollsNothing()
        {
            // An Assault weapon swung in base contact is never "over half of its range away".
            await RunSaves(WeaponWith(WeaponRules36Definitions.Smash, range: 6f), MakeDefender(), distance: 20f,
                isMelee: true);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f));
        }

        // --- Fragment: most models Defense 5+ to 6+ ----------------------------------------------------------

        [Test]
        public async Task Fragment_VersusDefense5_RerollsTheFiveAndTheSix()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Fragment), MakeDefender(defense: 5), distance: 10f);

            Assert.That(Wounds, Is.EqualTo(2f).Within(0.0001f), "Defense 5+ is in the 5+..6+ band - re-roll 5-6");
        }

        [Test]
        public async Task Fragment_VersusDefense4_RerollsNothing()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Fragment), MakeDefender(defense: 4), distance: 10f);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f), "Defense 4+ is outside the band");
        }

        [Test]
        public async Task Fragment_OnlyRerollsSuccessfulFives()
        {
            // "re-roll SUCCESSFUL unmodified Defense results of 5-6": against Defense 6+ a natural 5 already failed,
            // so only the saved 6 goes back in the cup. Failed {1, 5}, saved {6}, save-needed 6: 2 wounds + 1 x 5/6.
            await RunSaves(WeaponWith(WeaponRules36Definitions.Fragment), MakeDefender(defense: 6), distance: 10f,
                failedFaces: new[] { 1, 5 }, savedFaces: new[] { 6 }, saveNeeded: 6);

            Assert.That(Wounds, Is.EqualTo(2f + 5f / 6f).Within(0.0001f), "the failed 5 is not re-rolled a second time");
        }

        // --- Obliterate: most models Tough(3)+ ------------------------------------------------------------------

        [Test]
        public async Task Obliterate_VersusToughThree_RerollsTheFiveAndTheSix()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Obliterate), MakeDefender(models: 3, tough: 3), distance: 10f);

            Assert.That(Wounds, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public async Task Obliterate_VersusToughOne_RerollsNothing()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Obliterate), MakeDefender(models: 3, tough: 1), distance: 10f);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f));
        }

        // --- Decimate: more than half of total size / Tough value ---------------------------------------------

        [Test]
        public async Task Decimate_VersusAFullStrengthUnit_RerollsTheSix()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Decimate), MakeDefender(models: 4), distance: 10f);

            Assert.That(Wounds, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public async Task Decimate_VersusAUnitAtHalfStrength_RerollsNothing()
        {
            DataBinding<UnitData> defender = MakeDefender(models: 4);
            Kill(defender, 2); // 2 of 4 left: half, not "more than half"

            await RunSaves(WeaponWith(WeaponRules36Definitions.Decimate), defender, distance: 10f);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public async Task Decimate_VersusASingleToughModel_ReadsItsToughValue()
        {
            // "or Tough value (for single-model units)": Tough(6) with 2 wounds taken has 4 of 6 - more than half.
            DataBinding<UnitData> monster = MakeDefender(models: 1, tough: 6);
            monster.GetValue().ModelBindings[0].GetValue().DealWounds(2);
            await RunSaves(WeaponWith(WeaponRules36Definitions.Decimate), monster, distance: 10f);
            Assert.That(Wounds, Is.EqualTo(1.5f).Within(0.0001f), "4 of 6 wounds left - the 6 is re-rolled");

            // 3 of 6 is half - no longer above it.
            SetUp();
            DataBinding<UnitData> wounded = MakeDefender(models: 1, tough: 6);
            wounded.GetValue().ModelBindings[0].GetValue().DealWounds(3);
            await RunSaves(WeaponWith(WeaponRules36Definitions.Decimate), wounded, distance: 10f);
            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f), "3 of 6 is half strength - nothing is re-rolled");
        }

        // --- Reap: Shaken or Fatigued ----------------------------------------------------------------------------

        [TestCase("Fatigued")]
        [TestCase("Shaken")]
        public async Task Reap_VersusAShakenOrFatiguedUnit_RerollsTheFiveAndTheSix(string status)
        {
            DataBinding<UnitData> defender = MakeDefender();
            defender.GetValue().Tokens.AddToken(Rules.Tokens.TokenDefinitionCatalog.Create(new TokenType(status)));

            await RunSaves(WeaponWith(WeaponRules36Definitions.Reap), defender, distance: 10f);

            Assert.That(Wounds, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public async Task Reap_VersusAFreshUnit_RerollsNothing()
        {
            await RunSaves(WeaponWith(WeaponRules36Definitions.Reap), MakeDefender(), distance: 10f);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f));
        }

        // --- Ignores Regeneration: Anti-Regen, Puncture, Obliterate, Disintegrate -------------------------------

        // A Regeneration defender ignores each wound on a 5+: of the 1 wound, 2/6 is shrugged off. A weapon that
        // ignores Regeneration lands the whole wound.
        private static IEnumerable<TestCaseData> RegenerationIgnorers()
        {
            yield return new TestCaseData("Anti-Regen");
            yield return new TestCaseData("Puncture");
            yield return new TestCaseData("Obliterate");
            yield return new TestCaseData("Disintegrate");
        }

        private static SpecialRuleDefinition ByName(string name) => name switch
        {
            "Anti-Regen" => WeaponRules36Definitions.AntiRegen,
            "Puncture" => WeaponRules36Definitions.Puncture,
            "Obliterate" => WeaponRules36Definitions.Obliterate,
            _ => WeaponRules36Definitions.Disintegrate,
        };

        [TestCaseSource(nameof(RegenerationIgnorers))]
        public async Task TheRegenerationIgnorers_LandEveryWound(string rule)
        {
            DataBinding<UnitData> defender = MakeDefender(models: 3);
            defender.GetValue().AttachRuleDefinition(new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));

            await RunSaves(WeaponWith(ByName(rule)), defender, distance: 10f, failedFaces: new[] { 1 },
                savedFaces: new int[0]);

            Assert.That(Wounds, Is.EqualTo(1f).Within(0.0001f), $"{rule} ignores Regeneration");
        }

        [Test]
        public async Task WithoutAnIgnorer_RegenerationShrugsOffTwoSixths()
        {
            DataBinding<UnitData> defender = MakeDefender(models: 3);
            defender.GetValue().AttachRuleDefinition(new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));

            await RunSaves(new Weapon("Plain", 24f, 1, 0), defender, distance: 10f, failedFaces: new[] { 1 },
                savedFaces: new int[0]);

            Assert.That(Wounds, Is.EqualTo(4f / 6f).Within(0.0001f), "the control: 5+ ignores 2/6");
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private async Task RunSaves(Weapon weapon, DataBinding<UnitData> defender, float distance,
            bool isMelee = false, int[]? failedFaces = null, int[]? savedFaces = null, int saveNeeded = 4)
        {
            DataBinding<UnitData> attacker = MakeUnit("Shooter", models: 1, z: 0f);
            Place(defender, distance);

            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee);

            int[] failed = failedFaces ?? new[] { 1 };
            int[] saved = savedFaces ?? new[] { 5, 6 };
            var failedList = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(failed), new PendingSaveRolls(TestDice.Faces(failed), saveNeeded)),
            };
            var savedList = new List<SuccessfulSaveInfo>();
            if (saved.Length > 0)
            {
                savedList.Add(new SuccessfulSaveInfo(TestDice.Faces(saved),
                    new PendingSaveRolls(TestDice.Faces(saved), saveNeeded)));
            }
            metadata.AddResult(new RollToSaveResults(savedList, failedList));

            _last = metadata;
            await stage.Enter(metadata);
        }

        private static Weapon WeaponWith(SpecialRuleDefinition rule, float range = 24f)
        {
            var weapon = new Weapon("Test gun", rangeInches: range, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return weapon;
        }

        private DataBinding<UnitData> MakeDefender(int defense = 4, int models = 1, int tough = 1) =>
            MakeUnit("Target", models, z: 10f, defense, tough);

        private DataBinding<UnitData> MakeUnit(string name, int models, float z, int defense = 4, int tough = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                if (tough > 1) model.SetMaxWounds(tough);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: defense,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Base edge to base edge: the attacker sits at z = 0, so the target's models sit at the distance plus the
        // two base radii.
        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count; i++)
            {
                models[i].GetValue().SetPosition(new Position(i * 1.5f, edgeDistance + 2f * BaseRadius));
            }
        }

        private static void Kill(DataBinding<UnitData> unit, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ModelData model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds);
            }
        }
    }
}
