using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using Newtonsoft.Json;
using NUnit.Framework;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Tests
{
    // #306 — a unit may carry two weapons that share a NAME but differ in profile: a partial upgrade
    // buying Precise for one of three rifles (#197 slice 0's targeted upgrades, `UpgradeSection.Targets`).
    // The pool groups by WeaponComparer, so those arrive as two distinct entries — but both choosers keyed
    // their maps by `Weapon.Name`, and `Dictionary.Add` throws rather than overwrites, so the split
    // FAULTED THE STATE MACHINE mid-activation ("An item with the same key has already been added:
    // Rifle"). The melee sibling keyed its options by label TEXT, which is unique in practice (a rule's
    // requested name carries its argument, "Deadly(3)") but was guaranteed by nothing.
    //
    // Both now key on the weapon PROFILE (WeaponProfileKey, agreeing exactly with WeaponComparer), and
    // #209's deterministic ordering still holds because the key is orderable and name-primary.
    [TestFixture]
    public class SplitWeaponProfileTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // The key itself — it must agree with the comparer the pool dedupes by,
        // or an option can exist that no pool entry matches (or vice versa).
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void ProfileKey_AgreesWithWeaponComparer()
        {
            var comparer = new WeaponComparer();
            Weapon plain = Rifle();
            Weapon plainTwin = Rifle();
            Weapon precise = PreciseRifle();
            Weapon deadly3 = MeleeDeadly("Sword", 3);
            Weapon deadly6 = MeleeDeadly("Sword", 6);

            Assert.Multiple(() =>
            {
                Assert.That(WeaponProfileKey.For(plain), Is.EqualTo(WeaponProfileKey.For(plainTwin)),
                    "identical profiles share a key (the comparer groups them).");
                Assert.That(comparer.Equals(plain, plainTwin), Is.True);

                Assert.That(WeaponProfileKey.For(precise), Is.Not.EqualTo(WeaponProfileKey.For(plain)),
                    "same name, different rules - the comparer splits them, so the key must too.");
                Assert.That(comparer.Equals(precise, plain), Is.False);

                Assert.That(WeaponProfileKey.For(deadly3), Is.Not.EqualTo(WeaponProfileKey.For(deadly6)),
                    "the rule's ARGUMENT is part of the profile - Deadly(3) is not Deadly(6).");
                Assert.That(comparer.Equals(deadly3, deadly6), Is.False);
            });
        }

        // The key is name-primary, so re-keying the options did not reshuffle the #209 ordering that
        // same-seed replay depends on: for the unique-name case the sort is byte-identical to the old
        // OrderBy(Weapon.Name), including the prefix case ("Rifle" before "Rifle Grenade").
        [Test]
        public void ProfileKey_SortsNamePrimary()
        {
            List<string> keys = new List<Weapon>
                {
                    new Weapon("Rifle Grenade", 18f, 1, 0), Rifle(), new Weapon("Cannon", 36f, 1, 0),
                }
                .Select(WeaponProfileKey.For)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            Assert.That(keys.Select(NameOf), Is.EqualTo(new[] { "Cannon", "Rifle", "Rifle Grenade" }));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Ranged chooser
        // ──────────────────────────────────────────────────────────────────────

        // The headline: before the fix this threw out of BuildWeaponOptions and took the state machine
        // with it. Three rifles, one of them upgraded — two options, correct carrier counts on each.
        [Test]
        public async Task RangedChooser_SplitSameNameWeapon_OffersBothProfiles()
        {
            var requester = new ChooseRangedAttackStageTests.CapturingRangedRequester
            {
                Reply = _ => new Cancelled<RangedAttackChoice>(),
            };
            var (ctx, attacker) = BuildSplitRifleWorld(requester);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRangedEvents(stage);
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null, "the split must reach the resolver, not fault.");
            List<WeaponOption> options = requester.Captured!.WeaponOptions;
            Assert.That(options, Has.Count.EqualTo(2), "the two profiles are two options.");
            Assert.That(options.Select(o => o.Weapon.Name), Is.All.EqualTo("Rifle"),
                "both options really do share the name - that is the whole point.");

            WeaponOption preciseOption = options.Single(o => HasPrecise(o.Weapon));
            WeaponOption plainOption = options.Single(o => !HasPrecise(o.Weapon));
            Assert.That(preciseOption.WeaponTargetStats.Single().modelsThatCanShoot, Has.Count.EqualTo(1),
                "one model carries the upgraded rifle.");
            Assert.That(plainOption.WeaponTargetStats.Single().modelsThatCanShoot, Has.Count.EqualTo(2),
                "the other two carry the plain one - a model must not be counted under both profiles.");
        }

        // Picking a profile must fire THAT profile: the chosen weapon carries the rule (or doesn't) and
        // the volley is sized from that profile's carriers, not from every same-named copy in the unit.
        [TestCase(true, 1)]
        [TestCase(false, 2)]
        public async Task RangedChooser_ChoosingAProfile_FiresThatProfile(bool pickPrecise, int expectedShots)
        {
            var requester = new ChooseRangedAttackStageTests.CapturingRangedRequester
            {
                Reply = req =>
                {
                    WeaponOption opt = req.WeaponOptions.Single(o => HasPrecise(o.Weapon) == pickPrecise);
                    return new Selected<RangedAttackChoice>(
                        new RangedAttackChoice(opt.Weapon, opt.WeaponTargetStats.Single().TargetUnit));
                },
            };
            var (ctx, attacker) = BuildSplitRifleWorld(requester);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRangedEvents(stage);
            await stage.Enter(combatCtx);

            ICombatMetadata metadata = combatCtx.ConsumeAttackIntoContext(ctx);
            Assert.That(HasPrecise(metadata.WeaponType), Is.EqualTo(pickPrecise),
                "the profile that was picked is the profile that fires.");
            Assert.That(metadata.WeaponCount, Is.EqualTo(expectedShots),
                "only the carriers of the chosen profile shoot.");
        }

        // A remote player's reply is a DESERIALIZED weapon: RuleDefinitions is [JsonIgnore] and travels as
        // the persisted blob. #323 moved rehydration to the deserialization boundary itself (Weapon's
        // [OnDeserialized]), so the reply now arrives with its rules already live - and the stage's own
        // RehydrateRules call is a harmless no-op. The profile match this test pins is unchanged.
        [Test]
        public async Task RangedChooser_ChoiceThatCameOverTheWire_StillBindsItsProfile()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new ChooseRangedAttackStageTests.CapturingRangedRequester
            {
                Reply = req =>
                {
                    WeaponOption opt = req.WeaponOptions.Single(o => HasPrecise(o.Weapon));
                    // Round-trip exactly the way a networked reply body does.
                    string json = JsonConvert.SerializeObject(opt.Weapon, store.GetJsonSettings());
                    Weapon overTheWire = JsonConvert.DeserializeObject<Weapon>(json, store.GetJsonSettings())!;
                    Assert.That(HasPrecise(overTheWire), Is.True,
                        "#323: a deserialized weapon rehydrates its rules at the boundary.");
                    return new Selected<RangedAttackChoice>(
                        new RangedAttackChoice(overTheWire, opt.WeaponTargetStats.Single().TargetUnit));
                },
            };
            var (ctx, attacker) = BuildSplitRifleWorld(requester, store);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRangedEvents(stage);
            await stage.Enter(combatCtx);

            ICombatMetadata metadata = combatCtx.ConsumeAttackIntoContext(ctx);
            Assert.That(HasPrecise(metadata.WeaponType), Is.True,
                "the upgraded profile survives the wire round-trip.");
            Assert.That(metadata.WeaponCount, Is.EqualTo(1));
        }

        // ChooseActionStage's shoot gate runs the same builder to grey out Shoot (#200: gate and stage
        // must agree). It deduped its pool by NAME, which both collapsed the split and fed the builder a
        // pool the stage would never see.
        [Test]
        public void HasAnyFireableTarget_SplitSameNameWeapon_DoesNotFault()
        {
            var (ctx, attacker) = BuildSplitRifleWorld(new ChooseRangedAttackStageTests.CapturingRangedRequester());

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, ctx), Is.True);
        }

        // #209 still holds with two same-named options in play: the order cannot depend on how the pool
        // happened to hash, so the same models built in the opposite order offer the same sequence.
        [Test]
        public async Task RangedChooser_SplitWeaponOptionOrder_IsDeterministic()
        {
            IReadOnlyList<string> forward = await SplitOptionKeys(preciseModelFirst: false);
            IReadOnlyList<string> reversed = await SplitOptionKeys(preciseModelFirst: true);

            Assert.That(forward, Is.EqualTo(reversed),
                "option order must not depend on pool insertion/hash order.");
            Assert.That(forward, Is.EqualTo(forward.OrderBy(k => k, StringComparer.Ordinal).ToList()),
                "options come out in a deterministic (ordinal) order.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Melee chooser
        // ──────────────────────────────────────────────────────────────────────

        // The ordinary case: an army-loaded rule's requested name IS its entry's PrintableName, which
        // carries the argument, so the two swords already read differently. The pool must still offer
        // them as two options rather than folding them together.
        [Test]
        public async Task MeleeChooser_SplitSameNameWeapon_OffersBothProfiles()
        {
            var requester = new RecordingStringRequester();
            var (ctx, attacker) = BuildSplitSwordWorld(requester,
                MeleeDeadly("Sword", 3), MeleeDeadly("Sword", 6));

            await EnterMelee(ctx, attacker);

            Assert.That(requester.Captured, Is.Not.Null);
            // #320: filtered to the rows that ATTACK - each weapon also has a "Hold back" row now.
            List<string> labels = requester.Captured!.ValidOptions
                .Where(option => !ChooseMeleeWeaponStage.IsHoldBackChoice(option)).ToList();
            Assert.That(labels, Has.Count.EqualTo(2), "two profiles are two options.");
            Assert.That(labels.Any(l => l.Contains("Deadly(3)")), Is.True);
            Assert.That(labels.Any(l => l.Contains("Deadly(6)")), Is.True);
        }

        // The case nothing guaranteed before: two profiles whose labels render IDENTICALLY, because their
        // rules were resolved under a bare requested name that drops the argument. The label is the
        // option's identity on this wire, so if they collapse to one string the second weapon becomes
        // unpickable — one of the two swords could never be swung with.
        [Test]
        public async Task MeleeChooser_ProfilesThatRenderIdentically_StayDistinctOptions()
        {
            var requester = new RecordingStringRequester();
            var (ctx, attacker) = BuildSplitSwordWorld(requester,
                MeleeDeadlyBareName("Sword", 3), MeleeDeadlyBareName("Sword", 6));

            await EnterMelee(ctx, attacker);

            List<string> labels = requester.Captured!.ValidOptions
                .Where(option => !ChooseMeleeWeaponStage.IsHoldBackChoice(option)).ToList();   // #320
            Assert.That(labels, Has.Count.EqualTo(2));
            Assert.That(labels.Distinct().Count(), Is.EqualTo(2),
                "two profiles must never present as one string - the label IS the option key here.");
        }

        // Choosing the second of two same-named swords must bind the second, not "whichever matched first".
        [Test]
        public async Task MeleeChooser_ChoosingTheUpgradedProfile_BindsThatProfile()
        {
            Weapon plain = new Weapon("Sword", 0f, 2, 1);
            Weapon rending = new Weapon("Sword", 0f, 2, 1);
            rending.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));

            var requester = new RecordingStringRequester
            {
                // #320: the hold-back row for the same weapon also contains "Rending" - pick the one
                // that actually swings it.
                Pick = options => options.Single(o =>
                    o.Contains("Rending") && !ChooseMeleeWeaponStage.IsHoldBackChoice(o)),
            };
            var (ctx, attacker) = BuildSplitSwordWorld(requester, plain, rending);

            ICombatActionContext combatCtx = await EnterMelee(ctx, attacker);

            ICombatMetadata metadata = combatCtx.ConsumeAttackIntoContext(ctx);
            Assert.That(metadata.WeaponType.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Rending),
                Is.True, "the label that was chosen must map back to its own weapon.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);

        private static Weapon PreciseRifle()
        {
            Weapon weapon = new Weapon("Rifle", 24f, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Precise", CoreRuleCatalog.Precise));
            return weapon;
        }

        // Requested name as an army load produces it: SpecialRuleEntry_CoreNumeric's PrintableName is
        // "Deadly(3)", so the argument is already part of what the label renders.
        private static Weapon MeleeDeadly(string name, int x)
        {
            Weapon weapon = new Weapon(name, 0f, 2, 1);
            weapon.AttachRuleDefinition(new ResolvedRule($"Deadly({x})", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(x) }));
            return weapon;
        }

        // The same weapon resolved under a BARE requested name, which drops the argument from every
        // display string - two of these differ in profile but render as one identical label.
        private static Weapon MeleeDeadlyBareName(string name, int x)
        {
            Weapon weapon = new Weapon(name, 0f, 2, 1);
            weapon.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(x) }));
            return weapon;
        }

        private static bool HasPrecise(IWeapon weapon) =>
            weapon.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Precise);

        private static string NameOf(string profileKey) =>
            profileKey.Split(WeaponProfileKey.FieldSeparator)[0];

        private static void BindRangedEvents(ChooseRangedAttackStage stage)
        {
            stage.OnChoseWeapon.Bind("test-on-chose-weapon");
            stage.BackToChooseAction.Bind("test-back-to-choose-action");
            stage.OnNoValidShots.Bind("test-on-no-valid-shots");
        }

        private static async Task<IReadOnlyList<string>> SplitOptionKeys(bool preciseModelFirst)
        {
            var requester = new ChooseRangedAttackStageTests.CapturingRangedRequester
            {
                Reply = _ => new Cancelled<RangedAttackChoice>(),
            };
            var (ctx, attacker) = BuildSplitRifleWorld(requester, preciseModelFirst: preciseModelFirst);

            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindRangedEvents(stage);
            await stage.Enter(combatCtx);

            return requester.Captured!.WeaponOptions.Select(o => WeaponProfileKey.For(o.Weapon)).ToList();
        }

        // Three models 10" from a lone enemy: two plain rifles and one Precise rifle, all named "Rifle".
        private static (ChooseRangedAttackStageTests.TestGameContextWithRequester ctx,
            DataBinding<UnitData> attacker) BuildSplitRifleWorld(IPlayerRequestByID requester,
                GameDataStore? existingStore = null, bool preciseModelFirst = false)
        {
            GameDataStore store = existingStore ?? GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            List<Weapon> carried = preciseModelFirst
                ? new List<Weapon> { PreciseRifle(), Rifle(), Rifle() }
                : new List<Weapon> { Rifle(), Rifle(), PreciseRifle() };

            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < carried.Count; i++)
            {
                models.Add(MakeModel(store, new Position(i, 0, 0), carried[i]));
            }
            DataBinding<UnitData> attacker = MakeUnit(store, attackerPlayer, "Attacker", models);
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attacker }));

            DataBinding<UnitData> enemy = MakeUnit(store, enemyPlayer, "Enemy",
                new[] { MakeModel(store, new Position(10, 0, 0)) });
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemy }));

            return (ctx, attacker);
        }

        private static (ChooseRangedAttackStageTests.TestGameContextWithRequester ctx,
            DataBinding<UnitData> attacker) BuildSplitSwordWorld(IPlayerRequestByID requester,
                params Weapon[] swords)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);

            var models = swords
                .Select((sword, i) => MakeModel(store, new Position(i, 0, 0), sword))
                .ToList();
            DataBinding<UnitData> attacker = MakeUnit(store, new PlayerID(Guid.NewGuid()), "Attacker", models);

            return (ctx, attacker);
        }

        private static async Task<ICombatActionContext> EnterMelee(
            ChooseRangedAttackStageTests.TestGameContextWithRequester ctx, DataBinding<UnitData> attacker)
        {
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: true);
            // ConsumeAttackIntoContext needs a defender; the chooser itself never reads one.
            combatCtx.SetDefender(attacker);
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChosen.Bind("test-on-chosen");
            await stage.Enter(combatCtx);
            return combatCtx;
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position,
            params Weapon[] weapons)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons.ToList(),
                initialPosition: position, gameDataStore: store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID, string name,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, name, quality: 4, defense: 4, modelBindings: models.ToList());
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        // Like DeadlyWeaponPriorityTests' capturing requester, but the choice is steerable so a test can
        // pick the SECOND of two same-named options rather than always the first.
        private class RecordingStringRequester : IPlayerRequestByID
        {
            public StringSelectionRequest? Captured { get; private set; }

            public Func<IReadOnlyList<string>, string> Pick { get; set; } = options => options.First();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest ssr)
                {
                    Captured = ssr;
                    object reply = Pick(ssr.ValidOptions);
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
