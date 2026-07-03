using FDG.Players;
using FDG.SaveLoad;
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
                .RegisterType<string>(32)
                .RegisterType<Position>(128)
                .RegisterType<ModelData>(64)
                .RegisterType<PlayerSlotInfo>(8)
                .RegisterType<TeamData>(2)
                .RegisterType<UnitData>(32)
                .RegisterType<ArmyData>(8)
                .RegisterType<TerrainData>(32)
                .RegisterType<RectangularZone>(16)
                .RegisterType<ObjectiveData>(8)
                .RegisterType<PlayerID>(8)
                // Appended last: TypeID is positional and baked into every serialized DataReference,
                // so new types must go at the end to keep older type maps / saves valid.
                .RegisterType<GameProgressData>(2)
                // #150: per-model facing (Float2 unit normal), one binding per model — sized like Position.
                .RegisterType<Float2>(128)
                .Build();
            }
        }

        public record TypeAndCapacity(Type Type, int Capacity);
    }
}
