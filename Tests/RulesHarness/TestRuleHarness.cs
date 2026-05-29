using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Tests.RulesHarness
{
    /// <summary>
    /// One-stop scaffold for #042 special-rule tests. Owns the data store, a
    /// <see cref="TestGameContext"/> (with deterministic dice), the rule
    /// <see cref="Resolver"/>, and the <see cref="Bus"/>, and collapses unit
    /// construction + rule attachment + hook firing into a few calls so a test
    /// reads like the rule under test rather than the wiring around it.
    ///
    /// The bus is still the Phase 4 stub (returns no operations); Phase 6 tests
    /// that attach rules and expect operations stay RED until Phase 7a implements
    /// real dispatch.
    /// </summary>
    internal sealed class TestRuleHarness
    {
        private const float DefaultBaseRadiusInches = 0.75f;

        public RuleResolver Resolver { get; } = new();
        public RuleHookBus Bus { get; } = new();
        public TestGameContext GameContext { get; }

        private readonly GameDataStore _store;
        private readonly Dictionary<string, PlayerID> _playersByName = new();

        public TestRuleHarness(IDiceRoller? diceRoller = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameContext = new TestGameContext(_store, diceRoller ?? new FixedDiceRoller(4));
        }

        /// <summary> Registers a rule definition under its canonical name. </summary>
        public void Register(SpecialRuleDefinition definition) => Resolver.Register(definition);

        /// <summary> Registers an alias for an already-registered rule. </summary>
        public void RegisterAlias(string alias, string existingRuleName)
            => Resolver.RegisterAlias(alias, existingRuleName);

        /// <summary>
        /// Builds a unit owned by <paramref name="playerName"/> with
        /// <paramref name="modelCount"/> models, attaching each rule in
        /// <paramref name="ruleNames"/> resolved through <see cref="Resolver"/>.
        /// Rule names must be registered first (via <see cref="Register"/> /
        /// <see cref="RegisterAlias"/>) — this is the realistic path that exercises
        /// alias resolution. The same player name always maps to the same PlayerID.
        /// </summary>
        public IUnit BuildUnit(string playerName, int modelCount, params string[] ruleNames)
        {
            PlayerID playerID = GetOrCreatePlayer(playerName);

            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: DefaultBaseRadiusInches,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: new Position(),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(playerID, $"{playerName}-unit", quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(), modelBindings: modelBindings);

            foreach (string ruleName in ruleNames)
            {
                unit.AttachRuleDefinition(Resolver.Resolve(ruleName));
            }

            _store.Create(unit);
            return unit;
        }

        /// <summary>
        /// Attaches <paramref name="definition"/> to an already-built unit directly,
        /// without going through the resolver — for tests that hold a definition
        /// object rather than a registered name. The requested name is the
        /// definition's canonical name. <paramref name="arguments"/> supplies the
        /// per-instance parameter values for parameterized rules (Deadly(3) →
        /// <c>new RuleArgument.Int(3)</c>); omit for rules that take none.
        /// </summary>
        public void AttachRule(IUnit unit, SpecialRuleDefinition definition, params RuleArgument[] arguments)
        {
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule(definition.Name, definition, arguments));
        }

        /// <summary> Fires a hook through the bus and returns the resulting operations. </summary>
        public IReadOnlyList<RuleOperation> Fire(IHookContext context) => Bus.Dispatch(context);

        /// <summary>
        /// Returns the player-triggered abilities the bus offers at this hook (affordable,
        /// available, trigger matches). The observable for "an ability was offered."
        /// </summary>
        public IReadOnlyList<AbilityOffer> OfferAbilities(IHookContext context) => Bus.GatherOffers(context);

        /// <summary>
        /// Accepts an offered ability against the chosen <paramref name="targets"/>,
        /// returning the resolved queue (cost-consumption operations then effect
        /// operations).
        /// </summary>
        public IReadOnlyList<RuleOperation> Accept(AbilityOffer offer, params IUnit[] targets)
            => Bus.ResolveAbility(offer, targets);

        /// <summary>
        /// Seeds <paramref name="count"/> tokens of <paramref name="type"/> onto a unit's
        /// container, for tests that need pre-existing token state (stacking markers,
        /// cross-unit marks). <paramref name="owner"/> sets the placing unit for
        /// cross-unit tokens; <paramref name="clear"/> defaults to ManualOnly.
        /// </summary>
        public void SeedToken(IUnit unit, TokenType type, int count = 1,
            UnitID? owner = null, TokenClearTrigger? clear = null)
        {
            TokenClearTrigger trigger = clear ?? new TokenClearTrigger.ManualOnly();
            for (int i = 0; i < count; i++)
            {
                unit.Tokens.AddToken(new Token(type, 1, trigger, OwnerUnitID: owner));
            }
        }

        private PlayerID GetOrCreatePlayer(string playerName)
        {
            if (_playersByName.TryGetValue(playerName, out PlayerID existing))
            {
                return existing;
            }

            var created = new PlayerID(Guid.NewGuid());
            _playersByName[playerName] = created;
            return created;
        }
    }
}
