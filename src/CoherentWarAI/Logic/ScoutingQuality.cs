using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free rules for what a party's scout is worth.
    ///
    /// Vanilla applies the Scouting skill to the player's own sight range and
    /// tracking, but the AI reads the world directly, so an army led by a gifted
    /// scout notices no more than one led by none at all. With reports now
    /// mattering, who is doing the watching should matter too: a good scout sees
    /// further and brings back something worth acting on, a poor one rides past an
    /// army two valleys over without a word.
    /// </summary>
    public static class ScoutingQuality
    {
        /// <summary>Skill at which a scout is considered thoroughly accomplished.</summary>
        public const float AccomplishedSkill = 250f;

        /// <summary>How much further an accomplished scout sees than an unskilled one.</summary>
        public const float DefaultReachBonus = 1f;

        /// <summary>Weight a report from an unskilled observer carries.</summary>
        public const float DefaultMinimumConfidence = 0.5f;

        /// <summary>
        /// How far this party notices things, as a multiple of the base radius.
        /// Ranges from 1 for someone with no eye for it up to 1 + reachBonus for an
        /// accomplished scout.
        /// </summary>
        public static float ReachMultiplier(float scoutingSkill, float reachBonus)
        {
            return 1f + Math.Max(0f, reachBonus) * Competence(scoutingSkill);
        }

        /// <summary>
        /// How much a report from this party is worth, 0..1.
        ///
        /// Never zero: even an indifferent observer riding into an army will say
        /// something. But an unskilled one misjudges numbers and direction, so what
        /// he brings back should not carry the weight of a proper reconnaissance.
        /// </summary>
        public static float Confidence(float scoutingSkill, float minimumConfidence)
        {
            float floor = minimumConfidence < 0f ? 0f : (minimumConfidence > 1f ? 1f : minimumConfidence);
            return floor + (1f - floor) * Competence(scoutingSkill);
        }

        /// <summary>
        /// How accomplished a scout this is, 0..1. Rises quickly at first - the
        /// difference between no scout and a passable one is larger than between a
        /// good one and a great one.
        /// </summary>
        public static float Competence(float scoutingSkill)
        {
            if (scoutingSkill <= 0f)
            {
                return 0f;
            }
            if (scoutingSkill >= AccomplishedSkill)
            {
                return 1f;
            }

            // Square root: early skill buys a lot of capability, later skill less.
            return (float)Math.Sqrt(scoutingSkill / AccomplishedSkill);
        }
    }
}
