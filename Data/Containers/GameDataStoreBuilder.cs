using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Data
{
    public partial class GameDataStore
    {
        public class GameDataStoreBuilder
        {
            private List<TypeAndCapacity> _registeredTypes = new List<TypeAndCapacity>() 
            {
                new TypeAndCapacity(typeof(UnreferenceableTypeStruct), 0)
            };

            private bool _hasBuilt = false;

            public GameDataStoreBuilder RegisterType<T>(int capacity)
            {
                Type type = typeof(T); //Shorthand.
                if (_registeredTypes.FirstOrDefault( t => t.Type == type) != default)
                {
                    throw new ArgumentException($"Tried to register type {type} but it was already registered.");
                }

                _registeredTypes.Add(new TypeAndCapacity(type, capacity));

                TypeID typeID = new TypeID(_registeredTypes.Count - 1);

                if (capacity <= 0)
                {
                    capacity = DEFAULT_COMPONENT_STORE_CAPACITY;
                }

                return this;
            }

            public GameDataStore Build()
            {
                if(_hasBuilt)
                {
                    throw new InvalidOperationException($"Tried to build {nameof(GameDataStoreBuilder)} that was already built.");
                }

                _hasBuilt = true;

                return new GameDataStore(_registeredTypes);
            }

            public static GameDataStore GetDefault()
            {
                return new GameDataStoreBuilder()
                .RegisterType<int>(64)
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<ModelData>(64)
                .RegisterType<TeamData>(2)
                .RegisterType<UnitData>(32)
                .RegisterType<ArmyData>(8)
                .RegisterType<Terrain>(8)
                .Build();
            }
        }

        public record TypeAndCapacity(Type Type, int Capacity);
    }
}
