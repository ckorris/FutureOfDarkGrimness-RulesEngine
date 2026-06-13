
namespace FDG
{
    public interface IPlayerOwnable
    {
        PlayerID PlayerID { get; }
    }

    public static class IPlayerOwnableExtensions
    {
        public static bool IsOwnedBy(this IPlayerOwnable playerOwnable, PlayerID playerID)
        {
            return playerID == playerOwnable.PlayerID;
        }

        public static bool IsNotOwnedBy(this IPlayerOwnable playerOwnable, PlayerID playerID)
        {
            return playerID != playerOwnable.PlayerID;
        }
    }
}
