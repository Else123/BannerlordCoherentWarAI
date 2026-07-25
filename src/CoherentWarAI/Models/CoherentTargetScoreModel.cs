using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Models
{
    /// <summary>
    /// Slice A - de-greeds vanilla target selection. Subclasses the vanilla model
    /// and post-multiplies its score by two neutral-by-default factors (overkill
    /// damping and front coherence). We never decompose or reimplement the vanilla
    /// scoring, so all vanilla naval/siege/value logic (and its hard zero-gates)
    /// stays intact - if base returns 0 we return 0.
    ///
    /// Only offensive missions (siege/raid) are adjusted; defensive scoring is left
    /// to vanilla.
    /// </summary>
    public class CoherentTargetScoreModel : DefaultTargetScoreCalculatingModel
    {
        public override float GetTargetScoreForFaction(Settlement targetSettlement, Army.ArmyTypes missionType, MobileParty mobileParty, float ourStrength)
        {
            float baseScore = base.GetTargetScoreForFaction(targetSettlement, missionType, mobileParty, ourStrength);

            if (baseScore <= 0f)
            {
                return baseScore;
            }

            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableTargetDeGreed)
            {
                return baseScore;
            }

            // Only de-greed offensive target picking; leave defense to vanilla.
            if (missionType != Army.ArmyTypes.Besieger && missionType != Army.ArmyTypes.Raider)
            {
                return baseScore;
            }

            float defenderStrength = EstimateDefenderStrength(targetSettlement);
            float wOverkill = TargetWeights.Overkill(ourStrength, defenderStrength, settings.OverkillOnset, settings.OverkillMinFactor, settings.OverkillSpan);

            CountFrontNeighbors(targetSettlement, mobileParty, out int ownedByUs, out int notOwnedByTarget);
            float wFront = TargetWeights.FrontCoherence(ownedByUs, notOwnedByTarget, settings.FrontFloor, settings.FrontGain);

            return baseScore * wOverkill * wFront;
        }

        /// <summary>
        /// Local defender strength estimate (garrison + militia + aggressive lord
        /// parties present at the settlement), mirroring how vanilla sizes a target's
        /// defenders. Used to decide when extra attacker strength is pure overkill.
        /// </summary>
        private static float EstimateDefenderStrength(Settlement targetSettlement)
        {
            float total = 0f;
            IFaction defenderFaction = targetSettlement.MapFaction;
            foreach (MobileParty party in targetSettlement.Parties)
            {
                if (party?.Party == null)
                {
                    continue;
                }

                // Garrison and militia are settlement-bound defenders. Any other
                // party present only counts if it actually belongs to the defending
                // faction - not allied escorts, passing caravans, or third-faction
                // parties that merely happen to be here. (0.01 mirrors vanilla's
                // aggressiveness cutoff for "is this a fighting party".)
                bool isDefender = party.IsGarrison
                    || party.IsMilitia
                    || (party.Aggressiveness > 0.01f && party.MapFaction == defenderFaction);
                if (isDefender)
                {
                    total += party.Party.EstimatedStrength;
                }
            }
            return total;
        }

        /// <summary>
        /// Counts the target's neighbouring fortifications not owned by the target's
        /// faction (the contested front) and how many of those we own, so a fief on
        /// our own front out-scores a distant soft target.
        /// </summary>
        private static void CountFrontNeighbors(Settlement targetSettlement, MobileParty mobileParty, out int ownedByUs, out int notOwnedByTarget)
        {
            ownedByUs = 0;
            notOwnedByTarget = 0;

            Town town = targetSettlement.IsVillage
                ? targetSettlement.Village?.Bound?.Town
                : targetSettlement.Town;
            if (town == null)
            {
                return;
            }

            IFaction targetFaction = targetSettlement.MapFaction;
            IFaction ourFaction = mobileParty.MapFaction;

            foreach (Settlement neighbor in town.GetNeighborFortifications(MobileParty.NavigationType.All))
            {
                if (neighbor.MapFaction != targetFaction)
                {
                    notOwnedByTarget++;
                    if (neighbor.MapFaction == ourFaction)
                    {
                        ownedByUs++;
                    }
                }
            }
        }
    }
}
