
namespace FDG
{
    public interface ITeam
    {
        int TeamNumber { get; }

        IReadOnlyList<IPlayerIdentifyable> Players { get; }
    }
}
