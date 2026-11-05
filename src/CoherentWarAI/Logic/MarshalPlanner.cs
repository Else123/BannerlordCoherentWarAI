using System;
using System.Collections.Generic;

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

        /// <summary>Default weight for <see cref="CommandBonus"/>.</summary>
        public const float DefaultCommandWeight = 0.5f;

        /// <summary>Combined Leadership/Tactics blend at which command competence saturates.</summary>
        public const float AccomplishedCommandSkill = 250f;

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

        /// <summary>
        /// Whether the next lord in the post ranking becomes marshal. A lord already
        /// leading a host always does, even past <paramref name="marshalCount"/>:
        /// gathering an army takes longer than a day, and the mod never disbands one,
        /// so a host leader outside the post would be a second offensive the doctrine
        /// does not count (T-0115). Ranked first, host leaders fill the posts before
        /// anyone new is appointed.
        /// </summary>
        public static bool IsAppointed(bool leadsHost, int appointedSoFar, int marshalCount)
        {
            return leadsHost || appointedSoFar < marshalCount;
        }

        /// <summary>
        /// Sorts the day's <paramref name="attackers"/> for the marshal posts - host
        /// leaders first, then by <paramref name="fitness"/> (see
        /// <see cref="PosturePlanner.CompareForOffense"/>) - and returns how many of
        /// them, counted from the front, are appointed (<see cref="IsAppointed"/>).
        /// Because host leaders rank first, the marshals are always a prefix of the
        /// sorted list.
        /// </summary>
        public static int RankForPosts<T>(List<T> attackers, Func<T, bool> leadsHost, Func<T, float> fitness, int marshalCount)
        {
            attackers.Sort((a, b) => PosturePlanner.CompareForOffense(leadsHost(a), fitness(a), leadsHost(b), fitness(b)));

            int appointed = 0;
            while (appointed < attackers.Count && IsAppointed(leadsHost(attackers[appointed]), appointed, marshalCount))
            {
                appointed++;
            }
            return appointed;
        }

        private static float AggressionScoreFor(float partyStrength, int valorTraitLevel, float valorWeight)
        {
            return PosturePlanner.AggressionScore(partyStrength, valorTraitLevel, valorWeight);
        }

        /// <summary>
        /// How good a commander this hero is, 0..1, from Leadership and Tactics.
        /// Leadership leads the blend: vanilla's own <c>ArmyCommander</c> perks tie
        /// Leadership to the cost and reach of calling an army together, and Tactics
        /// to holding it once raised - authority is more Leadership than Tactics.
        /// Same saturating-square-root shape as <see cref="ScoutingQuality.Competence"/>:
        /// early skill buys a lot, later skill less.
        /// </summary>
        public static float CommandCompetence(float leadership, float tactics)
        {
            float blend = (0.6f * leadership + 0.4f * tactics) / AccomplishedCommandSkill;
            float clamped = blend < 0f ? 0f : (blend > 1f ? 1f : blend);
            return (float)Math.Sqrt(clamped);
        }

        /// <summary>
        /// Multiplier applied to <see cref="MarshalSuitability"/> for how competent a
        /// commander this candidate is. No-op shape: at <paramref name="weight"/> == 0
        /// this always returns 1, so a disabled setting never changes who is appointed.
        /// </summary>
        public static float CommandBonus(float competence, float weight)
        {
            float clamped = competence < 0f ? 0f : (competence > 1f ? 1f : competence);
            return 1f + Math.Max(0f, weight) * clamped;
        }

        /// <summary>Floor of <see cref="OrderLifetimeHours"/>'s blend - the lifetime at zero competence, full weight.</summary>
        public const float MinOrderLifetimeHours = 24f;

        /// <summary>Ceiling of <see cref="OrderLifetimeHours"/>'s blend - the lifetime at full competence, full weight.</summary>
        public const float MaxOrderLifetimeHours = 96f;

        /// <summary>Slice 1's shipped, fixed order lifetime - what <see cref="OrderLifetimeHours"/> returns at weight 0.</summary>
        public const float DefaultOrderLifetimeHours = 48f;

        /// <summary>
        /// How long a freshly issued order stays valid, blended from how competent its
        /// issuing commander is. No-op shape: at <paramref name="weight"/> == 0 this
        /// always returns <see cref="DefaultOrderLifetimeHours"/> (48h) for every
        /// <paramref name="competence"/> - Slice 1's shipped constant, byte-identical,
        /// not merely close to it. As <paramref name="weight"/> rises the blend leans
        /// toward a competence-scaled span from <see cref="MinOrderLifetimeHours"/> (24h,
        /// zero competence) to <see cref="MaxOrderLifetimeHours"/> (96h, full competence),
        /// reaching that full spread at <paramref name="weight"/> == 1. The blend
        /// saturates there: the MCM range for the weight is 0..2, and anything at or
        /// above 1 behaves exactly like 1. Both inputs are clamped to [0,1] before use.
        /// </summary>
        public static float OrderLifetimeHours(float competence, float weight)
        {
            float c = competence < 0f ? 0f : (competence > 1f ? 1f : competence);
            float w = weight < 0f ? 0f : (weight > 1f ? 1f : weight);
            return (1f - w) * DefaultOrderLifetimeHours + w * (MinOrderLifetimeHours + (MaxOrderLifetimeHours - MinOrderLifetimeHours) * c);
        }
    }
}
