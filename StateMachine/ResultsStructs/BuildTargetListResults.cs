using FDG.Data;

namespace FDG
{

    public struct BuildTargetListResults
    {
        public List<DataBinding<ModelData>> OrderedTargetList;

        public BuildTargetListResults(List<DataBinding<ModelData>> orderedTargetList)
        {
            OrderedTargetList = orderedTargetList;
        }
    }
}