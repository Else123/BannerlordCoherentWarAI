using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free rules for which command domains a realm should delegate at all,
    /// and how many distinct holders it can actually afford.
    ///
    /// The two questions are kept apart on purpose. <see cref="DesiredDomains"/>
    /// answers "does this realm's situation call for a Defense / Offense / Naval
    /// holder" from the realm's own signals alone - it is a desire, not a staffing
    /// plan, and stays true even for a realm with no lord left to hand a domain to.
    /// <see cref="LimitToHolderBudget"/> answers the separate, later question "how
    /// many of those desired domains can this realm's <c>MaxMarshals</c> setting
    /// actually staff", by dropping flags rather than picking them - a deliberately
    /// conservative upper bound on distinct holders (a realm may end up with fewer,
    /// e.g. if the same hero would otherwise hold two domains), never a floor.
    /// </summary>
    public static class DelegationPlanner
    {
        /// <summary>Threat ratio above which Defense is desired even in a single war.</summary>
        public const float DefaultDefenseThreatThreshold = 0.25f;

        /// <summary>
        /// Which domains this realm's situation calls for, evaluated independently
        /// and deterministically from the given inputs only (no <see cref="Random"/>,
        /// no static state - same inputs always yield the same result):
        /// <list type="number">
        /// <item>Offense iff <paramref name="aggressiveSlotCount"/> &gt; 0.</item>
        /// <item>Naval iff <paramref name="ports"/> &gt;= 2 and <paramref name="navalCapableLords"/> &gt;= 1 -
        /// one port is never enough on its own.</item>
        /// <item>Defense iff <paramref name="wars"/> &gt;= 2 or <paramref name="threatRatio"/> is strictly
        /// greater than <see cref="DefaultDefenseThreatThreshold"/> - the boundary itself does not qualify.</item>
        /// </list>
        /// </summary>
        public static Domain DesiredDomains(int aggressiveSlotCount, int wars, float threatRatio, int ports, int navalCapableLords)
        {
            Domain result = Domain.None;
            if (aggressiveSlotCount > 0)
            {
                result |= Domain.Offense;
            }
            if (ports >= 2 && navalCapableLords >= 1)
            {
                result |= Domain.Naval;
            }
            if (wars >= 2 || threatRatio > DefaultDefenseThreatThreshold)
            {
                result |= Domain.Defense;
            }
            return result;
        }

        /// <summary>
        /// Drops flags out of <paramref name="desired"/>, in reverse of the rules'
        /// own priority above - Defense first, then Naval, then Offense - until at
        /// most <paramref name="maxMarshals"/> remain. Never adds a flag that was
        /// not already set in <paramref name="desired"/>. At <paramref name="maxMarshals"/>
        /// &lt;= 0 the result is always <see cref="Domain.None"/>.
        /// </summary>
        public static Domain LimitToHolderBudget(Domain desired, int maxMarshals)
        {
            if (maxMarshals <= 0)
            {
                return Domain.None;
            }

            Domain result = desired;
            if (CountFlags(result) > maxMarshals && (result & Domain.Defense) != 0)
            {
                result &= ~Domain.Defense;
            }
            if (CountFlags(result) > maxMarshals && (result & Domain.Naval) != 0)
            {
                result &= ~Domain.Naval;
            }
            if (CountFlags(result) > maxMarshals && (result & Domain.Offense) != 0)
            {
                result &= ~Domain.Offense;
            }
            return result;
        }

        private static int CountFlags(Domain domain)
        {
            int count = 0;
            if ((domain & Domain.Defense) != 0) { count++; }
            if ((domain & Domain.Offense) != 0) { count++; }
            if ((domain & Domain.Naval) != 0) { count++; }
            return count;
        }
    }
}
