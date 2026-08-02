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
    // #316: Limited was enforced in shooting only (#032) - ChooseMeleeWeaponStage never asked IsSpent and
    // never called MarkFired, so a once-per-game melee weapon was usable every combat, forever, and could
    // not be declined. These cover the melee half: the spent gate, the per-model spend (only the models
    // within melee range swing), and the hold-back that #315 gave the shooting flow.
    [TestFixture]
    public class MeleeLimitedTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // Enforcement — the half that was missing entirely.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task MeleeChoose_ChoosingLimitedWeapon_MarksItSpentForTheGame()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            var requester = new MeleeRequester { Pick = options => Swing(options, "Demo Charge") };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, bomb, Blade());

            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), bomb), Is.False, "available beforehand.");

            await EnterStage(ctx, combatCtx);

            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), bomb), Is.True,
                "swinging the Limited weapon spends it for the rest of the game.");
        }

        [Test]
        public async Task MeleeChoose_SpentLimitedWeapon_IsOfferedAsUnavailable_OthersStayValid()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            var requester = new MeleeRequester { Pick = options => Swing(options, "Blade") };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, bomb, Blade());

            LimitedRules.MarkFired(attacker.GetValue(), bomb); // used in an earlier melee this game

            await EnterStage(ctx, combatCtx);

            Assert.That(requester.Captured!.ValidOptions.Any(o => o.Contains("Demo Charge")), Is.False,
                "a spent Limited weapon may not be swung again this game.");
            Assert.That(requester.Captured!.InvalidOptions
                    .Any(o => o.Option.Contains("Demo Charge") && o.Reason.Contains("Limited")), Is.True,
                "and the menu says why, rather than hiding the weapon.");
            Assert.That(requester.Captured!.ValidOptions.Any(o => o.Contains("Blade")), Is.True,
                "the unit's ordinary weapon is unaffected.");
        }

        // Melee only lets the models within melee range swing, so only they spend their charge. Marking a
        // carrier that stood three inches back would burn a use it still has - and would wrongly retire the
        // weapon for the whole unit, since IsSpent asks whether EVERY living carrier has used it.
        [Test]
        public async Task MeleeChoose_OnlyTheModelsInMeleeRange_SpendTheirLimitedUse()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            var inRange = MakeModel(store, bomb);
            var standingBack = MakeModel(store, LimitedWeapon("Demo Charge"));
            var attacker = MakeUnit(store, inRange, standingBack);

            var requester = new MeleeRequester { Pick = options => Swing(options, "Demo Charge") };
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: true);
            combatCtx.SetInRangeAttackers(new List<DataBinding<ModelData>> { inRange });

            await EnterStage(ctx, combatCtx);

            Assert.That(HasSpentToken(inRange), Is.True, "the model that swung spent its charge.");
            Assert.That(HasSpentToken(standingBack), Is.False,
                "the model out of melee range never attacked, so it keeps its charge.");
            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), bomb), Is.False,
                "and the unit's weapon is therefore not retired for the game.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Hold back — the melee counterpart of #315's Hold fire.
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public async Task MeleeChoose_HoldBackLimitedWeapon_LeavesItUnspent_AndOffersTheRest()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            var requests = new List<StringSelectionRequest>();
            var requester = new MeleeRequester
            {
                Pick = options =>
                {
                    string? hold = options.FirstOrDefault(o =>
                        ChooseMeleeWeaponStage.IsHoldBackChoice(o) && o.Contains("Demo Charge"));
                    return hold ?? Swing(options, "Blade");
                },
                OnRequest = requests.Add,
            };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, bomb, Blade());

            await EnterStage(ctx, combatCtx);

            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), bomb), Is.False,
                "a weapon that never swung keeps its once-per-game use.");
            Assert.That(combatCtx.DeclinedWeapons.Keys.Any(w => w.Name == "Demo Charge"), Is.True);
            Assert.That(combatCtx.AlreadyUsedWeapons.Keys.Any(w => w.Name == "Demo Charge"), Is.False,
                "declining is not attacking.");
            Assert.That(combatCtx.AlreadyUsedWeapons.Keys.Any(w => w.Name == "Blade"), Is.True,
                "the unit still attacks with its other weapon.");
            Assert.That(requests, Has.Count.EqualTo(2), "holding back re-offers the remaining weapons.");
            Assert.That(requests[1].ValidOptions.Any(o => o.Contains("Demo Charge")), Is.False,
                "the held-back weapon is not offered again this melee.");
        }

        // The melee twin of #315's headline case: a Deadly+Limited weapon gates the unit's ordinary ones,
        // so declining it has to release them or declining would cost the unit its whole attack.
        [Test]
        public async Task MeleeChoose_HoldBackDeadlyLimitedWeapon_UnlocksTheOrdinaryWeapons()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var spike = LimitedWeapon("Metal Spike");
            spike.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(3) }));

            var requests = new List<StringSelectionRequest>();
            var requester = new MeleeRequester
            {
                Pick = options =>
                {
                    string? hold = options.FirstOrDefault(o =>
                        ChooseMeleeWeaponStage.IsHoldBackChoice(o) && o.Contains("Metal Spike"));
                    return hold ?? Swing(options, "Blade");
                },
                OnRequest = requests.Add,
            };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, spike, Blade());

            await EnterStage(ctx, combatCtx);

            Assert.That(requests[0].InvalidOptions.Any(o => o.Option.Contains("Blade")), Is.True,
                "test setup: the Deadly spike gates the blade while it is on offer.");
            Assert.That(requests[1].ValidOptions.Any(o =>
                    o.Contains("Blade") && !ChooseMeleeWeaponStage.IsHoldBackChoice(o)), Is.True,
                "a declined resolve-first weapon must stop demanding to be resolved first.");
            Assert.That(combatCtx.AlreadyUsedWeapons.Keys.Any(w => w.Name == "Blade"), Is.True);
            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), spike), Is.False);
        }

        // User sign-off: the charge already happened and cannot be rewound, so a unit may not decline its
        // way out of attacking altogether. The last un-declined weapon is offered as unavailable.
        [Test]
        public async Task MeleeChoose_LastWeaponWithNothingSwungYet_CannotBeHeldBack()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new MeleeRequester { Pick = options => Swing(options, "Demo Charge") };
            var (ctx, _, combatCtx) = BuildMelee(store, requester, LimitedWeapon("Demo Charge"));

            await EnterStage(ctx, combatCtx);

            Assert.That(requester.Captured!.ValidOptions.Any(ChooseMeleeWeaponStage.IsHoldBackChoice), Is.False,
                "with one weapon and nothing swung, holding back would mean charging in and doing nothing.");
            Assert.That(requester.Captured!.InvalidOptions.Any(o =>
                    ChooseMeleeWeaponStage.IsHoldBackChoice(o.Option)
                    && o.Reason.Contains("At least one weapon must attack")), Is.True,
                "and the menu says why it is refused.");
        }

        // Holding back the LAST weapon after something has already swung ends the attack, so it confirms
        // first (user sign-off) - the melee analogue of the Done shooting confirmation.
        [Test]
        public async Task MeleeChoose_HoldBackLastWeaponAfterSwinging_ConfirmsThenEndsTheAttack()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            // Swing the blade while it is on offer; once only the bomb is left, hold it back - which is the
            // decline that ends the attack.
            var requester = new MeleeRequester
            {
                Pick = options => options.FirstOrDefault(o =>
                        o.Contains("Blade") && !ChooseMeleeWeaponStage.IsHoldBackChoice(o))
                    ?? options.First(o => ChooseMeleeWeaponStage.IsHoldBackChoice(o)),
                YesNoAnswer = true,
            };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, bomb, Blade());
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            var transitions = new List<string>();
            BindAll(stage);
            stage.OnChosen.OnWillActivate += _ => transitions.Add("chosen");
            stage.OnNoWeaponsLeftToSwing.OnWillActivate += _ => transitions.Add("no-weapons-left");

            await stage.Enter(combatCtx);   // swings the blade
            await stage.Enter(combatCtx);   // holds back the bomb - confirms, then ends the attack

            Assert.That(requester.YesNoAsked, Is.Not.Null,
                "ending the attack with a weapon unswung asks first.");
            Assert.That(requester.YesNoAsked!.QuestionText, Does.Contain("Demo Charge")
                .And.Contain("once-per-game"),
                "the question names the weapon and what holding it back keeps.");
            Assert.That(transitions, Is.EqualTo(new[] { "chosen", "no-weapons-left" }));
            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), bomb), Is.False);
        }

        [Test]
        public async Task MeleeChoose_DecliningTheConfirmation_OffersTheWeaponsAgain()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            bool firstTry = true;
            var requester = new MeleeRequester
            {
                Pick = options =>
                {
                    string? blade = options.FirstOrDefault(o =>
                        o.Contains("Blade") && !ChooseMeleeWeaponStage.IsHoldBackChoice(o));
                    if (blade != null) return blade;
                    if (firstTry)
                    {
                        firstTry = false;
                        return options.First(o => ChooseMeleeWeaponStage.IsHoldBackChoice(o)
                            && o.Contains("Demo Charge"));
                    }
                    return Swing(options, "Demo Charge");
                },
                YesNoAnswer = false, // "no, I did not mean to end the attack"
            };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, bomb, Blade());
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAll(stage);

            await stage.Enter(combatCtx);   // swings the blade (sorted first)
            await stage.Enter(combatCtx);   // tries to hold back the bomb, says no, then swings it

            Assert.That(requester.YesNoAsked, Is.Not.Null);
            Assert.That(combatCtx.DeclinedWeapons, Is.Empty, "the hold-back was called off.");
            Assert.That(LimitedRules.IsSpent(attacker.GetValue(), bomb), Is.True,
                "the weapon was swung instead.");
        }

        // Every melee weapon spent means the unit simply has no attack - route on rather than send a menu
        // with nothing selectable (the AI's catch-all would throw on an empty ValidOptions).
        [Test]
        public async Task MeleeChoose_EveryWeaponSpent_RoutesOnWithoutAskingThePlayer()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bomb = LimitedWeapon("Demo Charge");
            var requester = new MeleeRequester { Pick = options => options.First() };
            var (ctx, attacker, combatCtx) = BuildMelee(store, requester, bomb);

            LimitedRules.MarkFired(attacker.GetValue(), bomb);

            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            var transitions = new List<string>();
            BindAll(stage);
            stage.OnChosen.OnWillActivate += _ => transitions.Add("chosen");
            stage.OnNoWeaponsLeftToSwing.OnWillActivate += _ => transitions.Add("no-weapons-left");

            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Null, "there was nothing to ask.");
            Assert.That(transitions, Is.EqualTo(new[] { "no-weapons-left" }));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static async Task EnterStage(IGameContext ctx, ICombatActionContext combatCtx)
        {
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            BindAll(stage);
            await stage.Enter(combatCtx);
        }

        private static void BindAll(ChooseMeleeWeaponStage stage)
        {
            stage.OnChosen.Bind("test-on-chosen");
            stage.OnNoWeaponsLeftToSwing.Bind("test-no-weapons-left");
        }

        private static string Swing(IReadOnlyList<string> options, string weaponName) =>
            options.First(o => o.Contains(weaponName) && !ChooseMeleeWeaponStage.IsHoldBackChoice(o));

        private static Weapon Blade() => new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0);

        private static Weapon LimitedWeapon(string name)
        {
            var weapon = new Weapon(name, rangeInches: 0f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
            return weapon;
        }

        private static bool HasSpentToken(DataBinding<ModelData> model) =>
            model.GetValue().Tokens.GetAllTokens(Rules.Foundation.TokenType.LimitedSpent).Any();

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

        private static (ChooseRangedAttackStageTests.TestGameContextWithRequester ctx,
            DataBinding<UnitData> attacker, CombatActionContext combatCtx) BuildMelee(
                GameDataStore store, MeleeRequester requester, params Weapon[] weapons)
        {
            var model = MakeModel(store, weapons);
            var attacker = MakeUnit(store, model);
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);
            var combatCtx = new CombatActionContext(ctx, attacker, isMelee: true);
            combatCtx.SetInRangeAttackers(new List<DataBinding<ModelData>> { model });
            return (ctx, attacker, combatCtx);
        }

        // Answers both request types the melee weapon choice can raise: the weapon menu and (#316) the
        // confirmation that ending the attack early puts up.
        private class MeleeRequester : IPlayerRequestByID
        {
            public StringSelectionRequest? Captured { get; private set; }
            public YesNoRequest? YesNoAsked { get; private set; }
            public Func<IReadOnlyList<string>, string> Pick { get; set; } = options => options.First();
            public Action<StringSelectionRequest>? OnRequest { get; set; }
            public bool YesNoAnswer { get; set; } = true;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest ssr)
                {
                    Captured = ssr;
                    OnRequest?.Invoke(ssr);
                    object reply = Pick(ssr.ValidOptions);
                    return Task.FromResult((TReply)reply);
                }
                if (request is YesNoRequest yn)
                {
                    YesNoAsked = yn;
                    object reply = YesNoAnswer;
                    return Task.FromResult((TReply)reply);
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
