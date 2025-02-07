using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Data.Serialization
{
    public interface IGameDataAware
    {
        void SetGameDataStore(IReadWriteableGameDataStore gameDataStore);
    }
}
