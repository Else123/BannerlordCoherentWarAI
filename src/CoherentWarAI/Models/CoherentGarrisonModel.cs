using System.Collections.Generic;
using CoherentWarAI.Behaviors;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

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
    public class CoherentGarrisonModel : SettlementGarrisonModel
    {
        // Previously registered model of this type; protected member inherited via
        // MBGameModel<T>, accessed unqualified (see CoherentTargetScoreModel).
        private SettlementGarrisonModel Base => BaseModel;

        public string BaseModelName => Base?.GetType().Name;

        public override int FindNumberOfTroopsToLeaveToGarrison(MobileParty mobileParty, Settlement settlement)
        {
            int vanilla = Base.FindNumberOfTroopsToLeaveToGarrison(mobileParty, settlement);
            if (vanilla <= 0)
            {
                return vanilla;
            }

            float multiplier = GetMultiplier(settlement, mobileParty);
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
            int vanilla = Base.FindNumberOfTroopsToTakeFromGarrison(mobileParty, settlement, defaultIdealGarrisonStrengthPerWalledCenter);
            if (vanilla <= 0)
            {
                return vanilla;
            }

            // Inverse: the more exposed the settlement, the less a passing lord may
            // strip from its garrison.
            float multiplier = GetMultiplier(settlement, mobileParty);
            return multiplier <= 0f ? vanilla : GarrisonPlanner.ScaleTroopCount(vanilla, 1f / multiplier);
        }

        public override int GetMaximumDailyAutoRecruitmentCount(Town town)
        {
            int vanilla = Base.GetMaximumDailyAutoRecruitmentCount(town);

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
        /// <param name="requester">
        /// The party deciding about this garrison, if any. Neither it nor its army
        /// counts as a friendly defender - otherwise a lord would talk the threat
        /// down with his own presence right before marching off with the troops.
        /// </param>
        public static float GetMultiplier(Settlement settlement, MobileParty requester = null)
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
                EstimateActiveThreat(settlement, settings, requester),
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
        /// Current military pressure on a settlement: hostile lord strength nearby
        /// relative to its defenders (see <see cref="GarrisonPlanner.ActiveThreat"/>).
        /// An active siege counts as maximum pressure.
        ///
        /// Searches with vanilla's own locator and radius - the same query vanilla
        /// runs every three hours per settlement for its threat intensity - around the
        /// gate for land parties and, for ports, around the port for parties at sea:
        /// War Sails fleets can land at or blockade a coastal fief.
        /// </summary>
        private static float EstimateActiveThreat(Settlement settlement, CoherentWarAISettings settings, MobileParty requester)
        {
            if (settlement.IsUnderSiege)
            {
                return settings.GarrisonThreatCap;
            }

            MobilePartyAIModel aiModel = Campaign.Current?.Models?.MobilePartyAIModel;
            if (aiModel == null)
            {
                return 0f;
            }

            // Reused between calls to keep the hourly AI path allocation-free. Safe only
            // because the PartialHourlyAi tickers (which reach this model through
            // FindNumberOfTroopsToTakeFromGarrison) run with doParallel=false in v1.4.8
            // (CampaignPeriodicEventManager); revisit if that changes.
            NearbyForces.Clear();
            CollectNearbyForces(settlement, settlement.GatePosition.ToVec2(),
                aiModel.GetSettlementNearbyThreatAndAllyCheckRadius(settlement, false), false, requester, NearbyForces);
            if (settlement.HasPort)
            {
                CollectNearbyForces(settlement, settlement.PortPosition.ToVec2(),
                    aiModel.GetSettlementNearbyThreatAndAllyCheckRadius(settlement, true), true, requester, NearbyForces);
            }

            return GarrisonPlanner.ActiveThreat(NearbyForces, DefenderStrength(settlement),
                settings.AllyWeight, settings.GarrisonThreatCap);
        }

        private static readonly List<NearbyForce> NearbyForces = new List<NearbyForce>();

        /// <summary>
        /// Raw facts about the parties around one search centre. Land and sea are
        /// searched separately (as vanilla does), so each party is reported once.
        /// Classification (lords only, armies once) is left to the planner.
        /// </summary>
        private static void CollectNearbyForces(Settlement settlement, Vec2 center, float radius, bool atSea,
            MobileParty requester, List<NearbyForce> forces)
        {
            Army requesterArmy = requester?.Army;

            IFaction owner = settlement.MapFaction;
            if (owner == null || radius <= 0f)
            {
                return;
            }

            LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(center, radius);
            for (MobileParty party = MobileParty.FindNextLocatable(ref data); party != null; party = MobileParty.FindNextLocatable(ref data))
            {
                if (!party.IsActive || party.IsGarrison || party.IsMilitia || party.Party == null
                    || party.IsCurrentlyAtSea != atSea || party.MapFaction == null)
                {
                    continue;
                }

                // Allied kingdoms (Kingdom.IsAllyWith) are neither owner nor at war, so
                // their lords count as neutral - mirroring vanilla's ally intensity,
                // which also only adds parties of the settlement's own map faction.
                ForceStance stance = party.MapFaction == owner
                    ? ForceStance.Friendly
                    : (FactionManager.IsAtWarAgainstFaction(party.MapFaction, owner) ? ForceStance.Hostile : ForceStance.Neutral);
                if (stance == ForceStance.Neutral)
                {
                    continue;
                }
                // An enemy resting inside another settlement is not marching on this one
                // (vanilla's rule too); a friendly lord inside still helps defend. A
                // disbanding or leaderless enemy party is going home, not attacking.
                if (stance == ForceStance.Hostile
                    && (party.CurrentSettlement != null || party.IsDisbanding || party.LeaderHero == null))
                {
                    continue;
                }

                Army army = party.Army;
                bool leadsArmy = army != null && army.LeaderParty == party;
                forces.Add(new NearbyForce(
                    leadsArmy ? army.EstimatedStrength : party.Party.EstimatedStrength,
                    party.Position.Distance(center),
                    radius,
                    party.IsLordParty,
                    !leadsArmy && party.AttachedTo != null,
                    stance,
                    party == requester || (requesterArmy != null && army == requesterArmy)));
            }
        }

        /// <summary>The settlement's own defenders: garrison plus militia.</summary>
        private static float DefenderStrength(Settlement settlement)
        {
            float garrison = settlement.Town?.GarrisonParty?.Party?.EstimatedStrength ?? 0f;
            float militia = settlement.MilitiaPartyComponent?.MobileParty?.Party?.EstimatedStrength ?? 0f;
            return garrison + militia;
        }

        // Forwarded to the previously registered model - no behaviour of our own.
        public override ExplainedNumber CalculateBaseGarrisonChange(Settlement settlement, bool includeDescriptions = false) => Base.CalculateBaseGarrisonChange(settlement, includeDescriptions);
        public override float GetMaximumDailyRepairAmount(Settlement settlement) => Base.GetMaximumDailyRepairAmount(settlement);
    }
}
