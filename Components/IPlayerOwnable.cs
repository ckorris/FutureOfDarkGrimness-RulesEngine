using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
