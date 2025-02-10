using FDG.Data;

namespace FDG.Network.Messages.DataMessages
{
    public class AddSingleDataMessage
    {
        public DataReference DataReference;

        public string InitialValueAsJson;

        public AddSingleDataMessage(DataReference dataReference, string initialValueAsJson)
        {
            DataReference = dataReference;
            InitialValueAsJson = initialValueAsJson;
        }
    }
}
