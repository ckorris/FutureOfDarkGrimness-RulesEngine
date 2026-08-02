using System.Threading.Tasks;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P12, the READ side of Regenerative Strength: "when in melee, pick one of its weapons to get
    /// +X attacks, where X is the number of markers on it." The markers themselves are placed by rule data
    /// at <see cref="EHookID.Lifecycle_OnWoundIgnored"/>; this is the spend.
    ///
    /// <para><b>Stage code, not rule data</b> — for the same reason <see cref="TargetMarkerSpend"/> is.
    /// The rule says "pick ONE of its weapons", and a declarative effect at the attack-count seam would
    /// have to add attacks to every melee weapon the unit swings, which is a different (and stronger)
    /// rule. The choice needs a prompt and a per-melee gate, neither of which the effect vocabulary can
    /// express. This is also why <see cref="EHookID.Shooting_OnPreHitRollCount"/> stays dormant: wiring a
    /// generic attack-count hook would mean authoring the wrong rule at it.</para>
    ///
    /// <para><b>Prompted, not automatic</b> (owner-signed): the player picks the weapon by accepting the
    /// offer on it. Declining costs nothing — the markers are never consumed, so a player saving them for
    /// a Deadly weapon later in the same swing loop loses nothing by saying no now. The default answer is
    /// <c>true</c> so an EOF/AI resolver takes the bonus on the first weapon offered.</para>
    /// </summary>
    public static class RegenerativeStrengthAttacks
    {
        /// <summary>
        /// Extra attacks this volley: 0 unless <paramref name="isMelee"/>, the attacker carries markers,
        /// and it hasn't already cashed them in this melee. On acceptance the gate token is stamped, so
        /// the unit's remaining weapons in the same melee are not offered.
        /// </summary>
        public static async Task<float> Offer(IGameContext gameContext, IUnit attacker, IWeapon weapon,
            bool isMelee)
        {
            if (!isMelee)
            {
                return 0f;
            }

            float markers = attacker.Tokens.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker);
            if (markers <= 0f || attacker.Tokens.HasToken(TokenType.RegenerativeStrengthSpent))
            {
                return 0f;
            }

            YesNoRequest request = new YesNoRequest(attacker.PlayerID,
                $"{attacker.Name} has {markers:0.##} Regenerative Strength marker(s). " +
                $"Add +{markers:0.##} attacks to {weapon.Name} this melee?",
                displayName: "Deciding on Regenerative Strength");

            bool accepted = await gameContext.PlayerRequester
                .RequestDecision<YesNoRequest, bool>(request);

            if (!accepted)
            {
                return 0f;
            }

            // Cleared by PostMeleeStage's Melee_OnPostMelee sweep - see the token's own doc for why an
            // ActivationEnd trigger would leak across a strike-back.
            attacker.Tokens.AddToken(new Token(TokenType.RegenerativeStrengthSpent, 1,
                new TokenClearTrigger.CustomHook(EHookID.Melee_OnPostMelee),
                CreatedAtHook: EHookID.Shooting_OnPreHitRollCount));

            gameContext.Log($"{attacker.Name}'s Regenerative Strength adds {markers:0.##} attacks to " +
                $"{weapon.Name}.");

            return markers;
        }
    }
}
