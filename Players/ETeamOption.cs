using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    public enum ETeamOption
    {
        None = -1,
        Team1 = 1,
        Team2 = 2,
        Team3 = 3,
        Team4 = 4,
        Team5 = 5,
        Team6 = 6,
        Team7 = 7,
        Team8 = 8
    }

    /// <summary>
    /// Helpers over <see cref="ETeamOption"/>. The lobby's default-team assignment and its range checks
    /// derive their upper bound from <see cref="MaxTeamNumber"/> rather than hard-coding one, so widening
    /// the enum above is the only edit needed to support more teams (#188 - the old code assumed the
    /// roster could never outgrow the enum and handed a fifth player an undefined value).
    /// </summary>
    public static class TeamOptions
    {
        /// <summary>Highest defined team number. Read off the enum so it cannot drift out of sync with it.</summary>
        public static readonly int MaxTeamNumber =
            Enum.GetValues(typeof(ETeamOption)).Cast<ETeamOption>().Max(team => (int)team);

        /// <summary>True for a real team (Team1..TeamN), false for <see cref="ETeamOption.None"/> and for
        /// any out-of-range value cast in from an int.</summary>
        public static bool IsRealTeam(ETeamOption team) =>
            (int)team >= 1 && (int)team <= MaxTeamNumber;
    }
}
