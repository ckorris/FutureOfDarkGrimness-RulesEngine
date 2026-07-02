using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
using FDG.SerializableVisuals;

namespace FDG
{
    public interface IModel
    {
        public const int DEFAULT_WOUND_COUNT = 1;

        /// <summary>
        /// Per-model special rules (#006 slice F). Mirrors <see cref="IUnit.RuleDefinitions"/> /
        /// <see cref="IWeapon.RuleDefinitions"/> but scoped to one model: the rule-dispatcher unions these
        /// into an event only for the model(s) actually involved (e.g. a weapon batch's sole owner on the
        /// hit hooks), so a joined hero's own rules (Furious/Relentless/Thrust) fire for the hero alone.
        /// Like the unit/weapon lists, not serialized — re-attached at army-load (the hero merge moves the
        /// hero's unit-scoped rules here), so it inherits the same save/load lifecycle as unit rules.
        /// </summary>
        public IReadOnlyList<ResolvedRule> RuleDefinitions { get; }

        /// <summary>
        /// Stable per-model identifier, used to name a specific model across JSON /
        /// network round-trips (e.g. the presentation-beat stream). Assigned at model
        /// creation. Mirrors <see cref="IUnit.ID"/>.
        /// </summary>
        public ModelID ID { get; }

        /// <summary>
        /// Per-model token container holding rule-system state. Used for
        /// model-scoped markers (e.g. Regenerative Strength accumulates a count
        /// on the specific model that ignored the wound). Tokens survive JSON /
        /// network round-trips; subscriptions to the container's events do not.
        /// </summary>
        public ITokenContainer Tokens { get; }

        public float TotalWounds { get; }

        public float WoundsDealt { get;}

        public Position Position { get; }

        /// <summary>
        /// This model's base footprint (#149) — circle, rectangle, or a future shape. The source of truth
        /// for the base; <see cref="BaseRadiusInches"/> is its circumscribing circle.
        /// </summary>
        public IBaseShape BaseShape { get; }

        /// <summary>
        /// Circumscribing-circle radius of <see cref="BaseShape"/>. Retained so radius-based geometry
        /// (terrain swept-paths, LoS blockers, objective seizure — the bounding-circle paths in #150) and
        /// rendering fallbacks keep working unchanged. Shape-aware geometry reads <see cref="BaseShape"/>.
        /// </summary>
        public float BaseRadiusInches => BaseShape.BoundingRadiusInches;

        /// <summary>
        /// This model's facing on the table: a yaw-only orientation as a unit normal in the X/Z plane,
        /// pointing along the base's local +Z ("forward" / height) axis. Default (0,1) reproduces the
        /// pre-facing axis-aligned layout (a <see cref="RectangleBase"/>'s width→X, height→Z), so existing
        /// armies are geometrically and visually unchanged. Drives oriented base geometry (#150) and
        /// rendering; an Aircraft derives its flight heading from it (asserting all its models share one).
        /// </summary>
        public Float2 Facing { get; }

        public IReadOnlyList<Weapon> Weapons { get; }

        public IMeshProvider MeshProvider { get; }

        public IMaterialProvider MaterialProvider { get; }

        public void SetPosition(Position newPosition);

        /// <summary>
        /// Sets this model's yaw facing — a unit normal in the table's X/Z plane (see <see cref="Facing"/>).
        /// </summary>
        public void SetFacing(Float2 facingNormal);

        /// <summary>
        /// Sets this model's maximum wounds (Tough) and fills it to that maximum. A creation-time
        /// primitive — applied once at unit setup before any wounds are dealt.
        /// </summary>
        public void SetMaxWounds(int maxWounds);

        public void DealWounds(float wounds);

        public event DataValueChangedHandler<Position> OnPositionChanged;

        public event DataValueChangedHandler<Float2> OnFacingChanged;

        public event DataValueChangedHandler<float> OnWoundsDealt;
    }

    /*
    public class Model : IModel
    {

        public float TotalWounds { get; }

        public float WoundsDealt { get; set; }

        public List<IWeapon> Weapons { get; }

        public Position Position { get; private set; }

        public float BaseRadiusInches { get; private set; }

        public event PositionChangedEventHandler OnPositionChanged;

        public event WoundsDealtEventHandler OnWoundsDealt;


        public Model(List<IWeapon> weapons, Position position, float baseRadiusInches)
        {
            Weapons = weapons;
            Position = position;
            //TODO: Not sure where Tough will come in for modifying wounds, but there should be a stage
            //that processes that kind of thing.

            TotalWounds = IModel.DEFAULT_WOUND_COUNT;
            WoundsDealt = 0;
            BaseRadiusInches = baseRadiusInches;
        }

        public void SetPosition(Position newPosition)
        {
            Position oldPosition = Position;
            Position = newPosition;

            OnPositionChanged?.Invoke(new PositionChangedEventArgs(newPosition, oldPosition));
        }

        public void DealWounds(float wounds)
        {
            WoundsDealt += wounds;

            OnWoundsDealt?.Invoke(new WoundsDealtEventArgs(wounds, TotalWounds - WoundsDealt, WoundsDealt >= TotalWounds));
        }
    }    
    */

    public static class IModelExtensions
    {
        public static bool GetIsAlive(this IModel model)
        {
            return model.WoundsDealt < model.TotalWounds;
        }

        public static bool GetIsDead(this IModel model)
        {
            return model.WoundsDealt >= model.TotalWounds;
        }

        public static float BaseDistanceToOtherModel_2D(this IModel thisModel, IModel otherModel)
        {
            // Facing-aware: an oriented rectangular base measures by its true footprint, not an axis-aligned
            // or circular approximation (#150).
            return DistanceUtilities.GetBaseToBaseDistanceInches_2D(thisModel.Position, otherModel.Position,
                thisModel.BaseShape, thisModel.Facing, otherModel.BaseShape, otherModel.Facing);
        }

        public static float BaseDistanceToOtherModel_3D(this IModel thisModel, IModel otherModel)
        {
            return DistanceUtilities.GetBaseToBaseDistanceInches_3D(thisModel.Position, otherModel.Position,
                thisModel.BaseShape, thisModel.Facing, otherModel.BaseShape, otherModel.Facing);
        }
    }

}
