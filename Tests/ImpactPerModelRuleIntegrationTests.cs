using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #447 vertical slice: Impact(X) is per model. Core Rules v3.6.0: "When charging, before either unit attacks,
    // roll X dice, unless this unit is Fatigued. For each 2+ the target takes 1 hit." Like every rule a model
    // carries, X is each model's, so a 5-model Impact(1) unit rolls 5 dice - the engine used to roll 1 (the rule
    // fired once per unit). Driven through the REAL ResolveImpactHitsStage, like ImpactRuleIntegrationTests:
    // RecordingDiceRoller's first entry is the impact dice thrown, an empty list proves none were.
    //   - the pool is the sum over the charger's LIVING models of each carrying model's X: unit-attached copies
    //     count the native models, model-attached ones (a joined hero's own Impact, a #407 per-model grant) their
    //     carriers, a unit-wide grant every model;
    //   - Brace(X) (#422) subtracts from that total, floored at 0; the Fatigue gate still zeroes it;
    //   - the log line and the dice beat show the sum ("5 models x Impact(1)").
    [TestFixture]
    public class ImpactPerModelRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // ---- the per-model sum --------------------------------------------------------------------------------

        [Test]
        public async Task FiveModels_WithImpactOne_RollFiveDice()
        {
            DataBinding<UnitData> riders = MakeUnit(5, "Iron Tusk-Beast Riders");
            AttachImpact(riders.GetValue(), 1);

            Run run = await RunStage(riders, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(5f), "one Impact(1) die per model, not one per unit");
            Assert.That(run.Lines, Has.Member(
                "Iron Tusk-Beast Riders' Impact rolled 5 dice (5 models x Impact(1)) -> 5 hit(s) on the charge."));
        }

        [Test]
        public async Task ThreeModels_TwoCarryingImpactTwo_RollFourDice()
        {
            // A per-model attachment (one shared instance, as a fresh army's #407 grant attaches it).
            DataBinding<UnitData> unit = MakeUnit(3, "Bikers");
            var impactTwo = ImpactRule(2);
            foreach (ModelData model in unit.GetValue().Models.OfType<ModelData>().Take(2))
            {
                model.AttachRuleDefinition(impactTwo);
            }

            Run run = await RunStage(unit, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(4f), "2 carriers x Impact(2); the third model adds none");
            Assert.That(run.Lines.Single(l => l.StartsWith("Bikers' Impact rolled")), Does.Contain("(2 models x Impact(2))"));
        }

        [Test]
        public async Task ThreeModels_TwoCarryingImpactTwo_StillRollFourDiceAfterASaveAndLoad()
        {
            // A resumed game rehydrates each model's rules from its own blob, so the shared attachment comes back as
            // one attachment PER MODEL. Each fires once and counts its own carrier - the same 4 dice.
            DataBinding<UnitData> unit = MakeUnit(3, "Bikers");
            var impactTwo = ImpactRule(2);
            foreach (ModelData model in unit.GetValue().Models.OfType<ModelData>().Take(2))
            {
                model.AttachRuleDefinition(impactTwo);
            }
            DataReference unitRef = unit.Reference;
            DataReference targetRef = MakeUnit(10, "Targets").Reference;

            _store = GameSaveSerializer.Load(GameSaveSerializer.Save(_store));
            DataBinding<UnitData> loaded = _store.GetDataBinding<UnitData>(unitRef);
            List<ResolvedRule> attachments = loaded.GetValue().Models.SelectMany(m => m.RuleDefinitions).ToList();
            Assert.That(attachments, Has.Count.EqualTo(2), "premise: both carriers kept their Impact");
            Assert.That(ReferenceEquals(attachments[0].Arguments, attachments[1].Arguments), Is.False,
                "premise: the reload gave each model its own attachment");

            Run run = await RunStage(loaded, _store.GetDataBinding<UnitData>(targetRef));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(4f));
            Assert.That(run.Lines.Single(l => l.StartsWith("Bikers' Impact rolled")), Does.Contain("(2 models x Impact(2))"),
                "the two one-model attachments read as one source");
        }

        [Test]
        public async Task AJoinedHerosImpactThree_AddsToAFiveModelImpactOneUnit_EightDice()
        {
            DataBinding<UnitData> knights = MakeUnitWithHero("Knights", hostModels: 5, hostImpact: 1, heroImpact: 3);

            Run run = await RunStage(knights, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(8f), "5 x Impact(1) + the hero's Impact(3)");
            Assert.That(run.Lines, Has.Member(
                "Knights' Impact rolled 8 dice (5 models x Impact(1) + 1 model x Impact(3)) -> 8 hit(s) on the charge."));
        }

        [Test]
        public async Task AJoinedHeroWithoutImpact_AddsNothing_TheHostsImpactCountsItsOwnModels()
        {
            DataBinding<UnitData> knights = MakeUnitWithHero("Knights", hostModels: 5, hostImpact: 1, heroImpact: null);

            Run run = await RunStage(knights, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(5f),
                "a joined hero does not inherit the host's rules, so it is not a sixth Impact(1) model");
        }

        [Test]
        public async Task AJoinedHerosImpact_InAUnitWithoutImpact_RollsJustTheHerosDice()
        {
            DataBinding<UnitData> squad = MakeUnitWithHero("Squad", hostModels: 5, hostImpact: null, heroImpact: 3);

            Run run = await RunStage(squad, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(3f), "the hero's model carries it; the squad does not");
        }

        [Test]
        public async Task CasualtiesBeforeTheCharge_ReduceTheDice()
        {
            // E.g. the #446 activation Dangerous terrain test: models lost before the charge carry no dice in.
            DataBinding<UnitData> riders = MakeUnit(5, "Riders");
            AttachImpact(riders.GetValue(), 1);
            foreach (IModel model in riders.GetValue().Models.Take(2))
            {
                model.DealWounds(model.TotalWounds);
            }

            Run run = await RunStage(riders, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(3f), "3 living models x Impact(1)");
        }

        [Test]
        public async Task OnlyTheJoinedHeroLeft_WithoutImpact_RollsNoDice()
        {
            DataBinding<UnitData> knights = MakeUnitWithHero("Knights", hostModels: 3, hostImpact: 2, heroImpact: null);
            ModelID heroId = knights.GetValue().JoinedHeroModelId!.Value;
            foreach (IModel model in knights.GetValue().Models.Where(m => m.ID != heroId))
            {
                model.DealWounds(model.TotalWounds);
            }

            Run run = await RunStage(knights, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts, Is.Empty, "every Impact carrier is dead; the hero never had it");
        }

        // ---- Brace and the Fatigue gate still apply to the total ---------------------------------------------

        [Test]
        public async Task Brace_SubtractsFromThePerModelTotal()
        {
            DataBinding<UnitData> riders = MakeUnit(5, "Riders");
            AttachImpact(riders.GetValue(), 2);
            DataBinding<UnitData> spears = MakeUnit(3, "Spears");
            AttachBrace(spears.GetValue(), 1);

            Run run = await RunStage(riders, spears);

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(7f), "5 x Impact(2) = 10, minus Brace(1) x 3 = 7");
            Assert.That(run.Lines, Has.Member(
                "Riders' Impact rolled 7 dice (5 models x Impact(2) - 3 Brace) -> 7 hit(s) on the charge."));
        }

        [Test]
        public async Task Brace_FloorsAtZero_AndTheLogSaysWhy()
        {
            DataBinding<UnitData> riders = MakeUnit(2, "Riders");
            AttachImpact(riders.GetValue(), 1);
            DataBinding<UnitData> spears = MakeUnit(3, "Spears");
            AttachBrace(spears.GetValue(), 1);

            Run run = await RunStage(riders, spears);

            Assert.That(run.Roller.RollCounts, Is.Empty, "2 dice minus 3 cancelled: nothing to roll");
            Assert.That(spears.RemainingWounds(), Is.EqualTo(3f));
            Assert.That(run.Lines, Has.Member("Riders' Impact rolls no dice (2 models x Impact(1) - 3 Brace)."));
        }

        [Test]
        public async Task AFatiguedMultiModelCharger_StillRollsNoDice()
        {
            DataBinding<UnitData> riders = MakeUnit(5, "Riders");
            AttachImpact(riders.GetValue(), 1);
            FatigueUtilities.ApplyFatigued(riders.GetValue());

            Run run = await RunStage(riders, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts, Is.Empty);
            Assert.That(run.Lines.Any(l => l.Contains("Impact")), Is.False, "a gated Impact fires nothing to narrate");
        }

        // ---- a unit-wide grant, Heavy Impact ------------------------------------------------------------------

        [Test]
        public async Task AUnitWideGrant_CountsEveryLivingModel_TheJoinedHeroIncluded()
        {
            // A grant (an aura or "gains Impact" buff) names a rule with no per-model attachment; grants carry no
            // arguments, so the granted rule's X is a literal. It covers the current unit, hero included (#183).
            DataBinding<UnitData> squad = MakeUnitWithHero("Squad", hostModels: 3, hostImpact: null, heroImpact: null);
            squad.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                new TokenPayload.RuleGrant(GrantedImpact.Name, ELifetime.Aura)));
            var resolver = new RuleResolver();
            resolver.Register(GrantedImpact);

            Run run = await RunStage(squad, MakeUnit(10, "Targets"), resolver: resolver);

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(4f), "3 squad models + the hero, 1 die each");
            Assert.That(run.Lines.Single(l => l.StartsWith("Squad's Impact rolled")),
                Does.Contain("(4 models x Charging Frenzy(1))"));
        }

        [Test]
        public async Task HeavyImpact_IsPerModelToo_AndKeepsItsAP()
        {
            // AllOnFace 4: every impact die hits; an AP(1) save of 4 against Defense 4 fails, so 3 wounds land.
            DataBinding<UnitData> rams = MakeUnit(3, "Rams");
            rams.GetValue().AttachRuleDefinition(new ResolvedRule("Heavy Impact", HeavyImpact,
                new RuleArgument[] { new RuleArgument.Int(1) }));
            DataBinding<UnitData> targets = MakeUnit(10, "Targets");

            Run run = await RunStage(rams, targets, face: 4);

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(3f), "3 models x Heavy Impact(1)");
            Assert.That(targets.RemainingWounds(), Is.EqualTo(7f), "the AP(1) fold survives the per-model sum");
            Assert.That(run.Lines.Single(l => l.StartsWith("Rams' Impact rolled")), Does.Contain("(3 models x Heavy Impact(1))"));
        }

        // ---- presentation -------------------------------------------------------------------------------------

        [Test]
        public async Task TheDiceBeat_ShowsTheRealCount_AndThePoolAsItsContext()
        {
            DataBinding<UnitData> knights = MakeUnitWithHero("Knights", hostModels: 5, hostImpact: 1, heroImpact: 3);
            var presenter = new RecordingPresenter();

            await RunStage(knights, MakeUnit(10, "Targets"), presenter: presenter);

            DiceRolledBeat beat = presenter.Beats.OfType<DiceRolledBeat>().Single(b => b.Label == "Impact Hits");
            Assert.That(beat.Total, Is.EqualTo(8f));
            Assert.That(beat.Context, Is.EqualTo("Knights: 5 models x Impact(1) + 1 model x Impact(3)"));
            Assert.That(beat.Category, Is.EqualTo(ERollBeatCategory.Offense));
        }

        [Test]
        public async Task AJoinedHerosRavage_IsNotNarratedByTheImpactStage()
        {
            // Walking the charger's models reaches the hero's Ravage too. ResolveRavageWoundsStage resolves (and
            // narrates) Ravage; the impact stage only narrates the impact dice, so it never reports a Ravage roll
            // that does not happen here.
            DataBinding<UnitData> knights = MakeUnitWithHero("Knights", hostModels: 5, hostImpact: 1, heroImpact: null,
                heroExtra: new ResolvedRule("Ravage", CoreRuleCatalog.Ravage, new RuleArgument[] { new RuleArgument.Int(2) }));

            Run run = await RunStage(knights, MakeUnit(10, "Targets"));

            Assert.That(run.Lines, Has.Some.Contains("Knights's Impact rolled 5 impact dice on the charge (5 models x 1)"),
                "the evaluator still narrates the Impact op");
            Assert.That(run.Lines.Where(l => l.Contains("Ravage")), Is.Empty);
        }

        [Test]
        public void DescribeDicePool_MergesSourcesAtTheSameRuleAndX_AndListsCancels()
        {
            // Army files attach numeric rules under their printable name ("Impact(2)"); the value is dropped so the
            // X shown is the per-model one.
            var named = new List<(RuleOperation, string)>
            {
                (new RuleOperation.ChargeImpactHits(2, Models: 1), "Impact(2)"),
                (new RuleOperation.ChargeImpactHits(2, Models: 1), "Impact"),
                (new RuleOperation.ChargeImpactHits(3, Models: 1), "Impact(3)"),
                (new RuleOperation.ChargeImpactHits(6, ArmorPenetration: 1, Models: 3), "Heavy Impact(2)"),
                (new RuleOperation.ChargeImpactHits(-2, Models: 2), "Brace(1)"),
                (new RuleOperation.ChargeImpactHits(-1, Models: 1), "Brace"),
                (new RuleOperation.StrikeFirst(), "Counter"),
            };

            Assert.That(ResolveImpactHitsStage.DescribeDicePool(named), Is.EqualTo(
                "2 models x Impact(2) + 1 model x Impact(3) + 3 models x Heavy Impact(2) - 3 Brace"));
        }

        // ---- a per-model item compiles to a per-model grant ---------------------------------------------------

        [Test]
        public void Compile_OneModelImpactItem_IsAModelGrant_NotUnitWide()
        {
            // 3.5's Orc Marauders Hover Bikers: "Replace one CCW -> Rotor Blade Attack (Impact(3))". Folded onto the
            // unit it would now mean 3 x Impact(3) = 9 dice; it is one model's, so it is a #407 model grant.
            UnitFileEntry bikers = ListCompiler.Compile(HoverBikerBook(), HoverBikerList()).Units.Single();

            Assert.That(bikers.SpecialRules.OfType<SpecialRuleEntry_CoreNumeric>().Where(r => r.Name == "Impact"), Is.Empty);
            Assert.That(bikers.ModelGrants, Has.Count.EqualTo(1));
            Assert.That(bikers.ModelGrants![0].Count, Is.EqualTo(1));
            Assert.That(bikers.ModelGrants[0].Rules, Is.EqualTo(new[] { new SpecialRuleEntry_CoreNumeric("Impact", 3) }));
        }

        [Test]
        public async Task ArmyLoad_OneModelImpactItem_RollsThatModelsDiceOnly()
        {
            BuiltArmyFile army = ListCompiler.Compile(HoverBikerBook(), HoverBikerList());
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, CoreRuleCatalog.CreateResolver());
            _store = store;
            DataBinding<UnitData> bikers = store.GetAllDataBindings<UnitData>().Single();

            Run run = await RunStage(bikers, MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(3f), "one model x Impact(3), not three");
        }

        [Test]
        public async Task Compile_ReplacingOneProfileItemWithTheSameItem_KeepsItUnitWide()
        {
            // 3.6.0 Dragon Empire Dragonkin Horsemen / Ghostly Undead Dread Knights shape: every model's profile
            // carries "Trample (Impact(1))", and "Replace one Lance and Trample" gives the one model a new Trample.
            // Every model still holds one, so the unit keeps Impact(1) - not a one-model grant that would leave the
            // other four without it (1 die instead of 5).
            BuiltArmyFile army = ListCompiler.Compile(HorsemenBook(), HorsemenList());
            UnitFileEntry horsemen = army.Units.Single();

            Assert.That(horsemen.SpecialRules, Does.Contain(new SpecialRuleEntry_CoreNumeric("Impact", 1)));
            Assert.That(horsemen.ModelGrants, Is.Null);

            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, CoreRuleCatalog.CreateResolver());
            _store = store;

            Run run = await RunStage(store.GetAllDataBindings<UnitData>().Single(), MakeUnit(10, "Targets"));

            Assert.That(run.Roller.RollCounts.First(), Is.EqualTo(5f), "5 models x Impact(1)");
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        private sealed record Run(RecordingDiceRoller Roller, List<string> Lines);

        private async Task<Run> RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int face = 2,
            IPresenter? presenter = null, IRuleResolver? resolver = null)
        {
            var roller = new RecordingDiceRoller(face);
            var log = new RecordingTextOutput();
            var ctx = new LoggingContext(_store, roller, log, presenter, resolver);
            var stage = new ResolveImpactHitsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnImpactResolved.Bind("done");

            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);
            await stage.Enter(context);

            return new Run(roller, log.Entries.Select(e => e.Message).ToList());
        }

        private static ResolvedRule ImpactRule(int x) =>
            new ResolvedRule("Impact", CoreRuleCatalog.Impact, new RuleArgument[] { new RuleArgument.Int(x) });

        private static void AttachImpact(UnitData unit, int x) => unit.AttachRuleDefinition(ImpactRule(x));

        private static void AttachBrace(UnitData unit, int x) =>
            unit.AttachRuleDefinition(new ResolvedRule("Brace", CoreRuleCatalog.Brace,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        // Heavy Impact's shipped shape (GdfRuleSupplement.json): core Impact's entry with AP(1) on the hits.
        private static readonly SpecialRuleDefinition HeavyImpact = new SpecialRuleDefinition("Heavy Impact",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact,
                    new Condition.Not(new Condition.CountsAsFatiguedInMelee()),
                    new Effect.ChargeImpactHits(new ValueSource.Arg(0), ArmorPenetration: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        // A grantable Impact: the same entry with a literal X (a RuleGrant token carries no arguments).
        private static readonly SpecialRuleDefinition GrantedImpact = new SpecialRuleDefinition("Charging Frenzy",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact,
                    new Condition.Not(new Condition.CountsAsFatiguedInMelee()),
                    new Effect.ChargeImpactHits(new ValueSource.Literal(1)),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        // A host unit with a joined hero (the real #006 merge, which moves the hero's rules onto its model).
        private DataBinding<UnitData> MakeUnitWithHero(string name, int hostModels, int? hostImpact, int? heroImpact,
            ResolvedRule? heroExtra = null)
        {
            DataBinding<UnitData> hostBinding = MakeUnit(hostModels, name);
            UnitData host = hostBinding.GetValue();
            if (hostImpact is int hx) AttachImpact(host, hx);

            UnitData hero = MakeUnit(1, "Hero").GetValue();
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroImpact is int hix) AttachImpact(hero, hix);
            if (heroExtra != null) hero.AttachRuleDefinition(heroExtra);

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });
            Assert.That(host.JoinedHeroModelId, Is.Not.Null, "premise: the hero joined");
            return hostBinding;
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, string name)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static BookFile HoverBikerBook() => new()
        {
            Name = "Test Book", Faction = "Testers",
            Units =
            {
                new RosterUnit
                {
                    Id = "bikers", Name = "Hover Bikers", Quality = 4, Defense = 4,
                    BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "CCW", Quantity = 3, RangeInches = 0, Attacks = 1 } },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "blade-section", Label = "Replace one CCW",
                            Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One, Targets = { "CCW" },
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "rotor", Label = "Rotor Blade Attack (Impact(3))", Cost = 10,
                                    ItemsGained =
                                    {
                                        new ItemEntry
                                        {
                                            Name = "Rotor Blade Attack", Quantity = 1,
                                            Rules = { new SpecialRuleEntry_CoreNumeric("Impact", 3) },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        private static BookFile HorsemenBook() => new()
        {
            Name = "Test Book", Faction = "Testers",
            Units =
            {
                new RosterUnit
                {
                    Id = "horsemen", Name = "Horsemen", Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Lance", Quantity = 5, RangeInches = 0, Attacks = 1 } },
                    Items =
                    {
                        new ItemEntry { Name = "Trample", Quantity = 5, Rules = { new SpecialRuleEntry_CoreNumeric("Impact", 1) } },
                    },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "lance-section", Label = "Replace one Lance and Trample",
                            Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One, Targets = { "Lance", "Trample" },
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "glaive", Label = "Glaive (A2), Trample (Impact(1))", Cost = 5,
                                    WeaponsGained = { new WeaponFileEntry { Name = "Glaive", Quantity = 1, RangeInches = 0, Attacks = 2 } },
                                    ItemsGained =
                                    {
                                        new ItemEntry
                                        {
                                            Name = "Trample", Quantity = 1,
                                            Rules = { new SpecialRuleEntry_CoreNumeric("Impact", 1) },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        private static BuilderList HorsemenList() => new()
        {
            Name = "Test", BookName = "Test Book", PointsLimit = 1000,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = "horsemen", ModelCount = 5,
                    Choices = { new UpgradeChoice { SectionId = "lance-section", OptionId = "glaive", Count = 1 } },
                },
            },
        };

        private static BuilderList HoverBikerList() => new()
        {
            Name = "Test", BookName = "Test Book", PointsLimit = 1000,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = "bikers", ModelCount = 3,
                    Choices = { new UpgradeChoice { SectionId = "blade-section", OptionId = "rotor", Count = 1 } },
                },
            },
        };

        // Returns EVERY rolled die on one face and records each roll's size: the first entry is the impact dice.
        private sealed class RecordingDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public List<float> RollCounts { get; } = new();
            public RecordingDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                RollCounts.Add(rollCount);
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }

        // WoundTestContext with a recording log wired into BOTH the game log and the rule evaluator's narration.
        private sealed class LoggingContext : IGameContext
        {
            public ITextOutput TextOutput { get; }
            public IDiceRoller DiceRoller { get; }
            public Random Rng { get; } = new Random(20261030);
            public RuleEvaluator RuleEvaluator { get; }
            public IPlayerRequestByID PlayerRequester { get; } = new CapturingWoundRequester();
            public TableState TableState { get; }
            public IReadWriteableGameDataStore GameDataStore { get; }
            public IPresenter Presenter { get; }
            public GameSettings Settings { get; } = GameSettings.GetDefault();
            public List<ITeam>? FirstDeploymentRollOrder => null;
            IGameContext IGameContextAccessor.GameContext => this;

            public LoggingContext(GameDataStore store, IDiceRoller dice, ITextOutput log, IPresenter? presenter,
                IRuleResolver? resolver)
            {
                GameDataStore = store;
                TableState = new TableState(store);
                DiceRoller = dice;
                TextOutput = log;
                RuleEvaluator = new RuleEvaluator(dice, log, resolver);
                Presenter = presenter ?? new LocalPresenter(null, new InstantPresentationClock());
            }

            public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
            public void NotifyGameCompleted(GameResult result) { }
        }
    }
}
