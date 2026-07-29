using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free reasoning about troops that are already busy.
    ///
    /// Vanilla rates a target by who is near it, and treats every nearby force as
    /// though it were free to fight. A relieving army pinned outside a castle of
    /// its own, or locked in a battle two provinces away, counts the same as one
    /// sitting idle at the gate. So a realm whose whole host is committed elsewhere
    /// looks exactly as well defended as one holding everything in reserve - and
    /// the moment when it is genuinely vulnerable passes unnoticed.
    ///
    /// Judging that moment is most of what makes a campaign feel deliberate rather
    /// than opportunistic: a war is won by striking where the enemy cannot answer.
    /// </summary>
    public static class ForceCommitment
    {
        /// <summary>Most a distracted enemy may raise a target's appeal.</summary>
        public const float DefaultExposureBonus = 0.6f;

        /// <summary>Below this share committed, a realm is not meaningfully distracted.</summary>
        public const float DefaultDistractionOnset = 0.3f;

        /// <summary>
        /// Share of a realm's strength that is tied up and cannot answer a new
        /// threat: besieging, defending a siege, or fighting a battle.
        /// </summary>
        public static float DistractionRatio(float totalStrength, float tiedDownStrength)
        {
            if (totalStrength <= 0f || tiedDownStrength <= 0f)
            {
                return 0f;
            }
            float ratio = tiedDownStrength / totalStrength;
            return ratio > 1f ? 1f : ratio;
        }

        /// <summary>
        /// How much more inviting a target is because its owner's forces are
        /// occupied elsewhere.
        ///
        /// Nothing below the onset: every realm has some troops busy at any moment,
        /// and treating that as opportunity would just raise every score equally.
        /// It is the unusual concentration - a realm that has thrown most of its
        /// strength at one siege - that leaves an opening worth taking.
        /// </summary>
        public static float ExposureBonus(float distractionRatio, float onset, float maxBonus)
        {
            if (distractionRatio <= onset || onset >= 1f)
            {
                return 1f;
            }

            float beyond = (distractionRatio - onset) / (1f - onset);
            if (beyond > 1f)
            {
                beyond = 1f;
            }
            return 1f + Math.Max(0f, maxBonus) * beyond;
        }

        /// <summary>
        /// Whether a force could actually come to a settlement's aid. Being nearby
        /// is not enough - a party already in a battle or holding a siege line is
        /// not going anywhere, however close it happens to be.
        /// </summary>
        public static bool CanIntervene(bool isEngaged, bool isBesieging, float distance, float reach)
        {
            if (isEngaged || isBesieging)
            {
                return false;
            }
            return reach > 0f && distance <= reach;
        }
    }
}
