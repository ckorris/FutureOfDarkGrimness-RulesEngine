using FDG.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{
    internal class ModifyDataMessage
    {
        public DataReference DataReference;

        public string DataAsJson;

        public ModifyDataMessage(DataReference dataReference, string dataAsJson)
        {
            DataReference = dataReference;
            DataAsJson = dataAsJson;
        }
    }
}
