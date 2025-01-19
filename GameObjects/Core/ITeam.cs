
namespace FDG
{
    public interface ITeam
    {
        int TeamNumber { get; }

        IReadOnlyList<IPlayer> Players { get; }
    }
}
