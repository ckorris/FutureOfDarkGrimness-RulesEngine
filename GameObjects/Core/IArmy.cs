

namespace FDG
{
    public interface IArmy : IPlayerOwnable
    {
        public IReadOnlyList<IUnit> Units { get; }
    }
}
