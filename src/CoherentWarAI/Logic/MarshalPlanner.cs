using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free marshal doctrine: offensives are led, not improvised.
    ///
    /// Vanilla lets every lord decide independently whether to raise an army, so a
    /// large realm sends out a scatter of small parties that get beaten one at a
    /// time. Observed in a real campaign: one empire fielding fifty parties put
    /// seventeen of them on the attack individually, the strongest barely a third
    /// the strength of a neighbouring kingdom's lords.
    ///
    /// Here a realm appoints a small number of marshals - only they raise armies,
    /// and the other lords released for offence become the men they call on.
    /// </summary>
    public static class MarshalPlanner
    {
        /// <summary>Offensive slots each marshal is expected to absorb.</summary>
        public const int DefaultSlotsPerMarshal = 6;

        /// <summary>Upper bound on simultaneous offensives per realm.</summary>
        public const int DefaultMaxMarshals = 3;

        /// <summary>How much a ruler outranks an ordinary lord for the post.</summary>
        public const float DefaultRulerBonus = 1.5f;

        /// <summary>
        /// How many offensives a realm should run at once. Scales with how many
        /// lords were released to attack, but stays small: several converging
        /// campaigns is the coherence problem, not the solution.
        /// </summary>
        public static int MarshalCount(int aggressiveSlots, int slotsPerMarshal, int maxMarshals)
        {
            if (aggressiveSlots <= 0)
            {
                return 0;
            }
            // Round up: any offensive at all needs someone to lead it.
            int count = slotsPerMarshal <= 0
                ? Math.Max(0, maxMarshals)
                : (aggressiveSlots + slotsPerMarshal - 1) / slotsPerMarshal;

            int cap = Math.Max(0, maxMarshals);
            if (count > cap)
            {
                count = cap;
            }
            // Never appoint more marshals than there are lords to lead.
            return count > aggressiveSlots ? aggressiveSlots : count;
        }

        /// <summary>
        /// Fitness for the post. Strength decides mostly - a marshal has to be worth
        /// following - nudged by Valor and by rank, since a ruler leading the host
        /// is both the historical norm and what a player expects to see.
        /// </summary>
        public static float MarshalSuitability(float partyStrength, int valorTraitLevel, bool isRuler, float valorWeight, float rulerBonus)
        {
            float score = AggressionScoreFor(partyStrength, valorTraitLevel, valorWeight);
            if (isRuler)
            {
                score *= Math.Max(0f, rulerBonus);
            }
            return score;
        }

        /// <summary>
        /// Whether a lord may raise an army: marshals may, everyone else falls in
        /// behind one or holds their ground.
        /// </summary>
        public static bool MayRaiseArmy(bool isMarshal, bool doctrineEnabled)
        {
            return !doctrineEnabled || isMarshal;
        }

        private static float AggressionScoreFor(float partyStrength, int valorTraitLevel, float valorWeight)
        {
            return PosturePlanner.AggressionScore(partyStrength, valorTraitLevel, valorWeight);
        }
    }
}
