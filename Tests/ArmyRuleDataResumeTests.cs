using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #095 residual — the army-LEVEL half of resume rehydration, sibling to
    /// <see cref="RuleRehydrationOnResumeTests"/> (which covers rules ATTACHED to a unit / model / weapon).
    /// <para>
    /// Two things an army carries are named rather than attached, so per-carrier persistence never reached
    /// them: the embedded (#059) definitions a <c>RuleGrant</c> token looks up BY NAME in the shared
    /// resolver, and <c>ArmyData.Spells</c> (#033), which is <c>[JsonIgnore]</c> and only ever set at army
    /// load. On resume the per-slot army file is vestigial and <c>GameBootstrap.CreateArmy</c> doesn't run,
    /// so both were lost: every grant of a supplement rule logged "has no definition in the registry" and
    /// did nothing, and a resumed Caster was offered an empty spell list.
    /// </para>
    /// <para>
    /// The fix persists both lists on <see cref="ArmyData"/> and replays them in
    /// <c>GameBootstrap.RestoreArmyRuleData</c>, which the resume <c>FDGServer</c> constructor calls right
    /// after building the resolver. These tests drive that exact pair of calls.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ArmyRuleDataResumeTests
    {
        private static readonly PlayerID Player = new PlayerID(Guid.NewGuid());

        // A #059-style embedded rule, not in the core catalog: ignores wounds on 4+. Deliberately a
        // DIFFERENT threshold from core Regeneration's 5+, so an assertion can't pass by accidentally
        // reading a core rule instead of the restored one.
        private const string EmbeddedRuleName = "Warp Knitting";

        private static SpecialRuleDefinition EmbeddedWoundIgnore() => new SpecialRuleDefinition(
            EmbeddedRuleName,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.Always(),
                    new Effect.IgnoreWoundOnRoll(MinRoll: 4),
                    ELifetime.ThisAttack,
                    ERuleSeat.Subject),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: EValence.Positive,
            Description: "Ignores each wound on a roll of 4+.");

        // ──────────────────────────────────────────────────────────────────────
        // Granted embedded rules: the residual this fix closes.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void GrantedEmbeddedRule_WithoutRestore_IsInertAfterResume()
        {
            // The state before the fix: the RuleGrant token round-trips, but the resume resolver holds only
            // the core catalog, so the name it points at resolves to nothing.
            (GameDataStore loaded, DataReference defender, DataReference attacker, _) = SaveAndLoadArmyWithGrant();

            IReadOnlyList<RuleOperation> ops = EvaluateSaveRollComplete(loaded, defender, attacker,
                CoreRuleCatalog.CreateResolver());

            Assert.That(ops.OfType<RuleOperation.IgnoreWound>(), Is.Empty,
                "with only the core catalog registered, a grant naming an embedded rule finds no " +
                "definition and does nothing - the residual this fix closes.");
        }

        [Test]
        public void GrantedEmbeddedRule_FiresAfterResume_OnceArmyRuleDataIsRestored()
        {
            (GameDataStore loaded, DataReference defender, DataReference attacker, _) = SaveAndLoadArmyWithGrant();

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            GameBootstrap.RestoreArmyRuleData(resolver, loaded);

            IReadOnlyList<RuleOperation> ops = EvaluateSaveRollComplete(loaded, defender, attacker, resolver);

            Assert.That(ops.OfType<RuleOperation.IgnoreWound>().Any(op => op.MinRoll == 4), Is.True,
                "the restored embedded definition must let the surviving grant token project again " +
                "(4+, the embedded rule's own threshold - not core Regeneration's 5+).");
        }

        [Test]
        public void RestoredDefinition_StillOverridesACoreRuleOfTheSameName()
        {
            // Registration order matters: core first, then the army's own, so a template that retunes a core
            // rule by name (#059) keeps winning on a resumed game exactly as it did before the save.
            SpecialRuleDefinition overrideStealth = new SpecialRuleDefinition("Stealth",
                new List<HookEntry>(), new List<ActivatedAbility>());

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ArmyData army = new ArmyData(Player, new List<DataBinding<UnitData>>());
            army.PersistRuleData(new[] { overrideStealth }, Array.Empty<SpellDefinition>());
            store.Create(army);

            GameDataStore loaded = SaveAndLoad(store);
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            GameBootstrap.RestoreArmyRuleData(resolver, loaded);

            Assert.That(resolver.TryResolve("Stealth", out ResolvedRule resolved), Is.True);
            Assert.That(resolved.Definition.Passive, Is.Empty,
                "the army's own definition must still override the same-named core rule after a resume " +
                "(core Stealth has a passive hook; this override has none).");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Spell lists: the second casualty of the same gap.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void ArmySpellList_IsEmptyOnLoad_AndRestored()
        {
            SpellDefinition bolt = new SpellDefinition("Warp Bolt", Threshold: 2,
                new TargetSelector(18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.DealHits(3, Array.Empty<string>()));

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ArmyData army = new ArmyData(Player, new List<DataBinding<UnitData>>());
            army.SetSpells(new[] { new RuntimeSpell(bolt, Array.Empty<ResolvedRule>()) });
            army.PersistRuleData(Array.Empty<SpecialRuleDefinition>(), new[] { bolt });
            DataReference armyReference = store.Create(army);

            GameDataStore loaded = SaveAndLoad(store);
            ArmyData loadedArmy = loaded.GetValue<ArmyData>(armyReference);

            Assert.That(loadedArmy.Spells, Is.Empty,
                "precondition: Spells is [JsonIgnore], so the load itself brings back nothing.");

            GameBootstrap.RestoreArmyRuleData(CoreRuleCatalog.CreateResolver(), loaded);

            Assert.That(loadedArmy.Spells.Select(s => s.Definition.Name), Is.EqualTo(new[] { "Warp Bolt" }),
                "a resumed army must offer its Caster the same spell list it had before the save.");
            Assert.That(loadedArmy.Spells.Single().Definition.Threshold, Is.EqualTo(2),
                "the spell's cost must round-trip too, not just its name.");
        }

        [Test]
        public void RestoredSpell_ResolvesItsWeaponRulesAgainstTheRestoredDefinitions()
        {
            // A damage spell whose WithRules names an EMBEDDED weapon rule. This only resolves if every
            // army's definitions are registered before any spell list is resolved - the ordering
            // RestoreArmyRuleData deliberately keeps.
            SpecialRuleDefinition embeddedWeaponRule = new SpecialRuleDefinition("Warp Edge",
                new List<HookEntry>(), new List<ActivatedAbility>(), Scope: ERuleScope.Weapon);

            SpellDefinition lance = new SpellDefinition("Warp Lance", Threshold: 3,
                new TargetSelector(24f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.DealHits(2, new[] { "Warp Edge" }));

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ArmyData army = new ArmyData(Player, new List<DataBinding<UnitData>>());
            army.PersistRuleData(new[] { embeddedWeaponRule }, new[] { lance });
            DataReference armyReference = store.Create(army);

            GameDataStore loaded = SaveAndLoad(store);
            GameBootstrap.RestoreArmyRuleData(CoreRuleCatalog.CreateResolver(), loaded);

            RuntimeSpell restored = loaded.GetValue<ArmyData>(armyReference).Spells.Single();
            Assert.That(restored.WeaponRules.Select(r => r.Definition.Name), Does.Contain("Warp Edge"),
                "the synthetic spell weapon's embedded rules must resolve on resume - so a resumed " +
                "damage spell hits as hard as it did before the save.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Producer side + tolerance.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void CreateArmy_PersistsTheArmyFilesRuleDataOntoTheArmy()
        {
            // The write half: army load is the only place the file is in hand, so it is the only place that
            // can record these two lists for a later resume.
            SpellDefinition bolt = new SpellDefinition("Warp Bolt", Threshold: 2,
                new TargetSelector(18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.DealHits(3, Array.Empty<string>()));
            ArmyListFile file = new ArmyListFile
            {
                Name = "Warp Cult",
                RuleDefinitions = new List<SpecialRuleDefinition> { EmbeddedWoundIgnore() },
                Spells = new List<SpellDefinition> { bolt },
            };

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameBootstrap.CreateArmy(Player, file, store, CoreRuleCatalog.CreateResolver());

            ArmyData army = store.GetAllValues<ArmyData>().Single();
            var persisted = army.RestoreRuleData();

            Assert.That(persisted, Is.Not.Null, "army load must record the file's rule data on the army.");
            Assert.That(persisted!.RuleDefinitions.Select(d => d.Name), Is.EqualTo(new[] { EmbeddedRuleName }));
            Assert.That(persisted.Spells.Select(s => s.Name), Is.EqualTo(new[] { "Warp Bolt" }));
        }

        [Test]
        public void RestoreArmyRuleData_WithNoPersistedBlob_IsANoOp()
        {
            // An ArmyData built outside army load (tests, and any pre-#095 save) carries no blob. Restoring
            // must skip it quietly rather than throw - a resume that dies on an old save is worse than one
            // that resumes without the extras.
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            store.Create(new ArmyData(Player, new List<DataBinding<UnitData>>()));

            GameDataStore loaded = SaveAndLoad(store);
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            Assert.DoesNotThrow(() => GameBootstrap.RestoreArmyRuleData(resolver, loaded));
            Assert.That(resolver.TryResolve("Stealth", out _), Is.True, "core rules remain registered.");
        }

        [Test]
        public void RestoreArmyRuleData_OnAStoreWithNoArmies_IsANoOp()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            Assert.DoesNotThrow(() =>
                GameBootstrap.RestoreArmyRuleData(CoreRuleCatalog.CreateResolver(), store));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        // A saved-and-reloaded world in the shape the residual was found in: a defender carrying a RuleGrant
        // token that names an EMBEDDED rule, and an army whose persisted data defines it.
        private static (GameDataStore Loaded, DataReference Defender, DataReference Attacker, DataReference Army)
            SaveAndLoadArmyWithGrant()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> defender = MakeUnit(store, "Buffed Squad");
            DataBinding<UnitData> attacker = MakeUnit(store, "Attacker");

            // What an Effect.AddRule / Effect.Aura leaves behind: the rule named as a string, not attached.
            defender.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(),
                new TokenPayload.RuleGrant(EmbeddedRuleName, ELifetime.Aura)));

            ArmyData army = new ArmyData(Player, new List<DataBinding<UnitData>> { defender });
            army.PersistRuleData(new[] { EmbeddedWoundIgnore() }, Array.Empty<SpellDefinition>());
            DataReference armyReference = store.Create(army);

            return (SaveAndLoad(store), defender.Reference, attacker.Reference, armyReference);
        }

        private static IReadOnlyList<RuleOperation> EvaluateSaveRollComplete(GameDataStore store,
            DataReference defender, DataReference attacker, IRuleResolver resolver)
        {
            UnitData loadedDefender = store.GetValue<UnitData>(defender);
            UnitData loadedAttacker = store.GetValue<UnitData>(attacker);

            RuleEvaluator evaluator = new RuleEvaluator(new FixedDiceRoller(4), ruleResolver: resolver);
            return evaluator.Evaluate(loadedDefender, ERuleSeat.Subject,
                new SaveRollCompleteContext(loadedAttacker, loadedDefender, TestDice.Faces(4, 4, 4)));
        }

        private static GameDataStore SaveAndLoad(GameDataStore store)
            => GameSaveSerializer.Load(GameSaveSerializer.Save(store));

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, string name)
        {
            ModelData model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(1, 1), gameDataStore: store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));

            UnitData unit = new UnitData(Player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
