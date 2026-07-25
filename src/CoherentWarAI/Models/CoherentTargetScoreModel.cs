using CoherentWarAI.Behaviors;
using CoherentWarAI.Diagnostics;
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
        private readonly CommitmentStore _commitments = new CommitmentStore();

        public override float GetTargetScoreForFaction(Settlement targetSettlement, Army.ArmyTypes missionType, MobileParty mobileParty, float ourStrength)
        {
            float baseScore = base.GetTargetScoreForFaction(targetSettlement, missionType, mobileParty, ourStrength);

            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null)
            {
                return baseScore;
            }

            // Only touch offensive target picking; leave defense to vanilla.
            bool isOffensive = missionType == Army.ArmyTypes.Besieger || missionType == Army.ArmyTypes.Raider;

            if (baseScore <= 0f)
            {
                // Vanilla just declared this target impossible. That verdict flips
                // the moment defenders change - which is what makes lords dither in
                // front of a castle. Hold a recent commitment instead of dropping it.
                return isOffensive
                    ? HoldCommitmentIfReasonable(targetSettlement, missionType, mobileParty, ourStrength, settings)
                    : baseScore;
            }

            if (!isOffensive)
            {
                return baseScore;
            }

            if (!settings.EnableTargetDeGreed)
            {
                if (settings.EnableCommitmentHysteresis)
                {
                    _commitments.Remember(mobileParty, targetSettlement, missionType, baseScore);
                }
                return baseScore;
            }

            float defenderStrength = EstimateDefenderStrength(targetSettlement);
            float wOverkill = TargetWeights.Overkill(ourStrength, defenderStrength, settings.OverkillOnset, settings.OverkillMinFactor, settings.OverkillSpan);

            CountFrontNeighbors(targetSettlement, mobileParty, out int ownedByUs, out int notOwnedByTarget);
            float wFront = TargetWeights.FrontCoherence(ownedByUs, notOwnedByTarget, settings.FrontFloor, settings.FrontGain);

            // What the rest of our realm is already sending here. Vanilla has no
            // such term at all, which is why every lord picks the same fief.
            //
            // Only applies to lords who would be *arriving*: someone already
            // committed here is not joining a pile, and damping him because an ally
            // turned up too would undercut a siege he is already prosecuting.
            float wCoord = 1f;
            if (settings.EnableCoordination
                && WarCoordinatorBehavior.GetCommittedTarget(mobileParty) != targetSettlement)
            {
                float committed = WarCoordinatorBehavior.GetCommittedStrengthExcluding(targetSettlement, mobileParty);
                float required = ClaimPlanner.RequiredStrength(defenderStrength, settings.RequiredMargin);
                wCoord = ClaimPlanner.SaturationBias(committed, required, settings.SaturationSuppression, settings.NeglectBonus);
            }

            float score = baseScore * wOverkill * wFront * wCoord;

            // Off by default: this runs hundreds of times per game hour.
            if (WarAiLog.VerboseScoring)
            {
                WarAiLog.Write("Score", string.Format(
                    "{0} -> {1} ({2}): vanilla {3:F1} x overkill {4:F2} x front {5:F2} x coord {6:F2} = {7:F1}",
                    mobileParty.LeaderHero?.Name, targetSettlement.Name, missionType,
                    baseScore, wOverkill, wFront, wCoord, score));
            }

            // Remember this assessment while the target is still clearly ratable,
            // so a later dip does not erase what this lord already decided.
            if (settings.EnableCommitmentHysteresis)
            {
                _commitments.Remember(mobileParty, targetSettlement, missionType, score);
            }
            return score;
        }

        /// <summary>
        /// Keeps a lord on a target vanilla has momentarily written off, as long as
        /// the situation has not genuinely deteriorated. Returns 0 (vanilla's own
        /// verdict) when there is no commitment worth holding.
        ///
        /// Two guards stop this from becoming suicide: the remembered rating decays
        /// to nothing over time, and once the commitment is no longer fresh the
        /// strength ratio must still clear the abandon threshold.
        /// </summary>
        private float HoldCommitmentIfReasonable(Settlement targetSettlement, Army.ArmyTypes missionType, MobileParty mobileParty, float ourStrength, CoherentWarAISettings settings)
        {
            if (!settings.EnableCommitmentHysteresis || targetSettlement == null || mobileParty == null)
            {
                return 0f;
            }

            if (!_commitments.TryGet(mobileParty, targetSettlement, missionType,
                    out float lastScore, out float hoursSinceSeen, out float hoursSinceCommitted))
            {
                return 0f;
            }

            float retention = EngagementHysteresis.RetentionFactor(hoursSinceSeen, settings.RetentionDecayHours);
            if (retention <= 0f)
            {
                return 0f;
            }

            // A fresh commitment tolerates defenders flickering in and out, but even
            // then an outright collapse of our own force ends it - the window must
            // never march a shattered party to its death.
            float ratio = EngagementHysteresis.StrengthRatio(ourStrength, EstimateDefenderStrength(targetSettlement));
            bool isFresh = EngagementHysteresis.IsWithinCommitmentWindow(hoursSinceCommitted, settings.MinCommitmentHours);
            float threshold = EngagementHysteresis.ThresholdForCommitment(isFresh, settings.AbandonRatio, settings.CollapseRatio);

            if (!EngagementHysteresis.ShouldPursue(ratio, committed: true, settings.EngageRatio, threshold))
            {
                return 0f;
            }

            // Scale by how the odds actually stand now, so a held target cannot
            // outrank freshly rated ones on a stale, rosier assessment.
            float held = lastScore * retention * EngagementHysteresis.OddsFactor(ratio, settings.EngageRatio);

            // This is the flip-flop being prevented: vanilla just wrote the target
            // off, and we are keeping the lord on it. Worth seeing in the log.
            WarAiLog.Write("Hysteresis", string.Format(
                "{0} holds {1} ({2}) - vanilla would abort; ratio {3:F2}, {4} commitment, score {5:F1}",
                mobileParty.LeaderHero?.Name, targetSettlement.Name, missionType,
                ratio, isFresh ? "fresh" : "matured", held));

            return held;
        }

        /// <summary>
        /// Draws defending lords to the gates of the realm.
        ///
        /// This is the score vanilla uses to choose where a party sits and watches.
        /// Left alone it spreads lords by local threat and ownership, which means
        /// they end up wherever the last alarm came from rather than where an
        /// invader must actually pass. Weighting it by how much of the realm lies
        /// behind a settlement puts them on the approaches instead.
        /// </summary>
        public override float CalculateDefensivePatrollingScoreForSettlement(Settlement settlement, bool isTargetingPort, MobileParty mobileParty)
        {
            float baseScore = base.CalculateDefensivePatrollingScoreForSettlement(settlement, isTargetingPort, mobileParty);

            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (baseScore <= 0f || settings == null || !settings.EnableGatewayDefense)
            {
                return baseScore;
            }

            // Only our own gates are worth standing at.
            if (settlement?.MapFaction == null || mobileParty?.MapFaction == null
                || settlement.MapFaction != mobileParty.MapFaction)
            {
                return baseScore;
            }

            // Somewhere already under attack is handled by the defence scoring, which
            // competes for the same decision. Boosting a quiet gate here could
            // outrank a burning town, which would be the opposite of the intent:
            // this is about standing watch before trouble, not instead of answering it.
            if (settlement.IsUnderSiege || settlement.Party?.MapEvent != null)
            {
                return baseScore;
            }

            float gateway = ChokepointMapBehavior.GetGatewayScore(settlement);
            if (gateway <= 0f)
            {
                return baseScore;
            }

            return baseScore * ClaimPlanner.GatewayDefenseBias(gateway, settings.GatewayDefenseGain);
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
