using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Models
{
    /// <summary>
    /// Caches which fortifications neighbour which.
    ///
    /// Target scoring runs hundreds of times per game hour per party, and several
    /// of this mod's weights need a settlement's neighbours - so the engine's
    /// neighbour query would otherwise be hit repeatedly for the same settlement
    /// within a single scoring call.
    ///
    /// The list itself is safe to keep: it comes from map distances, so it depends
    /// on the shape of the map rather than on who owns what. Ownership changes
    /// constantly during a war and is read live at each use; the adjacency does not
    /// change at all during a campaign.
    /// </summary>
    internal static class SettlementNeighbors
    {
        private static readonly Dictionary<Settlement, Settlement[]> Cache = new Dictionary<Settlement, Settlement[]>();

        private static readonly Settlement[] Empty = new Settlement[0];

        /// <summary>
        /// Neighbouring fortifications of a settlement - for a village, those of the
        /// town it belongs to. Never null; entries are never null.
        /// </summary>
        public static Settlement[] Of(Settlement settlement)
        {
            if (settlement == null)
            {
                return Empty;
            }

            if (Cache.TryGetValue(settlement, out Settlement[] cached))
            {
                return cached;
            }

            Town town = settlement.IsVillage
                ? settlement.Village?.Bound?.Town
                : settlement.Town;

            Settlement[] neighbors;
            if (town == null)
            {
                neighbors = Empty;
            }
            else
            {
                List<Settlement> collected = new List<Settlement>();
                foreach (Settlement neighbor in town.GetNeighborFortifications(MobileParty.NavigationType.All))
                {
                    // Filtered once here so no caller has to guard again.
                    if (neighbor != null)
                    {
                        collected.Add(neighbor);
                    }
                }
                neighbors = collected.ToArray();
            }

            Cache[settlement] = neighbors;
            return neighbors;
        }

        /// <summary>Drops the cache; called when a campaign is loaded or started.</summary>
        public static void Clear()
        {
            Cache.Clear();
        }
    }
}
