using FDG.Players;
using FDG.StageResolution;

namespace FDG.Simulation
{
    /// <summary>
    /// The in-simulation replacement for <see cref="RequestMessageSender"/> (#191 B1 step 5c's bus
    /// bypass). Answers a decision by calling the target player's resolver registry through the
    /// typed <see cref="IStageResolverRegistry.ResolveRequest{TRequest,TReply}"/> path directly,
    /// with no message bus, no Newtonsoft round trip and no awaiting/resolved notifications.
    /// <para>
    /// The 2026-09-03 Release profile put the JSON round trip at about 7% of a Tactician game's CPU,
    /// and every request pays it - including local AI players, whose decisions never actually need
    /// the wire (step 4's exporter had to hook <c>ResolveRequestAsJson</c> precisely because local
    /// play travels it). A simulation is all-local by construction, so it can skip it entirely.
    /// </para>
    /// <para>
    /// <b>Only ever used inside a simulation.</b> Real play - anything with a human, a remote client,
    /// or a front end that needs the awaiting/resolved notifications for its outstanding-task UI -
    /// keeps <see cref="RequestMessageSender"/> and the bus. This class has no disconnect handling
    /// and no notifications because a simulation has no connections and no UI.
    /// </para>
    /// </summary>
    public sealed class DirectPlayerRequester : IPlayerRequestByID
    {
        private readonly IReadOnlyDictionary<PlayerID, IStageResolverRegistry> _registriesByPlayer;

        public DirectPlayerRequester(IReadOnlyDictionary<PlayerID, IStageResolverRegistry> registriesByPlayer)
        {
            _registriesByPlayer = registriesByPlayer;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (_registriesByPlayer.TryGetValue(request.TargetPlayerID,
                    out IStageResolverRegistry? registry) == false)
            {
                throw new InvalidOperationException(
                    $"Simulation has no resolver registry for player {request.TargetPlayerID}. " +
                    "Every slot in a simulated game must be local and AI-driven.");
            }

            return registry.ResolveRequest<TRequest, TReply>(request);
        }
    }
}
