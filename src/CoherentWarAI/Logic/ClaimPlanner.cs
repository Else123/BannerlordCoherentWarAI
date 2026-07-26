using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free coordination between parties of the same realm.
    ///
    /// Vanilla scores every target independently for every lord, so the fief that
    /// looks best to one lord looks best to all of them and they pile onto it
    /// together while the rest of the front goes unattended. Nothing in the vanilla
    /// offensive scoring counts how much force is already committed somewhere.
    ///
    /// This works out how much strength a target actually warrants and damps the
    /// score for everyone arriving after that is met, so surplus lords look
    /// elsewhere instead of joining a queue.
    /// </summary>
    public static class ClaimPlanner
    {
        /// <summary>Strength worth sending relative to the defenders.</summary>
        public const float DefaultRequiredMargin = 2f;

        /// <summary>How far an over-subscribed target is pushed down.</summary>
        public const float DefaultSaturationSuppression = 0.3f;

        /// <summary>
        /// Extra credit for a target nobody is dealing with yet. Neutral by default:
        /// in a real campaign almost no target has anyone committed to it, so this
        /// was being handed to nearly every candidate - which is not a preference,
        /// just a shifted scale. The damping of over-subscribed targets is the part
        /// that actually separates them.
        /// </summary>
        public const float DefaultNeglectBonus = 1f;

        /// <summary>
        /// How strongly gateways attract defenders. Deliberately modest: patrolling
        /// scores compete against the scores for answering an actual attack, and a
        /// gate is only worth watching while nothing is burning.
        /// </summary>
        public const float DefaultGatewayDefenseGain = 0.8f;

        /// <summary>
        /// Strength that taking a settlement is worth committing: its defenders
        /// times a margin, floored so an empty fief still warrants a real party.
        /// </summary>
        public static float RequiredStrength(float defenderStrength, float margin)
        {
            float defenders = Math.Max(defenderStrength, TargetWeights.MinDefenderStrength);
            float required = defenders * Math.Max(0f, margin);
            return required < defenders ? defenders : required;
        }

        /// <summary>
        /// Score multiplier for a party considering a target others are already
        /// heading for.
        ///
        /// Below what the target needs, nothing changes - reinforcing an
        /// insufficient effort is correct. Once enough is committed the score is
        /// pushed down toward <paramref name="suppression"/>, so the next lord
        /// prefers somewhere else. A target nobody has claimed is nudged up.
        /// </summary>
        /// <param name="committedStrength">Strength already heading there, excluding this party.</param>
        /// <param name="requiredStrength">Strength the target warrants.</param>
        /// <param name="suppression">Multiplier once fully over-subscribed.</param>
        /// <param name="neglectBonus">Multiplier when nothing is committed at all.</param>
        public static float SaturationBias(float committedStrength, float requiredStrength, float suppression, float neglectBonus)
        {
            if (committedStrength <= 0f)
            {
                return Math.Max(0f, neglectBonus);
            }
            if (requiredStrength <= 0f)
            {
                return 1f;
            }

            float ratio = committedStrength / requiredStrength;
            if (ratio <= 1f)
            {
                return 1f;
            }

            // Fade from 1 down to the suppression floor as commitment runs from
            // "just enough" to "twice what is needed".
            float over = Math.Min(1f, ratio - 1f);
            float floor = Math.Max(0f, suppression);
            return 1f + (floor - 1f) * over;
        }

        /// <summary>
        /// How strongly a defending party should be drawn to a settlement, given how
        /// much of the realm lies behind it. Standing at the gate beats patrolling
        /// wherever the last alarm happened to come from.
        /// </summary>
        public static float GatewayDefenseBias(float gatewayScore, float gain)
        {
            float score = gatewayScore < 0f ? 0f : (gatewayScore > 1f ? 1f : gatewayScore);
            float bias = 1f + Math.Max(0f, gain) * score;
            return bias < 0f ? 0f : bias;
        }
    }
}
