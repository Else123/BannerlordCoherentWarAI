using System;

namespace CoherentWarAI.Logic
{
    /// <summary>The stance a party should adopt. Maps onto the engine's party objective.</summary>
    public enum Posture
    {
        Neutral,
        Defensive,
        Aggressive
    }

    /// <summary>
    /// Engine-free planning for Slice B-def (defense-first posture).
    ///
    /// Vanilla leaves every AI lord's party objective at its default and only
    /// reacts to attacks once they have already started, so nobody holds their own
    /// territory. This decides how many of a kingdom's parties may go on the
    /// offensive at all, and which ones - the rest default to defending.
    ///
    /// Uses only <c>System</c> so it is unit-tested without a game install.
    /// </summary>
    public static class PosturePlanner
    {
        public const float DefaultAggressiveShare = 0.34f;
        public const int DefaultMinimumDefenders = 2;
        public const float DefaultValorWeight = 0.25f;

        /// <summary>
        /// How many of a kingdom's war parties may act offensively.
        ///
        /// The share shrinks as more of the realm is under threat, so a kingdom
        /// being invaded pulls its lords home instead of pressing an offensive.
        /// At least <paramref name="minimumDefenders"/> parties always stay
        /// defensive (unless the kingdom has fewer parties than that in total).
        /// </summary>
        /// <param name="warPartyCount">Total war parties the kingdom fields.</param>
        /// <param name="threatRatio">Share of the realm's fiefs under threat, 0..1.</param>
        /// <param name="aggressiveShare">Base share allowed to attack when unthreatened.</param>
        /// <param name="minimumDefenders">Parties always held back for defense.</param>
        public static int AggressiveSlotCount(int warPartyCount, float threatRatio, float aggressiveShare, int minimumDefenders)
        {
            if (warPartyCount <= 0)
            {
                return 0;
            }

            float clampedThreat = Clamp01(threatRatio);
            float share = Clamp01(aggressiveShare) * (1f - clampedThreat);

            int slots = (int)Math.Round(warPartyCount * share, MidpointRounding.AwayFromZero);

            // The defensive reserve must never freeze a kingdom outright: a small
            // realm would otherwise be permanently unable to attack, and two small
            // kingdoms at war would stalemate forever. Cap the reserve so at least
            // one party can still be spared. Threat scaling above already drives
            // attacks to zero on its own when the realm is actually under pressure.
            int reserve = Math.Min(Math.Max(0, minimumDefenders), warPartyCount - 1);
            int cap = warPartyCount - reserve;

            if (slots > cap)
            {
                slots = cap;
            }
            return slots < 0 ? 0 : slots;
        }

        /// <summary>
        /// Ranking score deciding which parties get the offensive slots. Stronger
        /// parties lead attacks, nudged by the lord's Valor trait so bold lords
        /// push and cautious ones hold - vanilla already uses Valor/Calculating for
        /// temperament elsewhere.
        /// </summary>
        /// <param name="partyStrength">Party strength (any consistent scale).</param>
        /// <param name="valorTraitLevel">Lord's Valor trait, typically -2..2.</param>
        /// <param name="valorWeight">How strongly Valor shifts the ranking.</param>
        public static float AggressionScore(float partyStrength, int valorTraitLevel, float valorWeight)
        {
            if (partyStrength < 0f)
            {
                partyStrength = 0f;
            }
            int clampedValor = valorTraitLevel < -2 ? -2 : (valorTraitLevel > 2 ? 2 : valorTraitLevel);
            // Floor the multiplier: a large valorWeight with negative valor would
            // otherwise invert the score, which only ranks correctly by accident.
            float multiplier = 1f + valorWeight * clampedValor;
            return partyStrength * (multiplier < 0f ? 0f : multiplier);
        }

        /// <summary>
        /// Final posture for one party, given its rank among its kingdom's parties
        /// by <see cref="AggressionScore"/> (0 = most aggressive candidate).
        /// Ranks inside the offensive allowance attack; everyone else defends.
        /// </summary>
        public static Posture DecidePosture(int aggressionRank, int aggressiveSlots)
        {
            if (aggressionRank < 0)
            {
                return Posture.Defensive;
            }
            return aggressionRank < aggressiveSlots ? Posture.Aggressive : Posture.Defensive;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }
            return value > 1f ? 1f : value;
        }
    }
}
