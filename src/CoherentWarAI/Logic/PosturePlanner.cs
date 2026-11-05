using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Order for the offensive ranking: lords leading a host first, then by score
        /// (descending). Negative when the first lord ranks ahead of the other. Used
        /// for the posture slots (by <see cref="AggressionScore"/>) and the marshal
        /// posts (by <see cref="MarshalPlanner.MarshalSuitability"/>) alike.
        /// Host leaders only move ahead within the existing allowance - the slot
        /// count itself never grows for them (T-0115): on a "0 may attack" day a
        /// host keeps going in Defensive posture, so a relief army keeps its
        /// defender mission instead of being turned to the offensive.
        /// </summary>
        public static int CompareForOffense(bool leadsHost, float score, bool otherLeadsHost, float otherScore)
        {
            if (leadsHost != otherLeadsHost)
            {
                return leadsHost ? -1 : 1;
            }
            return otherScore.CompareTo(score);
        }

        /// <summary>
        /// Whether a lord gets the host-first priority for the offensive slots: he
        /// leads a host that is not a defender army. A defender (relief) host is no
        /// offensive the doctrine has to count, and moving it into a slot would turn
        /// it Aggressive - vanilla then stops scoring Defender missions for it and
        /// the relief is abandoned (T-0115). It still competes by score as before.
        /// </summary>
        public static bool TakesSlotFirst(bool leadsHost, bool hostIsDefender)
        {
            return leadsHost && !hostIsDefender;
        }

        /// <summary>
        /// Sorts <paramref name="parties"/> into offensive rank order by
        /// <see cref="CompareForOffense"/>, swapping <paramref name="scores"/> (the
        /// parallel list of their <see cref="AggressionScore"/>s) along. Rank 0 is the
        /// first offensive slot (<see cref="DecidePosture"/>). Selection sort keeps it
        /// allocation-free on a list that is at most a few dozen entries.
        /// </summary>
        public static void RankForOffense<T>(IList<T> parties, IList<float> scores, Func<T, bool> leadsHost)
        {
            for (int rank = 0; rank < parties.Count; rank++)
            {
                int best = rank;
                for (int i = rank + 1; i < parties.Count; i++)
                {
                    if (CompareForOffense(leadsHost(parties[i]), scores[i], leadsHost(parties[best]), scores[best]) < 0)
                    {
                        best = i;
                    }
                }
                if (best == rank)
                {
                    continue;
                }

                T swapParty = parties[best];
                parties[best] = parties[rank];
                parties[rank] = swapParty;

                float swapScore = scores[best];
                scores[best] = scores[rank];
                scores[rank] = swapScore;
            }
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
