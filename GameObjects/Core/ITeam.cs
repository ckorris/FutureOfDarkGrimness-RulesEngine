
namespace FDG
{
    public interface ITeam
    {
        int TeamNumber { get; }

        IReadOnlyList<IPlayerInfo> Players { get; }
    }
}
