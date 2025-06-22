
namespace FDG.Players
{
    public enum EChatMessageType
    {
        /// <summary>
        /// Sent from one person to everyone.
        /// </summary>
        Global,
        /// <summary>
        /// Sent from one person to members of their team.
        /// </summary>
        Team,
        /// <summary>
        /// Sent from one person to just one other person.
        /// </summary>
        Direct
    }
}
