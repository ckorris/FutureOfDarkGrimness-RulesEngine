using FDG.Data;

namespace FDG.Network.Messages.DataMessages
{
    public class RemoveSingleDataMessage
    {
        public DataReference DataReference;

        public RemoveSingleDataMessage(DataReference dataReference)
        {
            DataReference = dataReference;
        }
    }
}
