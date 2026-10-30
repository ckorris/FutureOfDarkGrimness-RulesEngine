using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #422 - what 3.6 took out of the Counter family, and how old data still loads (plan rule 6: pre-3.6
    // .fdgarmy files keep loading; they get 3.6 semantics where the engine can give them).
    //   - Counter-Attack and Counter-Attack Aura were dropped by 3.6 and are gone from the catalog. A list
    //     naming them must load with the #168 "not implemented" diagnostic for each, never crash.
    //   - 3.5 Counter's impact facet was an Effect kind ("reduceImpactDicePerModel") that old army files and
    //     the not-yet-rebaked bundled books embed inside "Counter in Melee". The kind still parses, does
    //     nothing (3.6 moved the facet to Brace), passes the army-load gate, and is refused for new data.
    [TestFixture]
    public class CounterLegacyDataTests
    {
        // A 3.5-era "Counter in Melee" exactly as the bundled AoF-ChivalrousKingdoms book embeds it today.
        private const string LegacyCounterInMeleeJson = @"[
          {
            ""name"": ""Counter in Melee"",
            ""passive"": [
              { ""hookID"": ""Melee_OnCounterTrigger"", ""condition"": { ""kind"": ""always"" },
                ""effect"": { ""kind"": ""strikeFirst"" }, ""lifetime"": ""ThisActivation"", ""seat"": ""Subject"" },
              { ""hookID"": ""Melee_OnChargeContact"", ""condition"": { ""kind"": ""always"" },
                ""effect"": { ""kind"": ""reduceImpactDicePerModel"" }, ""lifetime"": ""ThisAttack"", ""seat"": ""Subject"" }
            ],
            ""activated"": [],
            ""scope"": ""Weapon"",
            ""engineArgumentCount"": 0,
            ""valence"": ""Positive"",
            ""description"": ""When charged, this unit strikes first and the charger rolls one fewer Impact die per living model.""
          }
        ]";

        private static SpecialRuleDefinition LegacyCounterInMelee() =>
            BookRuleSupplement.LoadDefinitions(LegacyCounterInMeleeJson).Single();

        [Test]
        public void CounterAttackFamily_IsGoneFromTheCatalog()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            Assert.That(resolver.TryResolve("Counter-Attack", out _), Is.False, "3.6 dropped Counter-Attack.");
            Assert.That(resolver.TryResolve("Counter-Attack Aura", out _), Is.False, "...and its Aura.");
            Assert.That(resolver.TryResolve("Counter", out _), Is.True);
            Assert.That(resolver.TryResolve("Brace", out _), Is.True);
        }

        [Test]
        public void ALegacyListNamingCounterAttack_LoadsWithANotImplementedDiagnostic()
        {
            ArmyListFile army = LegacyList();

            List<RuleDrop> drops = Launch(army, out UnitData unit);

            Assert.That(drops.Select(d => d.RuleName),
                Is.EquivalentTo(new[] { "Counter-Attack", "Counter-Attack Aura" }),
                "each dropped name is reported - nothing else on the list is lost.");
            Assert.That(drops.Select(d => d.Reason), Is.All.EqualTo(ERuleDropReason.Unimplemented));
            Assert.That(unit.Name, Is.EqualTo("Ogre Warriors"), "the army still builds.");
            Assert.That(ArmyRuleAudit.Audit(army).Drops.Select(d => d.RuleName),
                Is.EquivalentTo(new[] { "Counter-Attack", "Counter-Attack Aura" }),
                "the army-builder audit reports the same drops the launch does.");
        }

        [Test]
        public void ALegacyCounterInMelee_StillStrikesFirst_ButCancelsNoImpactDice()
        {
            Launch(LegacyList(), out UnitData legacyUnit);
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            UnitData charger = Charger(impact: 3);

            var contactParticipants = new List<RuleParticipant> { RuleParticipant.Actor(charger) };
            contactParticipants.AddRange(DetermineStrikeOrderStage.SubjectWithMeleeWeapons(legacyUnit));
            var sink = new ImpactSink();
            sink.ApplyFrom(evaluator.EvaluateAll(new ChargeContactContext(charger, legacyUnit),
                contactParticipants.ToArray()));

            IReadOnlyList<RuleOperation> counterOps = evaluator.EvaluateAll(
                new CounterTriggerContext(charger, legacyUnit),
                DetermineStrikeOrderStage.SubjectWithMeleeWeapons(legacyUnit));

            Assert.That(sink.TotalDice, Is.EqualTo(3),
                "the embedded 3.5 impact facet loads as a no-op - Impact(3) keeps all 3 dice.");
            Assert.That(counterOps.OfType<RuleOperation.StrikeFirst>(), Is.Not.Empty,
                "the strike-first half of the old definition still works.");
        }

        [Test]
        public void TheLegacyImpactReduction_LoadsAndDoesNothing_ButAuthoringRefusesIt()
        {
            SpecialRuleDefinition legacy = LegacyCounterInMelee();

            HookEntry reduction = legacy.Passive.Single(e => e.HookID == EHookID.Melee_OnChargeContact);
            Assert.That(reduction.Effect, Is.InstanceOf<Effect.LegacyReduceImpactDicePerModel>());

            var produced = new List<RuleOperation>();
            reduction.Effect.Apply(new RuleInvocation(null, Charger(impact: 0), Array.Empty<RuleArgument>()),
                produced);
            Assert.That(produced, Is.Empty, "3.6 Counter cancels no impact dice, so the old facet emits nothing.");

            Assert.That(new RuleValidator().Validate(legacy), Is.Empty,
                "the army-load gate accepts it, or every pre-3.6 file embedding it would refuse to open.");
            IReadOnlyList<RuleViolation> authoring = new RuleValidator().ValidateAuthoring(legacy);
            Assert.That(authoring.Select(v => v.Detail), Has.One.Contains("reduceImpactDicePerModel"),
                "new data must author the reduction as Brace(X) instead.");
        }

        [Test]
        public void TheLegacyKind_RoundTripsUnderItsOldName()
        {
            string json = JsonSerializer.Serialize((Effect)new Effect.LegacyReduceImpactDicePerModel(), RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\": \"reduceImpactDicePerModel\""),
                "re-saving an old army file must not rewrite the kind into something older builds can't read.");
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options),
                Is.InstanceOf<Effect.LegacyReduceImpactDicePerModel>());
        }

        [Test]
        public void TheNewKinds_RoundTrip()
        {
            Effect cancel = new Effect.CancelImpactDice(new ValueSource.Arg(0));
            string effectJson = JsonSerializer.Serialize(cancel, RuleJson.Options);
            Assert.That(effectJson, Does.Contain("\"kind\": \"cancelImpactDice\""));
            Assert.That(JsonSerializer.Deserialize<Effect>(effectJson, RuleJson.Options), Is.EqualTo(cancel));

            Condition gate = new Condition.Not(new Condition.CountsAsFatiguedInMelee());
            string conditionJson = JsonSerializer.Serialize(gate, RuleJson.Options);
            Assert.That(conditionJson, Does.Contain("\"kind\": \"countsAsFatiguedInMelee\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(conditionJson, RuleJson.Options), Is.EqualTo(gate));
        }

        // --- harness ---

        private static ArmyListFile LegacyList() =>
            new ArmyListFile
            {
                Name = "1k - Ogres (saved under 3.5)",
                Faction = "Ogres",
                RuleDefinitions = new List<SpecialRuleDefinition> { LegacyCounterInMelee() },
                Units =
                {
                    new UnitFileEntry
                    {
                        Name = "Ogre Warriors", ModelCount = 3, Quality = 4, Defense = 4,
                        SpecialRules =
                        {
                            new SpecialRuleEntry_Core("Counter-Attack"),
                            new SpecialRuleEntry_Core("Counter-Attack Aura"),
                            new SpecialRuleEntry_Core("Counter in Melee"),
                        },
                        Weapons = { new WeaponFileEntry { Name = "Great Club", Quantity = 3, Attacks = 2 } },
                    },
                },
            };

        private static UnitData Charger(int impact)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(baseRadiusInches: 0.75f,
                weapons: new List<Weapon> { new Weapon("Horns", 0f, 1, 0) },
                initialPosition: new Position(0, 0), gameDataStore: store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Charger", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    store.GetDataBinding<ModelData>(store.Create(model)),
                });
            if (impact > 0)
            {
                unit.AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                    new RuleArgument[] { new RuleArgument.Int(impact) }));
            }
            return unit;
        }

        /// <summary>Runs the real launch path, capturing structured drops and the built unit.</summary>
        private static List<RuleDrop> Launch(ArmyListFile army, out UnitData unit)
        {
            var drops = new List<RuleDrop>();
            void Capture(RuleDrop drop) => drops.Add(drop);

            RuleDiagnostics.OnRuleDropped += Capture;
            try
            {
                GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
                RuleResolver resolver = CoreRuleCatalog.CreateResolver();
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
                GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);

                unit = store.GetAllValues<UnitData>().Single();
            }
            finally
            {
                RuleDiagnostics.OnRuleDropped -= Capture;
            }

            return drops;
        }
    }
}
