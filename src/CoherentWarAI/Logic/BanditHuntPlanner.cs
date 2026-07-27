using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free rules for hunting bandits.
    ///
    /// Vanilla lords never seek bandits out at all - every AI behaviour that could
    /// pick a target excludes bandit parties outright, so lords only ever fight
    /// them by walking into them. Meanwhile a lord held back for defence often has
    /// nothing to do but patrol.
    ///
    /// Clearing bandits is worth doing: it protects villages and it is how troops
    /// gain experience cheaply. But it must never come at the cost of a real war -
    /// a realm facing an equal or stronger enemy has no lords to spare for
    /// policing.
    /// </summary>
    public static class BanditHuntPlanner
    {
        /// <summary>Above this share of the realm under threat, nobody goes bandit hunting.</summary>
        public const float DefaultMaxThreatRatio = 0.25f;

        /// <summary>How much stronger than its main enemy a realm must be to spare lords.</summary>
        public const float DefaultRequiredSuperiority = 1.3f;

        /// <summary>Strength ratio a hunter needs over its quarry to bother.</summary>
        public const float DefaultRequiredHunterAdvantage = 1.5f;

        /// <summary>Hours after which a hunt is given up as hopeless.</summary>
        public const float DefaultHuntCommitmentHours = 24f;

        /// <summary>
        /// Whether a realm can spare anyone for policing at all.
        ///
        /// Peace, or a war against someone clearly weaker, leaves room for it. An
        /// even fight does not: that is exactly when every lord is needed, and it is
        /// the case the player specifically did not want bandits to distract from.
        /// </summary>
        /// <param name="threatRatio">Share of the realm currently under threat, 0..1.</param>
        /// <param name="ourStrength">Our realm's total strength.</param>
        /// <param name="primaryEnemyStrength">Strength of the enemy we are pressing; 0 if at peace.</param>
        public static bool RealmMaySpareLords(float threatRatio, float ourStrength, float primaryEnemyStrength,
            float maxThreatRatio, float requiredSuperiority)
        {
            if (threatRatio > maxThreatRatio)
            {
                return false;
            }
            if (primaryEnemyStrength <= 0f)
            {
                return true;
            }
            if (ourStrength <= 0f)
            {
                return false;
            }

            return ourStrength / primaryEnemyStrength >= Math.Max(1f, requiredSuperiority);
        }

        /// <summary>
        /// Whether this particular lord is free to go. Anyone released for the
        /// offensive, leading a host, or already engaged is not.
        /// </summary>
        public static bool LordIsAvailable(bool isDefensive, bool leadsOrJoinsArmy, bool isMarshal, bool hasOwnObjective)
        {
            return isDefensive && !leadsOrJoinsArmy && !isMarshal && !hasOwnObjective;
        }

        /// <summary>
        /// How worthwhile a given bandit party is as quarry.
        ///
        /// Zero when it is too strong to take safely - a lord lost to bandits is
        /// worse than bandits left alone. Otherwise larger bands are preferred:
        /// they threaten more and yield more experience, which is half the point.
        /// Result is 0..1.
        /// </summary>
        public static float QuarryValue(float ourStrength, float banditStrength, float requiredAdvantage)
        {
            if (banditStrength <= 0f || ourStrength <= 0f)
            {
                return 0f;
            }

            float advantage = ourStrength / banditStrength;
            if (advantage < Math.Max(1f, requiredAdvantage))
            {
                return 0f;
            }

            // Prefer the biggest band we can still comfortably beat: value rises as
            // our advantage narrows toward the threshold.
            float headroom = advantage / Math.Max(1f, requiredAdvantage);
            return headroom <= 1f ? 1f : 1f / headroom;
        }

        /// <summary>
        /// Whether a hunt begun this long ago is still worth pursuing.
        ///
        /// This is an upper bound on one chase, not a protected window: vanilla's
        /// think loop reverts our order within a few hours because it never scores
        /// bandits as targets, so the order has to be re-issued rather than left
        /// alone. What this prevents is a lord chasing an evasive band forever.
        /// </summary>
        public static bool HuntStillWorthPursuing(float hoursSinceStarted, float giveUpAfterHours)
        {
            if (giveUpAfterHours <= 0f)
            {
                return false;
            }
            return hoursSinceStarted >= 0f && hoursSinceStarted < giveUpAfterHours;
        }
    }
}
