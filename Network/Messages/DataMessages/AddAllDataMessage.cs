using FDG.Data.Containers;

namespace FDG.Network.Messages.DataMessages
{
    public class AddAllDataMessage
    {
        public List<ReferenceJsonValuePair> AllData;

        public AddAllDataMessage(List<ReferenceJsonValuePair> allData)
        {
            AllData = allData;
        }
    }
}
