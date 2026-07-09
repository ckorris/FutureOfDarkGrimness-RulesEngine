using FDG.Players;
using FDG.StageResolution.Requests;

namespace FDG.StageResolution
{
    /// <summary>
    /// Helpers for issuing a <see cref="PlaceObjectsRequest{T}"/>.
    ///
    /// The reply is a <see cref="CancellableResult{T}"/> because Disembark lets the player back out before
    /// any model is repositioned. Every other placement - deployment, Scout, Ambush arrival, transport
    /// spillout - is mandatory: the request is built with <c>allowCancel: false</c>, no resolver offers a
    /// Back button for it, and a Cancelled reply would be a protocol violation rather than a player choice.
    /// <see cref="RequestMandatoryPlacement{T}"/> is how those callers say so once, instead of each of them
    /// hand-rolling the same cast.
    /// </summary>
    public static class PlacementRequesting
    {
        public static async Task<List<PlacedObjectEntry<T>>> RequestMandatoryPlacement<T>(
            IPlayerRequestByID requester, PlaceObjectsRequest<T> request)
        {
            CancellableResult<List<PlacedObjectEntry<T>>> result = await requester
                .RequestDecision<PlaceObjectsRequest<T>, CancellableResult<List<PlacedObjectEntry<T>>>>(request);

            if (result is not Selected<List<PlacedObjectEntry<T>>> selected)
            {
                throw new RequestResponseInvalidException(
                    $"'{request.TaskName}' is a mandatory placement and cannot be cancelled.");
            }
            return selected.Value;
        }
    }
}
