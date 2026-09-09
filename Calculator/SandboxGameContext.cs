using System;
using System.Collections.Generic;
using FDG.Ai;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Dispatch;
using FDG.StageResolution;

namespace FDG.Calculator
{
    /// <summary>
    /// A throwaway <see cref="IGameContext"/> for #397: everything the combat stages read, with no
    /// server, no network, no players and an empty table. The production twin of the test suite's
    /// <c>WoundTestContext</c>, which has driven these same stages since the first wound tests.
    /// <para>
    /// Two pieces are load-bearing. The roller is <see cref="ProbabilisticDiceRoller"/>, whose
    /// <c>Roll</c> spreads dice evenly over every face - so running the real stages under it IS the
    /// expected-value computation, with no parallel arithmetic to keep honest. And the requester is
    /// the production AI resolver registry rather than a hand-written stub: the chain can ask five
    /// different questions (wound allocation, yes/no offers, marker spends, Takedown's model pick, and
    /// a placement when a dying unit Splits), and the AI resolvers already answer all five the way the
    /// EOF/solo paths do.
    /// </para>
    /// </summary>
    internal sealed class SandboxGameContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();

        public IDiceRoller DiceRoller { get; }

        public Random Rng { get; }

        public RuleEvaluator RuleEvaluator { get; }

        public IPlayerRequestByID PlayerRequester { get; }

        public TableState TableState { get; }

        public IReadWriteableGameDataStore GameDataStore { get; }

        public IPresenter Presenter { get; }

        public GameSettings Settings { get; }

        public List<ITeam>? FirstDeploymentRollOrder => null;

        IGameContext IGameContextAccessor.GameContext => this;

        public SandboxGameContext(GameDataStore store, IDiceRoller diceRoller, RuleResolver ruleResolver,
            int? seed, params PlayerID[] players)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            DiceRoller = diceRoller;
            Rng = GameRandom.Create(seed, GameRandom.SALT_GAME_CONTEXT);
            RuleEvaluator = new RuleEvaluator(diceRoller, ruleResolver: ruleResolver);
            Presenter = new LocalPresenter(null, new InstantPresentationClock());
            // The roller really is probabilistic; say so, or the dice beats would label themselves
            // as realistic rolls (GetDefault ships Realistic, for a live game).
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            Settings = settings;
            PlayerRequester = new SandboxRequester(TableState, seed, players);
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> firstDeploymentRollWinner) { }

        public void NotifyGameCompleted(GameResult result) { }

        /// <summary>
        /// Answers every stage request with the solo-rules AI registry, one per player so a resolver
        /// that reads the asking player's own state (marker spends) sees the right side.
        /// </summary>
        private sealed class SandboxRequester : IPlayerRequestByID
        {
            private readonly Dictionary<PlayerID, IStageResolverRegistry> _byPlayer = new();
            private readonly IStageResolverRegistry _fallback;

            public SandboxRequester(ITableState tableState, int? seed, PlayerID[] players)
            {
                for (int i = 0; i < players.Length; i++)
                {
                    _byPlayer[players[i]] = AiResolverRegistryFactory.BuildSoloRules(tableState, players[i],
                        seed, slotID: i);
                }

                _fallback = players.Length > 0
                    ? _byPlayer[players[0]]
                    : AiResolverRegistryFactory.BuildSoloRules(tableState, default, seed);
            }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                IStageResolverRegistry registry =
                    _byPlayer.TryGetValue(request.TargetPlayerID, out IStageResolverRegistry? found)
                        ? found
                        : _fallback;

                return registry.ResolveRequest<TRequest, TReply>(request);
            }
        }
    }
}
