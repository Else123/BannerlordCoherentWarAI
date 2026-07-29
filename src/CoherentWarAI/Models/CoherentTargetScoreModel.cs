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

            WeightToggles toggles = BuildToggles(settings, targetSettlement, mobileParty);
            ScoreInputs inputs = GatherInputs(targetSettlement, mobileParty, ourStrength, toggles, settings);
            ScoreWeights weights = ScoreComposer.Compose(inputs, toggles, BuildTuning(settings, mobileParty.MapFaction));

            RecordDiagnostics(weights, inputs, toggles, mobileParty.MapFaction, ourStrength);

            float score = baseScore * weights.Combined;

            // Off by default: this runs hundreds of times per game hour.
            if (WarAiLog.VerboseScoring)
            {
                WarAiLog.Write("Score", string.Format(
                    "{0} -> {1} ({2}): vanilla {3:F1} x overkill {4:F2} x front {5:F2} x coord {6:F2} x strategy {7:F2} = {8:F1}",
                    mobileParty.LeaderHero?.Name, targetSettlement.Name, missionType,
                    baseScore, weights.Overkill, weights.Front, weights.Coordination, weights.Strategy, score));
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
        /// Which weights apply to this call. Settings decide, but so does whether
        /// the data a weight needs exists at all - a weight with nothing to work
        /// from is switched off here rather than defended against later.
        /// </summary>
        private static WeightToggles BuildToggles(CoherentWarAISettings settings, Settlement targetSettlement, MobileParty mobileParty)
        {
            bool factionsKnown = mobileParty.MapFaction != null && targetSettlement.MapFaction != null;

            return new WeightToggles
            {
                CountNearbyDefenders = settings.CountNearbyDefenders,
                DeGreedTargets = settings.EnableTargetDeGreed,
                Coordination = settings.EnableCoordination,
                EnemyFocus = settings.EnableEnemyFocus && factionsKnown,
                Holdability = settings.EnableHoldability && mobileParty.MapFaction != null,
                CommitmentStickiness = settings.EnableCommitmentHysteresis
            };
        }

        /// <summary>
        /// Reads from the game only what the enabled weights actually need. Each
        /// gather is guarded by the toggles' own derived properties, so a weight
        /// cannot end up reading a value nobody computed.
        /// </summary>
        private static ScoreInputs GatherInputs(Settlement targetSettlement, MobileParty mobileParty, float ourStrength, WeightToggles toggles, CoherentWarAISettings settings)
        {
            ScoreInputs inputs = new ScoreInputs { AttackerStrength = ourStrength };

            if (toggles.NeedsDefenderStrength)
            {
                inputs.DefenderStrength = EstimateDefenderStrength(targetSettlement);
            }

            if (toggles.CountNearbyDefenders)
            {
                inputs.AvailableDefence = EstimateAvailableDefence(targetSettlement, mobileParty, inputs.DefenderStrength);
            }

            if (toggles.NeedsFrontNeighbours)
            {
                CountFrontNeighbors(targetSettlement, mobileParty, out int ownedByUs, out int notOwnedByTarget);
                inputs.FrontOwnedByUs = ownedByUs;
                inputs.FrontNotOwnedByTarget = notOwnedByTarget;
            }

            if (toggles.NeedsPursuitState)
            {
                inputs.IsPursuingTarget = WarCoordinatorBehavior.GetCommittedTarget(mobileParty) == targetSettlement
                    || IsPursuing(mobileParty, targetSettlement);
            }

            if (toggles.Coordination)
            {
                inputs.CommittedStrength = WarCoordinatorBehavior.GetCommittedStrengthExcluding(targetSettlement, mobileParty);
            }

            if (toggles.EnemyFocus)
            {
                IFaction ourFaction = mobileParty.MapFaction;
                IFaction targetFaction = targetSettlement.MapFaction;
                inputs.IsPrimaryEnemy = WarCoordinatorBehavior.IsPrimaryEnemy(ourFaction, targetFaction);

                // Vanilla records a per-war priority but only applies it when the
                // party's faction leader is the player. This guard is exactly
                // vanilla's own gate, so the player's kingdom keeps vanilla
                // behaviour and the term is never counted twice.
                if (ourFaction.Leader != Hero.MainHero)
                {
                    StanceLink stance = ourFaction.GetStanceWith(targetFaction);
                    if (stance != null)
                    {
                        inputs.StancePriority = stance.BehaviorPriority;
                    }
                }
            }

            if (toggles.NeedsHoldabilityNeighbours)
            {
                CountHoldabilityNeighbors(targetSettlement, mobileParty.MapFaction,
                    out int wouldBeOurs, out int wouldStayForeign);
                inputs.HoldabilityFriendlyNeighbours = wouldBeOurs;
                inputs.HoldabilityHostileNeighbours = wouldStayForeign;
            }

            return inputs;
        }

        /// <summary>
        /// Tuning values as the composer wants them. The overkill onset is resolved
        /// here because it depends on what the campaign has looked like so far.
        /// </summary>
        private static ScoreTuning BuildTuning(CoherentWarAISettings settings, IFaction ourFaction)
        {
            return new ScoreTuning
            {
                OverkillOnset = TargetWeights.AdaptiveOnset(settings.OverkillOnset, WarAiStats.TypicalRatioFor(ourFaction)),
                OverkillMinFactor = settings.OverkillMinFactor,
                OverkillSpan = settings.OverkillSpan,
                FrontFloor = settings.FrontFloor,
                FrontGain = settings.FrontGain,
                RequiredMargin = settings.RequiredMargin,
                SaturationSuppression = settings.SaturationSuppression,
                NeglectBonus = settings.NeglectBonus,
                PrimaryEnemyBoost = settings.PrimaryEnemyBoost,
                SecondaryEnemyDamp = settings.SecondaryEnemyDamp,
                ConsolidationBonus = settings.ConsolidationBonus,
                SalientPenalty = settings.SalientPenalty,
                PursuitStickiness = settings.PursuitStickiness,
                MinimumWeightFloor = settings.MinimumWeightFloor
            };
        }

        private static void RecordDiagnostics(ScoreWeights weights, ScoreInputs inputs, WeightToggles toggles, IFaction ourFaction, float ourStrength)
        {
            if (toggles.CountNearbyDefenders && weights.Visibility < 1f)
            {
                WarAiStats.RecordDefenceCorrection();
            }
            if (toggles.DeGreedTargets)
            {
                WarAiStats.ObserveStrengthRatio(ourFaction, ourStrength, inputs.DefenderStrength);
            }
            if (toggles.Holdability)
            {
                WarAiStats.RecordHoldability(weights.HoldabilityBias);
            }
            if (toggles.CommitmentStickiness && inputs.IsPursuingTarget)
            {
                WarAiStats.RecordPursuitHeld();
            }

            WarAiStats.RecordScore(weights.Overkill, weights.Front, weights.Coordination,
                weights.Strategy, weights.WasFloored);
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
