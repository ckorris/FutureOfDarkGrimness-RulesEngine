

using FDG;
using FDG.Stages;
using NUnit.Framework.Internal;
using System;
using System.Text;

public static class DiceUtilities
{
    public const int MINIMUM_SUCCESS_ROLL = 2;

    public const int MAXIMUM_SUCCESS_ROLL = 6;

    /// <summary>
    /// Forces a number to be within the range of possible rolls on a D6 that can ever be successful. 
    /// Use this to keep the rule true that a 6 is always a success and a 1 is always a failure 
    /// after applying modifiers. 
    /// </summary>
    /// <param name="unclampedRoll"></param>
    /// <returns></returns>
    public static int ClampSuccessRollNeeded(int unclampedRoll)
    {
        return Math.Clamp(unclampedRoll, MINIMUM_SUCCESS_ROLL, MAXIMUM_SUCCESS_ROLL);
    }

    public static T RollOff_SingleWinner<T>(List<T> competitors, List<string> competitorNames, ITextOutput logger)
    {
        if (competitors.Count != competitorNames.Count)
        {
            throw new InvalidOperationException($"Must have equal number of {nameof(competitors)} and {nameof(competitorNames)}.");
        }

        Random random = new Random();

        if (competitors.Count < 1)
        {
            throw new InvalidOperationException($"No competitors present in {nameof(RollForMapSideStage)}.");
        }

        //Have all competitors roll off. If any tie, remove the others and repeat.
        do
        {
            logger.Log($"Rolling off {competitors.Count} ways.");

            //Have all competitors roll a D6, and as long as the highest value isn't tied, that's the winner.
            //We could just pick a competitor at random but doing it this way lets us show cool dice rolling
            //results and makes it feel more like a game.
            //Also note that we're not using the DiceRoller in context because that may not be random.
            //However this roll always has to be random.
            int[] rolls = new int[competitors.Count];

            int highest = -1;

            for (int i = 0; i < competitors.Count; i++)
            {
                int roll = random.Next(1, 7); //1 through 7, inclusive of 6.
                rolls[i] = roll;

                logger.Log($"{competitorNames[i]} rolled {roll}.");

                highest = Math.Max(highest, roll);
            }

            //Not the most optimized to keep allocating lists but it's rare and not critical, so I prefer readability.
            List<T> winners = new List<T>();
            List<string> winnerNames = new List<string>();

            for (int i = 0; i != competitors.Count; i++)
            {
                if (rolls[i] == highest)
                {
                    winners.Add(competitors[i]);
                    winnerNames.Add(competitorNames[i]);
                }
            }

            //TODO: Show visuals.

            if (winners.Count > 1)
            {
                StringBuilder sb = new StringBuilder($"Tie between {winners[0]}");

                for (int i = 1; i < winners.Count; i++)
                {
                    sb.Append($" and {winners[i]}");
                }
                sb.Append('.');

                logger.Log(sb.ToString());
            }

            competitors = winners;
            competitorNames = winnerNames;
        }
        while (competitors.Count > 1);

        return competitors[0];
    }
    /// <summary>
    /// Has all elements of <paramref name="competitors"/> roll off, and tiebreak at all levels, returning a fully-ordered
    /// list where the first element is the top winner, the second is the next winner, and so on.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="competitors">Competitors by type.</param>
    /// <param name="competitorNames">Names to use in log messages for each competitor.</param>
    /// <param name="logger">Log object used for printing logs.</param>
    /// <returns>Ordered list of winners.</returns>
    /// <exception cref="InvalidOperationException">If <paramref name="competitors"/> and <paramref name="competitorNames"/> don't match in length.</exception>
    /// <exception cref="InvalidOperationException">If <paramref name="competitors"/> doesn't have at least two values.</exception>
    public static List<T> RollOff_Ordered<T>(List<T> competitors, List<string> competitorNames, ITextOutput logger)
    {
        if (competitors.Count != competitorNames.Count)
        {
            throw new InvalidOperationException($"Must have equal number of {nameof(competitors)} and {nameof(competitorNames)}.");
        }

        Random random = new Random();


        return RankGroup<T>(competitors, competitorNames, logger, random);
    }


    /// <summary>
    /// Recursive helper for <see cref="RollOff_Ordered{T}(List{T}, List{string}, ITextOutput)"/>
    private static List<T> RankGroup<T>(List<T> group, List<string> groupNames, ITextOutput logger, Random random)
    {
        if (group.Count == 1)
        {
            return new List<T> { group[0] };
        }

        int count = group.Count;

        logger.Log($"Rolling off {count} ways.");

        int[] rolls = new int[count];
        int highest = -1;

        for (int i = 0; i < count; i++)
        {
            int roll = random.Next(1, 7);
            rolls[i] = roll;
            logger.Log($"{groupNames[i]} rolled {roll}.");
            highest = Math.Max(highest, roll);
        }

        //Bucket competitors by their roll.
        Dictionary<int, List<T>> rollBuckets = new Dictionary<int, List<T>>();
        Dictionary<int, List<string>> nameBuckets = new Dictionary<int, List<string>>();

        for (int i = 0; i < count; i++)
        {
            int roll = rolls[i];

            if (rollBuckets.TryGetValue(roll, out List<T>? bucket) == false)
            {
                bucket = new List<T>();
                rollBuckets[roll] = bucket;
                nameBuckets[roll] = nameBuckets[roll];
            }

            bucket.Add(group[i]);
            nameBuckets[roll].Add(groupNames[i]);
        }

        //Process from highest roll downward so upper tiers stay permanently ahead.
        List<int> sortedKeys = rollBuckets.Keys.OrderByDescending(x => x).ToList();
        List<T> ranked = new List<T>();

        foreach (int key in sortedKeys)
        {
            List<T> bucket = rollBuckets[key];
            List<string> names = nameBuckets[key];

            if (bucket.Count == 1)
            {
                ranked.Add(bucket[0]);
            }
            else
            {
                StringBuilder sb = new StringBuilder($"Tie between {names[0]}");
                for (int i = 1; i < names.Count; i++)
                {
                    sb.Append($" and {names[i]}");
                }
                sb.Append('.');
                logger.Log(sb.ToString());

                ranked.AddRange(RankGroup(bucket, names, logger, random));

            }
        }

        return ranked;
    }


}


