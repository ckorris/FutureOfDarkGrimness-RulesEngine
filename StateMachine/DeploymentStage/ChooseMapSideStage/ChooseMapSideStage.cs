

using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ChooseMapSideStage : StageBase<IDeploymentContext>
    {
        public StageBinding ToRollForFirstDeployment;

        public ChooseMapSideStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            ToRollForFirstDeployment = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentContext context)
        {
            List<RectangularZone> zoneOptions = new List<RectangularZone>();

            List<DataBinding<RectangularZone>> zoneBindings = new List<DataBinding<RectangularZone>>(zoneOptions.Count);

            for(int i = 0; i < zoneOptions.Count; i++)
            {
                RectangularZone zone = zoneOptions[i];

                DataReference zoneReference = context.GameDataStore().Create(zone);
                DataBinding<RectangularZone> zoneBinding = context.GameDataStore().GetDataBinding<RectangularZone>(zoneReference);
                zoneBindings.Add(zoneBinding);
            }

            if(context.MapSideRollOrder == null)
            {
                throw new NullReferenceException();
            }

            List<ITeam> teamOrderedByRoll = context.MapSideRollOrder;

            Dictionary<ITeam, DataBinding<RectangularZone>> choices = new Dictionary<ITeam, DataBinding<RectangularZone>>(teamOrderedByRoll.Count);

            for(int i = 0; i < teamOrderedByRoll.Count; i++)
            {
                ITeam thisTeam = teamOrderedByRoll[i];

                //If there's only one option left, take that one.
                if(zoneBindings.Count == 1)
                {
                    choices.Add(thisTeam, zoneBindings.First());
                }
                else if (zoneBindings.Count == 0)
                {
                    throw new Exception("Somehow ran out of zones before we went through all the teams.");
                }

                //Have the first player on the team choose a zone.
                PlayerID firstTeamPlayer = thisTeam.Players.First();

                ChooseDeploymentZoneRequest request = new ChooseDeploymentZoneRequest(firstTeamPlayer,
                    "Choose Deployment Zone", zoneBindings, choices.Values.ToList());

                DataBinding<RectangularZone> choice 
                    = await context.PlayerRequester().RequestDecision<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>
                        (firstTeamPlayer, request);

                choices.Add(thisTeam, choice);
            }

            context.SetDeploymentZones(choices);
        }


        private List<RectangularZone> GetRectangularZones(int teamCount)
        {
            if(teamCount <= 0)
            {
                throw new InvalidOperationException($"Can't have fewer than one team.");
            }

            //We need to return equally-sized zones, and there are two sides to the table.
            //That means there will be an extra zone when the team number is odd.

            int zonesPerSide = (teamCount + 1) / 2; //Rounds up.

            float zoneWidth = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / zonesPerSide;

            List<RectangularZone> zones = new List<RectangularZone>(zonesPerSide * 2);

            for(int i = 0; i < zonesPerSide; i++)
            {
                float left = i * zoneWidth;
                float right = left + zoneWidth;

                //Bottom zone.
                zones.Add(new RectangularZone(left, right, 0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES));

                //Top zone.
                zones.Add(new RectangularZone(left, right,
                    GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES,
                    GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES));
            }

            return zones;
        }
    }
}
