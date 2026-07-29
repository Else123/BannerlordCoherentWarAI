using CoherentWarAI.Behaviors;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Map;
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

            // Each weight is gated by its own setting. They must not be nested
            // inside one another: the settings page presents them as independent
            // switches, so turning one off has to leave the others working.
            bool needDefenderEstimate = settings.EnableTargetDeGreed
                || settings.EnableCoordination
                || settings.CountNearbyDefenders;
            float defenderStrength = needDefenderEstimate ? EstimateDefenderStrength(targetSettlement) : 0f;

            // What vanilla could not see: relieving forces nearby, and the player at
            // full weight rather than discounted. Applied as a correction because
            // vanilla's own defender figure is buried inside the base score.
            float wVisibility = 1f;
            if (settings.CountNearbyDefenders && isOffensive)
            {
                float available = EstimateAvailableDefence(targetSettlement, mobileParty, defenderStrength);
                wVisibility = TargetWeights.DefenderVisibilityCorrection(defenderStrength, available);
                if (wVisibility < 1f)
                {
                    WarAiStats.RecordDefenceCorrection();
                }
            }

            float wOverkill = 1f;
            float wFront = 1f;
            if (settings.EnableTargetDeGreed)
            {
                float onset = TargetWeights.AdaptiveOnset(settings.OverkillOnset, WarAiStats.TypicalStrengthRatio);
                wOverkill = TargetWeights.Overkill(ourStrength, defenderStrength, onset, settings.OverkillMinFactor, settings.OverkillSpan);
                WarAiStats.ObserveStrengthRatio(ourStrength, defenderStrength);

                CountFrontNeighbors(targetSettlement, mobileParty, out int ownedByUs, out int notOwnedByTarget);
                wFront = TargetWeights.FrontCoherence(ownedByUs, notOwnedByTarget, settings.FrontFloor, settings.FrontGain);
            }

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

            // Which war to press, and whether this conquest would round off our
            // border or stick out into theirs.
            float wStrategy = StrategicWeight(targetSettlement, mobileParty, settings);

            // Four mild nudges compound. Defending and patrolling are scored by
            // paths we do not touch, so an over-damped attack would not merely rank
            // lower - it would lose to standing around. Floor the combination.
            // Sticking with what this lord already set out to do.
            //
            // Measured across a campaign: vanilla never once rejected a target a
            // lord was already pursuing, so waiting for a target to become
            // impossible - the original approach - could never fire. The dithering
            // at the gates is relative, not absolute: a lord is lured away because
            // something else briefly scores higher, not because his own target
            // became unreachable. So the pull has to be on the target he already
            // has. Vanilla applies a mild stickiness of its own; this deepens it.
            float wCommitment = 1f;
            if (settings.EnableCommitmentHysteresis && IsPursuing(mobileParty, targetSettlement))
            {
                wCommitment = settings.PursuitStickiness;
                WarAiStats.RecordPursuitHeld();
            }

            float raw = wOverkill * wFront * wCoord * wStrategy * wCommitment * wVisibility;
            float combined = StrategicPriority.ApplyWeightFloor(raw, settings.MinimumWeightFloor);

            WarAiStats.RecordScore(wOverkill, wFront, wCoord, wStrategy, combined > raw);

            float score = baseScore * combined;

            // Off by default: this runs hundreds of times per game hour.
            if (WarAiLog.VerboseScoring)
            {
                WarAiLog.Write("Score", string.Format(
                    "{0} -> {1} ({2}): vanilla {3:F1} x overkill {4:F2} x front {5:F2} x coord {6:F2} x strategy {7:F2} = {8:F1}",
                    mobileParty.LeaderHero?.Name, targetSettlement.Name, missionType,
                    baseScore, wOverkill, wFront, wCoord, wStrategy, score));
            }

            // Remember the assessment of the target this lord is actually pursuing.
            //
            // Only that one: a party rates dozens of candidates every tick, and the
            // store holds one entry per party, so remembering every candidate meant
            // the entry was overwritten constantly and almost never described the
            // target that later dropped to zero. That is why the hysteresis never
            // fired in a full playtest despite being enabled throughout.
            if (settings.EnableCommitmentHysteresis && IsPursuing(mobileParty, targetSettlement))
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

            bool pursuing = IsPursuing(mobileParty, targetSettlement);

            if (!_commitments.TryGet(mobileParty, targetSettlement, missionType,
                    out float lastScore, out float hoursSinceSeen, out float hoursSinceCommitted))
            {
                WarAiStats.RecordRejectedTarget(pursuing, hadMemory: false, memoryStale: false, oddsTooBad: false);
                return 0f;
            }

            float retention = EngagementHysteresis.RetentionFactor(hoursSinceSeen, settings.RetentionDecayHours);
            if (retention <= 0f)
            {
                WarAiStats.RecordRejectedTarget(pursuing, hadMemory: true, memoryStale: true, oddsTooBad: false);
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
                WarAiStats.RecordRejectedTarget(pursuing, hadMemory: true, memoryStale: false, oddsTooBad: true);
                return 0f;
            }

            // Scale by how the odds actually stand now, so a held target cannot
            // outrank freshly rated ones on a stale, rosier assessment.
            float held = lastScore * retention * EngagementHysteresis.OddsFactor(ratio, settings.EngageRatio);

            // This is the flip-flop being prevented: vanilla just wrote the target
            // off, and we are keeping the lord on it. Worth seeing in the log.
            WarAiStats.RecordHysteresisHold();
            WarAiLog.Write("Hysteresis", string.Format(
                "{0} holds {1} ({2}) - vanilla would abort; ratio {3:F2}, {4} commitment, score {5:F1}",
                mobileParty.LeaderHero?.Name, targetSettlement.Name, missionType,
                ratio, isFresh ? "fresh" : "matured", held));

            return held;
        }

        /// <summary>
        /// Combines the realm-level judgements about a target: which war it belongs
        /// to, and what taking it would do to our border.
        /// </summary>
        private static float StrategicWeight(Settlement targetSettlement, MobileParty mobileParty, CoherentWarAISettings settings)
        {
            float weight = 1f;
            IFaction ourFaction = mobileParty.MapFaction;
            IFaction targetFaction = targetSettlement.MapFaction;

            if (settings.EnableEnemyFocus && ourFaction != null && targetFaction != null)
            {
                // Vanilla records a per-war priority but only ever applies it when
                // the party's faction leader is the player, leaving AI realms with
                // no notion of a prioritised war. This guard is exactly vanilla's
                // own gate, so the player's kingdom keeps vanilla behaviour and the
                // term is never counted twice.
                int behaviorPriority = 0;
                if (ourFaction.Leader != Hero.MainHero)
                {
                    StanceLink stance = ourFaction.GetStanceWith(targetFaction);
                    if (stance != null)
                    {
                        behaviorPriority = stance.BehaviorPriority;
                    }
                }

                weight *= StrategicPriority.CombinedWarFocus(
                    behaviorPriority,
                    WarCoordinatorBehavior.IsPrimaryEnemy(ourFaction, targetFaction),
                    settings.PrimaryEnemyBoost, settings.SecondaryEnemyDamp);
            }

            if (settings.EnableHoldability && ourFaction != null)
            {
                CountHoldabilityNeighbors(targetSettlement, ourFaction, out int wouldBeOurs, out int wouldStayForeign);
                float holdability = StrategicPriority.HoldabilityBias(
                    wouldBeOurs, wouldStayForeign,
                    settings.ConsolidationBonus, settings.SalientPenalty);
                WarAiStats.RecordHoldability(holdability);
                weight *= holdability;
            }

            return weight;
        }

        /// <summary>
        /// Whether this lord is already heading for this settlement, as opposed to
        /// merely rating it among the dozens he considers each tick.
        /// </summary>
        private static bool IsPursuing(MobileParty mobileParty, Settlement targetSettlement)
        {
            return mobileParty.BesiegedSettlement == targetSettlement
                || mobileParty.TargetSettlement == targetSettlement;
        }

        /// <summary>
        /// Who would surround this settlement once we held it.
        ///
        /// Deliberately a separate count from the front-coherence one: that ignores
        /// neighbours belonging to the target's own faction, because they are not
        /// contested ground. For holdability they are the whole point - a fief deep
        /// inside enemy land, ringed by that same enemy's castles, is the textbook
        /// salient, and the front-coherence count sees it as having no neighbours at
        /// all. Here anything not ours would stay foreign.
        ///
        /// Neighbours belonging to factions we are at peace with are not counted as
        /// hostile; a quiet neighbour is not a threatened border.
        /// </summary>
        private static void CountHoldabilityNeighbors(Settlement targetSettlement, IFaction ourFaction, out int wouldBeOurs, out int wouldStayForeign)
        {
            wouldBeOurs = 0;
            wouldStayForeign = 0;

            foreach (Settlement neighbor in SettlementNeighbors.Of(targetSettlement))
            {
                IFaction neighborFaction = neighbor.MapFaction;
                if (neighborFaction == null)
                {
                    continue;
                }

                if (neighborFaction == ourFaction)
                {
                    wouldBeOurs++;
                }
                else if (neighborFaction.IsAtWarWith(ourFaction))
                {
                    wouldStayForeign++;
                }
            }
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

            // Same fallback the garrison model uses: when the route analysis is
            // switched off, fall back to counting neighbours rather than silently
            // degrading this feature to nothing behind an unrelated setting.
            float gateway;
            if (ChokepointMapBehavior.HasComputedScores)
            {
                gateway = ChokepointMapBehavior.GetGatewayScore(settlement);
            }
            else
            {
                // Counted here rather than via CountFrontNeighbors: that one is
                // written for rating an ENEMY settlement, so it only counts
                // neighbours not belonging to the target's faction. This settlement
                // is ours, which means that filter excludes precisely our own
                // neighbours - it would return zero every time and the fallback
                // would silently do nothing.
                int foreign = 0;
                int friendly = 0;
                foreach (Settlement neighbor in SettlementNeighbors.Of(settlement))
                {
                    IFaction neighborFaction = neighbor.MapFaction;
                    if (neighborFaction == null)
                    {
                        continue;
                    }
                    if (neighborFaction == settlement.MapFaction)
                    {
                        friendly++;
                    }
                    else
                    {
                        foreign++;
                    }
                }

                gateway = GarrisonPlanner.ChokepointScore(foreign, friendly, settings.ChokepointSaturation);
            }

            if (gateway <= 0f)
            {
                return baseScore;
            }

            WarAiStats.RecordGatewayDefence();
            return baseScore * ClaimPlanner.GatewayDefenseBias(gateway, settings.GatewayDefenseGain);
        }

        /// <summary>
        /// Local defender strength estimate: garrison, militia, and lord parties of
        /// the owning faction present at the settlement.
        ///
        /// Close to vanilla's own figure but not identical - vanilla counts any
        /// aggressive party present regardless of allegiance, which credits a target
        /// with allied visitors and passing third parties. This counts only who
        /// defenders. Used to decide when extra attacker strength is pure overkill.
        /// </summary>
        /// <summary>
        /// Everyone who could realistically fight for this settlement - those inside
        /// it and any friendly force close enough to intervene - counted at full
        /// weight, including the player.
        ///
        /// Vanilla counts only who is inside, and discounts the player even then.
        /// A lord standing outside the gate is no less able to defend the place, so
        /// leaving him out is what makes an attacker's judgement lurch when the
        /// player rides in or out.
        /// </summary>
        private static float EstimateAvailableDefence(Settlement targetSettlement, MobileParty scoringParty, float countedInside)
        {
            IFaction defenderFaction = targetSettlement.MapFaction;
            if (defenderFaction == null)
            {
                return countedInside;
            }

            // Close enough to reach the walls before an assault is decided.
            float radius = Campaign.Current.Models.EncounterModel.GetEncounterJoiningRadius * 3f;
            if (radius <= 0f)
            {
                return countedInside;
            }

            // Skip the spatial query for targets this lord could not reach soon
            // anyway - vanilla gates its own relief search the same way, and without
            // it every candidate settlement triggers a scan on the hot path.
            float reach = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(
                MobileParty.NavigationType.All) * 3f;
            if (reach > 0f && targetSettlement.Position.Distance(scoringParty.Position) > reach)
            {
                return countedInside;
            }

            float nearby = 0f;
            LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(
                targetSettlement.GatePosition.ToVec2(), radius);

            for (MobileParty party = MobileParty.FindNextLocatable(ref data);
                 party != null;
                 party = MobileParty.FindNextLocatable(ref data))
            {
                // Those inside are already counted; garrison and militia cannot leave.
                if (party?.Party == null || party.CurrentSettlement == targetSettlement)
                {
                    continue;
                }
                if (party.IsGarrison || party.IsMilitia || party.IsCaravan || party.IsVillager)
                {
                    continue;
                }
                if (party.MapFaction != defenderFaction || party.Aggressiveness <= 0.01f)
                {
                    continue;
                }

                nearby += party.Party.EstimatedStrength;
            }

            return countedInside + nearby;
        }

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

            IFaction targetFaction = targetSettlement.MapFaction;
            IFaction ourFaction = mobileParty.MapFaction;

            foreach (Settlement neighbor in SettlementNeighbors.Of(targetSettlement))
            {
                IFaction neighborFaction = neighbor.MapFaction;
                if (neighborFaction != targetFaction)
                {
                    notOwnedByTarget++;
                    if (neighborFaction == ourFaction)
                    {
                        ownedByUs++;
                    }
                }
            }
        }
    }
}
