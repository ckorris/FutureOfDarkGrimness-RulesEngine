

using FDG.BuiltInAssets;
using FDG.Data;
using FDG.SerializableVisuals.Materials;
using FDG.SerializableVisuals.Meshes;
using FDG.SerializableVisuals.Textures;
using FDG.StageResolution.Requests;
using FDG.TempVisuals;
using System.Numerics;

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
            context.Log("Entered Choose Map Side stage.");

            List<ITeam> teamOrderedByRoll = context.MapSideRollOrder;

            List<RectangularZone> zoneOptions = GetRectangularZones(teamOrderedByRoll.Count);

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

            
            Dictionary<ITeam, DataBinding<RectangularZone>> choices = new Dictionary<ITeam, DataBinding<RectangularZone>>(teamOrderedByRoll.Count);

            for(int i = 0; i < teamOrderedByRoll.Count; i++)
            {
                ITeam thisTeam = teamOrderedByRoll[i];

                //If there's only one option left, take that one.
                if(zoneBindings.Count == 1)
                {
                    context.Log("Auto-assigning deployment zone as only one is left to pick.");
                    choices.Add(thisTeam, zoneBindings.First());
                    zoneBindings.RemoveAt(0); //Still removing so that we can hit the next exception if the team count is malaligned.
                    continue;
                }
                else if (zoneBindings.Count == 0)
                {
                    throw new Exception("Somehow ran out of zones before we went through all the teams.");
                }

                //Have the first player on the team choose a zone.
                PlayerID firstTeamPlayer = thisTeam.Players.First();

                context.Log($"Requesting player choose deployment zone.");
                ChooseDeploymentZoneRequest request = new ChooseDeploymentZoneRequest(firstTeamPlayer,
                    "Choose Deployment Zone", zoneBindings, choices.Values.ToList());

                DataBinding<RectangularZone> choice 
                    = await context.PlayerRequester().RequestDecision<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>
                        (firstTeamPlayer, request);

                zoneBindings.Remove(choice);

                choices.Add(thisTeam, choice);
            }

            context.SetDeploymentZones(choices);

            ToRollForFirstDeployment.Activate(context);
        }

        private void TempTestVisuals(IDeploymentContext context)
        {
            //TODO: This doesn't belong here but I want to make sure we can make visuals happen from the stage machine.
            System.Diagnostics.Debug.WriteLine("Testing drawing something in ChooseMapSideStage.");
            var meshProvider = new BuiltInObjMeshProvider(BuiltInAssetHelper.SILLYMANMODEL_PATH);
            var textureProvider = new BuiltInPngTextureProvider(BuiltInAssetHelper.SILLYMANTEXTURE_PATH);

            var materialProvider = new BasicMaterial();
            materialProvider.BaseColor = new Vector4(1, 1, 1, 1);
            materialProvider.BaseColorTexture = textureProvider;

            Position position = new Position(10f, 25f);
            Quaternion rotation = new Quaternion(Vector3.UnitY, 0f);
            Vector3 scale = new Vector3(1, 1, 1);

            TempVisual tempVisual1 = new TempVisual(meshProvider, materialProvider, position, rotation, scale);
            context.GameContext.TempVisualDrawer.AddVisual(tempVisual1);

            position = new Position(12f, 25f);

            TempVisual tempVisual2 = new TempVisual(meshProvider, materialProvider, position, rotation, scale);
            context.GameContext.TempVisualDrawer.AddVisual(tempVisual2);

            position = new Position(14f, 25f);
            TempVisual tempVisual3 = new TempVisual(meshProvider, materialProvider, position, rotation, scale);
            context.GameContext.TempVisualDrawer.AddVisual(tempVisual3);
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
