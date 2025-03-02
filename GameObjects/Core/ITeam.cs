
namespace FDG
{
    public interface ITeam
    {
        int TeamNumber { get; }

        IReadOnlyList<PlayerID> Players { get; }
    }
}
