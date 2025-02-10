using FDG.Data;

namespace FDG.Network.Messages.DataMessages
{
    public class UpdateSingleDataMessage
    {
        public DataReference DataReference;

        public string ValueAsJson;

        public UpdateSingleDataMessage(DataReference dataReference, string valueAsJson)
        {
            DataReference = dataReference;
            ValueAsJson = valueAsJson;
        }
    }
}
