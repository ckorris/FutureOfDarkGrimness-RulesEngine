using System.Collections.Generic;

namespace FDG.Rules.Definitions;

/// <summary>
/// Terrain-proximity reads for rule conditions - "if a unit ... has most of them within Nin of terrain"
/// (the Grounded family: Grounded Reinforcement/Precision/Stealth). Kept beside the other rule-query
/// seams (RangeRuleQueries, SightRuleQueries) as the shared, directly-testable geometry.
///
/// Distance is measured from each living model's base EDGE: the base's bounding radius inflates the
/// zone-proximity test, exactly as <see cref="SweptBaseGeometry"/> tests a base against a zone, so
/// "within 1in of terrain" means the footprint is within 1in, not the model centre.
/// </summary>
public static class TerrainProximityQueries
{
    /// <summary>
    /// True when a strict majority of <paramref name="unit"/>'s living models are within
    /// <paramref name="inches"/> of any piece in <paramref name="terrain"/>. Empty terrain, or no living
    /// models, is false - the bonus is only ever granted when the positioning clearly earns it.
    /// </summary>
    public static bool MostModelsWithinInches(IUnit unit, IReadOnlyList<ITerrain> terrain, float inches)
    {
        if (terrain.Count == 0)
        {
            return false;
        }

        int living = 0;
        int near = 0;
        foreach (IModel model in unit.Models)
        {
            if (!model.GetIsAlive())
            {
                continue;
            }

            living++;
            if (IsModelWithinInches(model, terrain, inches))
            {
                near++;
            }
        }

        return living > 0 && near * 2 > living;
    }

    /// <summary> True if <paramref name="model"/>'s base is within <paramref name="inches"/> of any piece. </summary>
    public static bool IsModelWithinInches(IModel model, IReadOnlyList<ITerrain> terrain, float inches)
    {
        var centre = new Float2(model.Position.x, model.Position.z);
        float reach = model.BaseShape.BoundingRadiusInches + inches;
        foreach (ITerrain piece in terrain)
        {
            // A degenerate path (centre -> centre) inflated by the reach is a disc of that radius at the
            // model: it intersects the zone iff the zone lies within the reach of the centre. The explicit
            // containment check keeps the intent obvious for zones that treat their interior specially.
            if (piece.IsPointWithinZone(centre) || piece.DoesPathIntersectZone(centre, centre, reach))
            {
                return true;
            }
        }

        return false;
    }
}
