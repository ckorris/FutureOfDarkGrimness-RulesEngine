
using System.Collections.Generic;

namespace FDG
{

    public struct BuildTargetListResults
    {
        public List<IModel> OrderedTargetList;

        public BuildTargetListResults(List<IModel> orderedTargetList)
        {
            OrderedTargetList = orderedTargetList;
        }
    }
}