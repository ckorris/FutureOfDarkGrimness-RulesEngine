namespace FDG.Stages
{
    /// <summary>
    /// Drives D3+2 alternating objective placement (GF Beginner's Guide v3.5.1 p.6).
    /// Reads the rolled count + starting team order from the surrounding
    /// <see cref="IMapSetupContext"/>, builds a per-turn context that owns
    /// the round-robin cursor and placement counter, and bounces between
    /// <see cref="DetermineNextObjectivePlacerStage"/> and
    /// <see cref="PlaceOneObjectiveStage"/> until every marker is on the table.
    /// </summary>
    public class PlaceObjectivesStage : ParentStage<IMapSetupContext, IObjectivePlacementTurnContext>
    {
        public StageBinding OnObjectivesPlaced;

        // The rules say "outside the deployment zones" — i.e. 12" from each long edge.
        // We intentionally do NOT use GameWideConstants.DEPLOYMENT_DISTANCE_INCHES here
        // because that constant is currently 9" and is also read by deployment code;
        // changing the deployment depth is out of scope for this work item.
        private const float ObjectiveLegalBandMarginInches = 12f;

        public PlaceObjectivesStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnObjectivesPlaced = new StageBinding(this);
        }

        protected override IObjectivePlacementTurnContext GetNewChildContext(IMapSetupContext contextSelf)
        {
            if (contextSelf.ObjectiveCount is not int count)
                throw new InvalidOperationException(
                    $"{nameof(PlaceObjectivesStage)} entered before {nameof(IMapSetupContext.ObjectiveCount)} was set.");

            if (contextSelf.ObjectivePlacementTeamOrder is not IReadOnlyList<ITeam> teamOrder)
                throw new InvalidOperationException(
                    $"{nameof(PlaceObjectivesStage)} entered before {nameof(IMapSetupContext.ObjectivePlacementTeamOrder)} was set.");

            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            var legalBand = new RectangularZone(
                left: 0f,
                right: tableW,
                bottom: ObjectiveLegalBandMarginInches,
                top: tableH - ObjectiveLegalBandMarginInches);

            return new ObjectivePlacementTurnContext(GameContext, teamOrder, count, legalBand);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IObjectivePlacementTurnContext> startingChild)
        {
            var determineNext = new DetermineNextObjectivePlacerStage(GameContext, this);
            var placeOne = new PlaceOneObjectiveStage(GameContext, this);

            var dictionary = new TransitionSetBuilder(this)
                .AddChild(determineNext)
                .AddChild(placeOne)
                .AddSibling(nameof(OnObjectivesPlaced), OnObjectivesPlaced, out string toParentEvent)
                .Build();

            startingChild = determineNext;

            determineNext.OnPlacerDetermined.Bind(placeOne);
            placeOne.OnObjectivePlaced.Bind(determineNext);
            determineNext.OnAllMarkersPlaced.Bind(toParentEvent);

            return dictionary;
        }
    }
}
