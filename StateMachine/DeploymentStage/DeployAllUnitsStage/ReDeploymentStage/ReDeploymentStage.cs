using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P21 Re-Deployment: the post-deployment re-deploy sub-phase. "After all other units are deployed
    /// (excluding units that were set aside), you may remove up to two friendly units from the table and
    /// deploy them again. Players alternate in placing Re-Deployment units, starting with the player that
    /// activates next."
    ///
    /// Runs once, after the normal deploy loop and BEFORE <c>PlaceDeferredUnitsStage</c> - so set-aside
    /// (Scout) units are still off-table and therefore not eligible, matching "excluding units that were set
    /// aside." Each player's budget is TWO redeploys per Re-Deployment unit they own (owner ruling: stacks
    /// with the count). Players alternate one unit at a time in activation order - the head of the deployment
    /// roll order activates first (<c>MainPhaseContext</c> seeds its turn order from the same
    /// <c>FirstDeploymentRollOrder</c>), so that is "the player that activates next." A player passes by
    /// declining the pick, which ends their participation; the sub-phase finishes when every player has
    /// passed or spent their budget.
    ///
    /// Re-Deployment is an engine marker (no dispatch hooks); this stage detects it by name, exactly as
    /// <c>ChooseUnitToActivateStage</c> does for Delayed Action.
    /// </summary>
    public class ReDeploymentStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        public ReDeploymentStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.LogDebug("Entered Re-Deployment stage.");

            // Players in activation order (the deployment roll order head activates first). For the common
            // 1v1 case this is [firstPlayer, secondPlayer] and the round-robin below alternates them exactly.
            List<(PlayerID Player, ITeam Team)> order = context.FirstDeploymentRollOrder
                .SelectMany(team => team.Players.Select(player => (player, team)))
                .ToList();

            var budget = new Dictionary<PlayerID, int>();
            foreach ((PlayerID player, ITeam _) in order)
            {
                budget[player] = 2 * CountReDeploymentUnits(player);
            }

            // Nobody has the rule - no sub-phase, no prompt.
            if (budget.Values.All(b => b == 0))
            {
                await OnFinish.Activate(context);
                return;
            }

            var done = new HashSet<PlayerID>(order.Where(o => budget[o.Player] == 0).Select(o => o.Player));
            var alreadyRedeployed = new HashSet<DataBinding<UnitData>>();

            context.Log("Re-Deployment: players may pick up and re-place units.");

            // Round-robin: one redeploy (or a pass) per player per cycle, until all are done. Terminates
            // because every iteration either marks a player done (pass / no eligible unit) or spends one of
            // their finite budget.
            while (order.Any(o => !done.Contains(o.Player)))
            {
                foreach ((PlayerID player, ITeam team) in order)
                {
                    if (done.Contains(player)) continue;

                    DataBinding<UnitData>? pick = await OfferRedeploy(context, player, budget[player], alreadyRedeployed);
                    if (pick == null)
                    {
                        done.Add(player);
                        continue;
                    }

                    await Redeploy(context, pick, team);
                    alreadyRedeployed.Add(pick);
                    if (--budget[player] == 0) done.Add(player);
                }
            }

            await OnFinish.Activate(context);
        }

        private int CountReDeploymentUnits(PlayerID player) =>
            GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(army => army.IsOwnedBy(player))
                .SelectMany(army => army.UnitBindings)
                .Count(unit => unit.GetValue().GetIsAlive() && HasReDeployment(unit.GetValue()));

        // Asks the rule graph for the CAPABILITY rather than testing for the Re-Deployment rule by
        // identity, so a second rule granting a re-deploy needs no change here.
        private bool HasReDeployment(UnitData unit) =>
            CapabilityRuleQueries.CanReDeploy(unit, GameContext.RuleEvaluator);

        /// <summary>
        /// Offers the player one of their living, on-table units (that hasn't already been redeployed) to
        /// pick up, or a pass (Cancelled). Set-aside units are off-table and so never eligible. Returns null
        /// on a pass or when there is nothing left to redeploy.
        /// </summary>
        private async Task<DataBinding<UnitData>?> OfferRedeploy(IDeploymentTurnContext context, PlayerID player,
            int remaining, HashSet<DataBinding<UnitData>> alreadyRedeployed)
        {
            List<DataBinding<UnitData>> eligible = GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(army => army.IsOwnedBy(player))
                .SelectMany(army => army.UnitBindings)
                .Where(unit => unit.GetValue().GetIsAlive()
                    && unit.GetValue().GetIsOnBattlefield()
                    && !alreadyRedeployed.Contains(unit))
                .ToList();

            if (eligible.Count == 0) return null;

            List<CancellableSelectionRequest<UnitData>.ValidOption> valid = eligible
                .Select(b => new CancellableSelectionRequest<UnitData>.ValidOption(b, b.GetValue().Name))
                .ToList();

            var request = new CancellableSelectionRequest<UnitData>(player,
                $"Re-Deploy a unit ({remaining} left), or pass",
                valid, new List<CancellableSelectionRequest<UnitData>.InvalidOption>(),
                displayName: "Choosing a Unit to Redeploy");

            CancellableResult<DataBinding<UnitData>> result = await GameContext.PlayerRequester
                .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(request);

            return result is Selected<DataBinding<UnitData>> selected ? selected.Value : null;
        }

        /// <summary>
        /// Picks the unit up and re-places it anywhere in its owner's deployment zone, reusing the normal
        /// mandatory-placement flow (as <c>DeployUnitStage</c> does for a first deployment).
        /// </summary>
        private async Task Redeploy(IDeploymentTurnContext context, DataBinding<UnitData> unitBinding, ITeam team)
        {
            UnitData unit = unitBinding.GetValue();
            RectangularZone zone = context.PlayerDeploymentZones![team].GetValue();

            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"Redeploying {unit.Name}",
                zone, unit.ModelBindings);

            // #282: commit-time overlap check, same as first deployment.
            List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard
                .RequestClearPlacement(GameContext, request);

            foreach (PlacedObjectEntry<ModelData> placement in placements)
            {
                placement.Binding.GetValue().SetPosition(placement.Position);
                if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
            }

            context.Log($"{unit.Name} was re-deployed.");
        }
    }
}
