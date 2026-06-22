using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #095 — Special rules are <c>[JsonIgnore]</c> on <see cref="UnitData"/>, <see cref="ModelData"/>,
    /// and <see cref="Weapon"/>; each carrier instead persists them as an STJ blob
    /// (<c>RuleAttachmentPersistence</c>) and <c>GameSaveSerializer.Load</c> replays it via
    /// <c>RehydrateRules</c>. These tests pin that a save→load resume preserves every rule (unit / weapon /
    /// per-model hero / custom / aliased / argumented) and that tokens — which already round-trip on the
    /// <c>[JsonProperty]</c> container — keep working.
    /// <para>
    /// Assertions match by <c>Definition.Name</c> / <c>RequestedName</c>, not by <c>==</c> against the
    /// catalog instance: a deserialized <see cref="SpecialRuleDefinition"/> is a fresh object and record
    /// equality compares its list members by reference, so identity-match would fail even on a perfect
    /// round-trip (see <c>SpecialRuleDefinitionSerializationTests</c>, which covers the graph itself).
    /// </para>
    /// </summary>
    [TestFixture]
    public class RuleRehydrationOnResumeTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        // ──────────────────────────────────────────────────────────────────────
        // Rules survive the round-trip (the #095 fix).
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void UnitScopedRule_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Stealthy Squad",
                new[] { MakeModel(store, new Position(1, 1)) });

            // A unit-scoped runtime rule, exactly as army-load attaches it.
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

            GameDataStore loaded = SaveAndLoad(store);
            UnitData loadedUnit = loaded.GetValue<UnitData>(unit.Reference);

            Assert.That(loadedUnit.RuleDefinitions.Select(r => r.Definition.Name),
                Does.Contain("Stealth"),
                "Unit-scoped rules (e.g. Stealth) must still be attached after a save/load resume.");
        }

        [Test]
        public void WeaponScopedRule_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            // #027 weapon-scoped rule: rides on the weapon, not the unit.
            var rifle = new Weapon("Surge Rifle", rangeInches: 24, attacks: 2, armorPenetration: 0);
            rifle.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));

            DataBinding<ModelData> model = MakeModel(store, new Position(1, 1),
                new List<Weapon> { rifle });
            MakeUnit(store, "Gunners", new[] { model });

            GameDataStore loaded = SaveAndLoad(store);
            ModelData loadedModel = loaded.GetValue<ModelData>(model.Reference);
            Weapon loadedWeapon = loadedModel.Weapons.Single();

            Assert.That(loadedWeapon.RuleDefinitions.Select(r => r.Definition.Name),
                Does.Contain("Surge"),
                "Weapon-scoped rules (#027, e.g. Surge) must still be attached after a save/load resume.");
        }

        [Test]
        public void PerModelHeroRule_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<ModelData> heroModel = MakeModel(store, new Position(1, 1));
            MakeUnit(store, "Hero + Bodyguard",
                new[] { heroModel, MakeModel(store, new Position(2, 1)) });

            // #006 slice F: a hero's own unit-scoped rules are carried onto the hero MODEL so they fire
            // for the hero alone. That per-model carriage is what must survive a resume.
            heroModel.GetValue().AttachRuleDefinition(new ResolvedRule("Furious", CoreRuleCatalog.Furious));

            GameDataStore loaded = SaveAndLoad(store);
            ModelData loadedHero = loaded.GetValue<ModelData>(heroModel.Reference);

            Assert.That(loadedHero.RuleDefinitions.Select(r => r.Definition.Name),
                Does.Contain("Furious"),
                "Per-model (hero, #006 slice F) rules must still be attached after a save/load resume.");
        }

        [Test]
        public void CustomEmbeddedRule_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "House-Rule Squad",
                new[] { MakeModel(store, new Position(1, 1)) });

            // A #059-style custom/embedded definition not in CoreRuleCatalog. Approach B persists the whole
            // resolved graph, so this rehydrates with no resolver and no (vestigial-on-resume) army file.
            SpecialRuleDefinition custom = new SpecialRuleDefinition(
                "HouseRuleOfDoom",
                Passive: new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(6),
                        ELifetime.ThisAttack),
                },
                Activated: Array.Empty<ActivatedAbility>());
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("HouseRuleOfDoom", custom));

            GameDataStore loaded = SaveAndLoad(store);
            UnitData loadedUnit = loaded.GetValue<UnitData>(unit.Reference);

            ResolvedRule? rehydrated = loadedUnit.RuleDefinitions
                .FirstOrDefault(r => r.Definition.Name == "HouseRuleOfDoom");
            Assert.That(rehydrated, Is.Not.Null,
                "Custom/embedded (#059) rule definitions must survive a save/load resume.");
            Assert.That(rehydrated!.Definition.Passive, Has.Count.EqualTo(1),
                "The custom rule's hook graph must round-trip, not just its name.");
        }

        [Test]
        public void AliasedRule_PreservesRequestedName()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Aliased Squad",
                new[] { MakeModel(store, new Position(1, 1)) });

            // A core rule attached under an alias (RequestedName != Definition.Name) — UI shows the alias.
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Healing Pods", CoreRuleCatalog.Regeneration));

            GameDataStore loaded = SaveAndLoad(store);
            UnitData loadedUnit = loaded.GetValue<UnitData>(unit.Reference);

            ResolvedRule rehydrated = loadedUnit.RuleDefinitions.Single();
            Assert.That(rehydrated.RequestedName, Is.EqualTo("Healing Pods"),
                "The requested (alias) name must survive so the UI can still show it.");
            Assert.That(rehydrated.Definition.Name, Is.EqualTo("Regeneration"),
                "The resolved definition behind the alias must survive too.");
        }

        [Test]
        public void IntArgument_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Tough Squad",
                new[] { MakeModel(store, new Position(1, 1)) });

            // Tough(3): the rule object (with its argument) must survive so a future GUI can show "Tough(3)".
            // The max-wounds creation EFFECT is deliberately not re-applied on load (gated in FDGServer).
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Tough", CoreRuleCatalog.Tough,
                new RuleArgument[] { new RuleArgument.Int(3) }));

            GameDataStore loaded = SaveAndLoad(store);
            UnitData loadedUnit = loaded.GetValue<UnitData>(unit.Reference);

            ResolvedRule tough = loadedUnit.RuleDefinitions.Single(r => r.Definition.Name == "Tough");
            Assert.That(tough.Arguments.Count, Is.EqualTo(1));
            Assert.That(tough.Arguments[0], Is.EqualTo(new RuleArgument.Int(3)),
                "A rule's Int argument (e.g. Tough(3)) must survive a save/load resume.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Rehydrated rules don't just persist — they FUNCTION. These run a loaded
        // unit through real machinery and assert the rule actually fires.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task RehydratedRule_FiresThroughHitRollStage()
        {
            // Defender carries Stealth (-1 to hit beyond 9"). Save, load, then run the REAL
            // DetermineHitRollStage on the LOADED units (its own RuleEvaluator, the same path live combat
            // uses). The rehydrated rule must still raise the hit threshold 4 → 5 — proving it fires, not
            // merely that RuleDefinitions is non-empty.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> attacker = MakeUnit(store, "Attacker",
                new[] { MakeModel(store, new Position(0, 5)) });
            DataBinding<UnitData> defender = MakeUnit(store, "Defender",
                new[] { MakeModel(store, new Position(20, 5)) }); // ~19" base-to-base → > 9"
            defender.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

            GameDataStore loaded = SaveAndLoad(store);

            var ctx = new TestGameContext(loaded, new FixedDiceRoller(4)); // quality 4 → base hit needed 4
            DataBinding<UnitData> loadedAttacker = loaded.GetDataBinding<UnitData>(attacker.Reference);
            DataBinding<UnitData> loadedDefender = loaded.GetDataBinding<UnitData>(defender.Reference);

            var stage = new DetermineHitRollStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, loadedAttacker, loadedDefender, weapon, weaponCount: 1);

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, +1 because the rehydrated Stealth applies -1 to hit from beyond 9\".");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Realistic full save composition — teams + armies + GameProgressData
        // alongside ruled units, so the rehydration walk runs amid the entries a
        // real mid-game save carries (not just a bare unit/model store).
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void FullSaveWithTeamsArmiesAndProgress_RehydratesRulesAndResumeState()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            var player = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { player }));

            var rifle = new Weapon("Surge Rifle", rangeInches: 24, attacks: 2, armorPenetration: 0);
            rifle.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));
            DataBinding<ModelData> model = MakeModel(store, new Position(1, 1), new List<Weapon> { rifle });
            DataBinding<UnitData> unit = MakeUnit(store, "Stealthy Gunners", new[] { model });
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unit }));

            GameProgressUtilities.WriteProgress(store, new GameProgressData(
                stage: EResumeStage.MainPhase,
                roundCount: 2,
                teamActivateOrder: new List<int> { 0 },
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 } },
                unactivatedUnits: new List<DataBinding<UnitData>> { unit },
                settings: GameSettings.GetDefault()));

            GameDataStore loaded = SaveAndLoad(store);

            // Resume flow state survives.
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(loaded);
            Assert.That(progress, Is.Not.Null);
            Assert.That(progress!.RoundCount, Is.EqualTo(2));

            // Both unit- and weapon-scoped rules rehydrate amid the fuller composition.
            UnitData loadedUnit = loaded.GetValue<UnitData>(unit.Reference);
            Assert.That(loadedUnit.RuleDefinitions.Select(r => r.Definition.Name), Does.Contain("Stealth"));
            Assert.That(loadedUnit.Models.Single().Weapons.Single().RuleDefinitions.Select(r => r.Definition.Name),
                Does.Contain("Surge"));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Tokens — the control. They live on a [JsonProperty] container and survive
        // independently of the rule blob; these guard that nothing regressed.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SelfOwnedToken_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Shaken Squad",
                new[] { MakeModel(store, new Position(1, 1)) });

            unit.GetValue().Tokens.AddToken(
                new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            GameDataStore loaded = SaveAndLoad(store);
            UnitData loadedUnit = loaded.GetValue<UnitData>(unit.Reference);

            Assert.That(loadedUnit.Tokens.HasToken(TokenType.Shaken), Is.True,
                "Self-owned tokens (e.g. Shaken) should survive a save/load resume.");
        }

        [Test]
        public void CrossUnitEmbarkedInToken_SurvivesSaveLoad()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> transport = MakeUnit(store, "Rhino",
                new[] { MakeModel(store, new Position(5, 5)) });
            DataBinding<UnitData> passengers = MakeUnit(store, "Passengers",
                new[] { MakeModel(store, new Position(1, 1)) });

            // #035 EmbarkedIn: a cross-unit token whose OwnerUnitID points at the transport.
            UnitID transportId = transport.GetValue().ID;
            passengers.GetValue().Tokens.AddToken(
                new Token(TokenType.EmbarkedIn, 1, new TokenClearTrigger.ManualOnly(),
                    OwnerUnitID: transportId));

            GameDataStore loaded = SaveAndLoad(store);
            UnitData loadedPassengers = loaded.GetValue<UnitData>(passengers.Reference);

            Token? embarked = loadedPassengers.Tokens.GetAllTokens(TokenType.EmbarkedIn).FirstOrDefault();
            Assert.That(embarked, Is.Not.Null,
                "The EmbarkedIn token (#035) should survive a save/load resume.");
            Assert.That(embarked!.OwnerUnitID, Is.EqualTo(transportId),
                "The cross-unit OwnerUnitID back-reference to the transport must survive too.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers (mirror GameProgressTests)
        // ──────────────────────────────────────────────────────────────────────

        private static GameDataStore SaveAndLoad(GameDataStore store)
        {
            string json = GameSaveSerializer.Save(store);
            return GameSaveSerializer.Load(json);
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position p,
            List<Weapon>? weapons = null)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: weapons ?? new List<Weapon>(),
                initialPosition: p,
                gameDataStore: store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, string name,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(Player, name, quality: 4, defense: 4,
                modelBindings: models.ToList());
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
