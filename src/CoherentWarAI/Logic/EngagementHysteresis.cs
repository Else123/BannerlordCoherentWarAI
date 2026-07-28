using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free hysteresis for target commitment.
    ///
    /// Vanilla re-evaluates every target from scratch on each AI tick against a
    /// single hard threshold, so a lord approaching a castle flips between
    /// "attack" and "abort" whenever the defenders change - most visibly when a
    /// player walks in and out of the settlement. That is also exploitable: stand
    /// outside to bait the commitment, then slip back in.
    ///
    /// Vanilla already applies this idea to *ongoing* sieges (it demands a lower
    /// strength ratio to continue than to begin). This generalizes it to the
    /// approach phase: starting an attack and sticking with one use different
    /// thresholds, and a fresh commitment is protected for a minimum time.
    /// </summary>
    public static class EngagementHysteresis
    {
        /// <summary>Ratio required to commit to a new target (mirrors the vanilla siege gate).</summary>
        public const float DefaultEngageRatio = 2.0f;

        /// <summary>Ratio below which an existing commitment is finally abandoned.</summary>
        public const float DefaultAbandonRatio = 1.4f;

        /// <summary>A fresh commitment is not reconsidered for this long.</summary>
        public const float DefaultMinCommitmentHours = 12f;

        /// <summary>How long a remembered assessment stays usable.</summary>
        public const float DefaultRetentionDecayHours = 24f;

        /// <summary>
        /// Ratio below which a commitment is dropped even while it is still fresh.
        /// The commitment window exists to ignore defenders flickering in and out -
        /// not to march a shattered party to its death.
        /// </summary>
        public const float DefaultCollapseRatio = 0.5f;

        /// <summary>
        /// Extra weight for the target a lord is already heading for. Modest on
        /// purpose: enough that a briefly better-looking alternative does not pull
        /// him off course, not so much that he ignores a genuinely better one.
        /// </summary>
        public const float DefaultPursuitStickiness = 1.35f;

        /// <summary>
        /// Schmitt trigger: an uncommitted party needs <paramref name="engageRatio"/>
        /// to start, while a committed one only needs <paramref name="abandonRatio"/>
        /// to carry on. The gap between the two is the hysteresis band in which a
        /// party simply keeps doing what it was already doing.
        /// </summary>
        /// <param name="strengthRatio">Our strength over the target's defenders.</param>
        /// <param name="committed">Whether this party already targets this settlement.</param>
        public static bool ShouldPursue(float strengthRatio, bool committed, float engageRatio, float abandonRatio)
        {
            // A sane band only: never let "continue" demand more than "start".
            float continueRatio = Math.Min(abandonRatio, engageRatio);
            return committed ? strengthRatio >= continueRatio : strengthRatio >= engageRatio;
        }

        /// <summary>
        /// Whether a commitment is still inside its protected window, during which
        /// it is not reconsidered at all. Models a lord who has already marched out
        /// and will not turn around over a momentary change.
        /// </summary>
        public static bool IsWithinCommitmentWindow(float hoursSinceCommitted, float minCommitmentHours)
        {
            if (minCommitmentHours <= 0f)
            {
                return false;
            }
            return hoursSinceCommitted >= 0f && hoursSinceCommitted < minCommitmentHours;
        }

        /// <summary>
        /// How much of a remembered assessment still counts after some hours, decaying
        /// linearly to zero. Lets a party act on what it last saw instead of on
        /// perfect instantaneous knowledge - which is both steadier and more plausible.
        /// </summary>
        public static float RetentionFactor(float hoursSinceSeen, float decayHours)
        {
            if (decayHours <= 0f)
            {
                return 0f;
            }
            if (hoursSinceSeen <= 0f)
            {
                return 1f;
            }
            if (hoursSinceSeen >= decayHours)
            {
                return 0f;
            }
            return 1f - hoursSinceSeen / decayHours;
        }

        /// <summary>
        /// Which ratio a committed party must still clear. A fresh commitment is
        /// only broken by an outright collapse; once the window has elapsed the
        /// normal abandon threshold applies again.
        /// </summary>
        public static float ThresholdForCommitment(bool isFresh, float abandonRatio, float collapseRatio)
        {
            return isFresh ? collapseRatio : abandonRatio;
        }

        /// <summary>
        /// How much a remembered rating is worth given how the odds have moved since.
        /// A held target must not outrank freshly rated ones on stale optimism, so
        /// this only ever scales the remembered score down.
        /// </summary>
        public static float OddsFactor(float currentRatio, float engageRatio)
        {
            if (engageRatio <= 0f)
            {
                return 1f;
            }
            if (currentRatio <= 0f)
            {
                return 0f;
            }
            float factor = currentRatio / engageRatio;
            return factor > 1f ? 1f : factor;
        }

        /// <summary>
        /// Strength ratio used by the hysteresis check, with the defender estimate
        /// floored so a momentarily empty settlement cannot produce an absurd ratio.
        /// </summary>
        public static float StrengthRatio(float ourStrength, float defenderStrength)
        {
            float defenders = Math.Max(defenderStrength, TargetWeights.MinDefenderStrength);
            return ourStrength <= 0f ? 0f : ourStrength / defenders;
        }
    }
}
