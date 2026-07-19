
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{

    public interface IUnitActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> ActivatingUnit { get; }

        public bool HasMoved { get; }

        public float MoveDistance { get;}

        public bool HasAttacked { get; }

        /// <summary>
        /// The custom action (#010) the player chose in <see cref="ChooseActionStage"/>, handed to
        /// <see cref="CustomActionStage"/> to resolve. Null when no custom action is pending. Set by the
        /// chooser, cleared once the custom-action stage has resolved it (and at activation start).
        /// </summary>
        public AbilityOffer? PendingCustomAction { get; }

        public void SetPendingCustomAction(AbilityOffer offer);

        public void ClearPendingCustomAction();

        /// <summary>
        /// Whether the unit was Shaken at the instant this activation began (snapshotted in
        /// <see cref="Reset"/>). This is the signal that decides Shaken recovery: a unit Shaken
        /// at activation start must idle this activation and recover, whereas a unit that becomes
        /// Shaken *during* its activation keeps the token for its next one. Captured explicitly so
        /// the recovery rule doesn't have to infer "activation start" from the action flags.
        /// </summary>
        public bool StartedActivationShaken { get; }

        /// <summary>
        /// How far <paramref name="enemyUnit"/> was from the activating unit at the instant this activation
        /// began — before the unit moved. This is the distance a charge is *declared* from: the engine models
        /// Charge as the melee attack and the approach as a separate Move action, so "charges an enemy over 9"
        /// away" means the enemy was over 9" away when the unit started its activation and set off. Snapshotted
        /// in <see cref="Reset"/> against every enemy unit, because a charge's defender isn't picked until the
        /// move has already happened and the pre-move geometry is gone.
        ///
        /// False (and 0) for a unit that was dead, unreachable, or absent at activation start. Read by
        /// <c>CombatActionContext</c> to fill <see cref="ICombatMetadata.ChargeOriginDistanceInches"/>.
        /// </summary>
        public bool TryGetActivationStartDistanceTo(UnitID enemyUnit, out float distanceInches);

        public void RegisterMoveFinished(float distance);

        public void RegisterAttackedFinished();

        /// <summary>
        /// #248: true once ANYTHING irreversible has happened this activation beyond HasMoved/HasAttacked —
        /// an activation-start rule applied (token ops, reposition, ThisActivation grant), spell tokens were
        /// spent, a custom action resolved, a teleport landed. Gates the "back out to unit selection" offer:
        /// while false (and not moved/attacked), un-picking the unit is safe because re-activating it re-runs
        /// ActivationStartStage against unchanged state.
        /// </summary>
        public bool IrreversibleActionTaken { get; }

        /// <summary>Marks the activation dirty (see <see cref="IrreversibleActionTaken"/>). Called by every
        /// stage at the exact point it commits something that cannot be undone.</summary>
        public void MarkIrreversibleAction();

        public void Reset(DataBinding<UnitData> activatingUnit);
    }

    public class UnitActionContext : IUnitActionContext
    {
        public IGameContext GameContext { get; private set; }

        public DataBinding<UnitData> ActivatingUnit { get; private set; }

        public bool HasMoved { get; private set; }

        public float MoveDistance { get; private set; } = 0f;

        public bool HasAttacked { get; private set; }

        public bool StartedActivationShaken { get; private set; }

        public AbilityOffer? PendingCustomAction { get; private set; }

        // Enemy UnitID -> distance at activation start. Rebuilt each Reset; empty until then.
        private readonly Dictionary<UnitID, float> _distanceAtActivationStart = new Dictionary<UnitID, float>();


        public UnitActionContext(IGameContext gameContext, DataBinding<UnitData> activatingUnit)
        {
            GameContext = gameContext;
            ActivatingUnit = activatingUnit;
        }

        public bool TryGetActivationStartDistanceTo(UnitID enemyUnit, out float distanceInches)
        {
            return _distanceAtActivationStart.TryGetValue(enemyUnit, out distanceInches);
        }

        public void SetPendingCustomAction(AbilityOffer offer)
        {
            PendingCustomAction = offer;
        }

        public void ClearPendingCustomAction()
        {
            PendingCustomAction = null;
        }

        public void RegisterMoveFinished(float distance)
        {
            HasMoved = true;
            MoveDistance = distance;
        }

        public void RegisterAttackedFinished()
        {
            HasAttacked = true;
        }

        public bool IrreversibleActionTaken { get; private set; }

        public void MarkIrreversibleAction()
        {
            IrreversibleActionTaken = true;
        }

        //TODO: This pattern sucks, make a new instance of the context each time.
        public void Reset(DataBinding<UnitData> activatingUnit)
        {
            ActivatingUnit = activatingUnit;
            HasMoved = false;
            MoveDistance = 0f;
            HasAttacked = false;
            IrreversibleActionTaken = false;
            PendingCustomAction = null;
            StartedActivationShaken = activatingUnit.GetValue().Tokens.HasToken(TokenType.Shaken);
            SnapshotDistancesToEnemies(activatingUnit);
        }

        // #197: the pre-move geometry a charge is declared from. Captured here, once, because by the time a
        // charge's defender is known the unit has already moved into base contact and the distance it charged
        // from is unrecoverable. Cheap: one min-distance pass per enemy unit, once per activation.
        private void SnapshotDistancesToEnemies(DataBinding<UnitData> activatingUnit)
        {
            _distanceAtActivationStart.Clear();

            IUnit self = activatingUnit.GetValue();
            PlayerID owner = self.PlayerID;

            TeamData? ownerTeam = GameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            foreach (ArmyData enemyArmy in GameContext.GameDataStore().GetAllValues<ArmyData>()
                         .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyBinding in enemyArmy.UnitBindings)
                {
                    IUnit enemy = enemyBinding.GetValue();
                    if (enemy.GetIsDead()) continue;

                    // Same measurement the charge/melee gates use: living model to living model, closest pair,
                    // including the vertical component.
                    _distanceAtActivationStart[enemy.ID] = UnitCompareUtilities.MinDistanceBetweenUnits(
                        self, enemy, out _, out _, includeVertical: true);
                }
            }
        }
    }

    public static class IUnitActionContextExtensions
    {
        public static PlayerID ActivatingPlayer(this IUnitActionContext context)
        {
            return context.ActivatingUnit.GetValue().PlayerID;
        }
    }
}