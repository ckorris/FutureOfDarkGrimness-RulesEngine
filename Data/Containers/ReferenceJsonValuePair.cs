
namespace FDG.Data.Containers
{
    public struct ReferenceJsonValuePair
    {
        public DataReference DataReference;
        public string JsonValue;

        public ReferenceJsonValuePair(DataReference dataReference, string jsonValue)
        {
            DataReference = dataReference;
            JsonValue = jsonValue;
        }
    }
}
