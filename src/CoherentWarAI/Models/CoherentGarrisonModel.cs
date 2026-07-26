using CoherentWarAI.Behaviors;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Models
{
    /// <summary>
    /// Makes garrison strength depend on where a settlement actually sits.
    ///
    /// Vanilla sizes garrisons from economics alone, so the fief the enemy marches
    /// through is defended no better than one deep inside the realm, and the
    /// one-troop-a-day recruitment cap cannot refill it between raids. This scales
    /// vanilla's own numbers by exposure and chokepoint value: gateways are held
    /// hardest, quiet interior fiefs give troops back to the field army.
    ///
    /// Vanilla's results (including its minimum-garrison floors) are scaled rather
    /// than reimplemented, so this stays robust across game updates.
    /// </summary>
    public class CoherentGarrisonModel : DefaultSettlementGarrisonModel
    {
        public override int FindNumberOfTroopsToLeaveToGarrison(MobileParty mobileParty, Settlement settlement)
        {
            int vanilla = base.FindNumberOfTroopsToLeaveToGarrison(mobileParty, settlement);
            if (vanilla <= 0)
            {
                return vanilla;
            }

            float multiplier = GetMultiplier(settlement);
            if (multiplier <= 0f)
            {
                return vanilla;
            }

            int scaled = GarrisonPlanner.ScaleTroopCount(vanilla, multiplier);

            // Defense in depth: whatever vanilla would have left behind is an upper
            // bound we may raise, but never past what this party can actually spare.
            // A lord must not garrison himself out of existence.
            int spareable = mobileParty?.Party != null ? mobileParty.Party.NumberOfRegularMembers - 1 : scaled;
            if (scaled > spareable)
            {
                scaled = spareable;
            }
            return scaled < vanilla ? vanilla : scaled;
        }

        public override int FindNumberOfTroopsToTakeFromGarrison(MobileParty mobileParty, Settlement settlement, float defaultIdealGarrisonStrengthPerWalledCenter = 0f)
        {
            int vanilla = base.FindNumberOfTroopsToTakeFromGarrison(mobileParty, settlement, defaultIdealGarrisonStrengthPerWalledCenter);
            if (vanilla <= 0)
            {
                return vanilla;
            }

            // Inverse: the more exposed the settlement, the less a passing lord may
            // strip from its garrison.
            float multiplier = GetMultiplier(settlement);
            return multiplier <= 0f ? vanilla : GarrisonPlanner.ScaleTroopCount(vanilla, 1f / multiplier);
        }

        public override int GetMaximumDailyAutoRecruitmentCount(Town town)
        {
            int vanilla = base.GetMaximumDailyAutoRecruitmentCount(town);

            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableGarrisonThreatAwareness || town?.Settlement == null)
            {
                return vanilla;
            }

            float multiplier = GetMultiplier(town.Settlement);
            return GarrisonPlanner.RecruitmentCap(multiplier, vanilla, settings.RecruitCapMax);
        }

        /// <summary>
        /// Combined exposure and chokepoint multiplier for a settlement. Returns 1
        /// (no change) when the feature is off or the settlement has no topology to
        /// reason about.
        /// </summary>
        public static float GetMultiplier(Settlement settlement)
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableGarrisonThreatAwareness)
            {
                return 1f;
            }
            if (settlement?.Town == null || !settlement.IsFortification)
            {
                return 1f;
            }

            IFaction owner = settlement.MapFaction;
            CountNeighbors(settlement, owner, out int foreignNeighbors, out int friendlyNeighbors, out int hostileNeighbors);

            // Deliberately front-local: a kingdom is almost always at war with
            // *somebody*, so asking "is my realm at war" would keep every border in
            // the country permanently inflated. Only a border facing an actual enemy
            // escapes the peacetime cap; a quiet frontier with a neutral neighbour
            // stays modestly reinforced instead.
            bool contested = hostileNeighbors > 0;

            float threatFactor = GarrisonPlanner.ThreatFactor(
                foreignNeighbors > 0,
                EstimateActiveThreat(settlement, settings),
                contested,
                settings.InteriorBase,
                settings.BorderBase,
                settings.GarrisonThreatGain,
                settings.GarrisonThreatCap,
                settings.PeaceCap);

            // How much of the realm sits behind this settlement, from the route
            // analysis - a gate with no way around it outranks a fief that merely
            // happens to sit near the border.
            //
            // Only fall back to counting neighbours when the routes have not been
            // worked out at all. A computed score of zero is a real answer - "the
            // enemy can simply march around this one" - and must not be overridden
            // by the cruder heuristic, which is exactly what this feature exists to
            // correct.
            float chokepoint = ChokepointMapBehavior.HasComputedScores
                ? ChokepointMapBehavior.GetGatewayScore(settlement)
                : GarrisonPlanner.ChokepointScore(foreignNeighbors, friendlyNeighbors, settings.ChokepointSaturation);

            return GarrisonPlanner.GarrisonMultiplier(threatFactor, chokepoint, settings.ChokepointGain);
        }

        /// <summary>
        /// Splits the neighbouring fortifications into those held by someone else
        /// and those held by us. A settlement that both faces foreign ground and
        /// covers friendly ground behind it is a gateway into the realm.
        ///
        /// Ownership, not the current war state, defines the topology: a border is a
        /// border in peacetime too, which is what lets garrisons be ready before a
        /// war rather than after it starts.
        /// </summary>
        private static void CountNeighbors(Settlement settlement, IFaction owner,
            out int foreignNeighbors, out int friendlyNeighbors, out int hostileNeighbors)
        {
            foreignNeighbors = 0;
            friendlyNeighbors = 0;
            hostileNeighbors = 0;

            foreach (Settlement neighbor in SettlementNeighbors.Of(settlement))
            {
                IFaction neighborFaction = neighbor.MapFaction;
                if (neighborFaction == null)
                {
                    continue;
                }
                if (neighborFaction == owner)
                {
                    friendlyNeighbors++;
                    continue;
                }

                foreignNeighbors++;
                if (owner != null && neighborFaction.IsAtWarWith(owner))
                {
                    hostileNeighbors++;
                }
            }
        }

        /// <summary>
        /// Current military pressure on a settlement: hostile intensity nearby, less
        /// whatever friendly force is already covering it. An active siege counts as
        /// maximum pressure.
        /// </summary>
        private static float EstimateActiveThreat(Settlement settlement, CoherentWarAISettings settings)
        {
            if (settlement.IsUnderSiege)
            {
                return settings.GarrisonThreatCap;
            }

            float hostile = settlement.NearbyLandThreatIntensity + settlement.NearbyNavalThreatIntensity;
            float friendly = settings.AllyWeight * settlement.NearbyLandAllyIntensity;

            float net = hostile - friendly;
            return net < 0f ? 0f : net;
        }
    }
}
