
namespace FDG.Network.Connection
{
    public readonly record struct ConnectionID
    {
        public readonly Guid ID;

        public static readonly ConnectionID Host = new ConnectionID(Guid.Empty);

        public ConnectionID(Guid id)
        {
            ID = id;
        }
    }
}
