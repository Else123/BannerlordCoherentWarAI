using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    /// <summary>
    /// These exist because of a bug that happened three times: a weight computed
    /// inside a block guarded by a *different* feature's toggle, so switching one
    /// feature off silently disabled another. Reviews caught all three; nothing
    /// else could, because the composition used to live in engine-facing code that
    /// no test could reach. It lives in the logic layer now, so this is checkable.
    /// </summary>
    public class ScoreComposerTests
    {
        private static ScoreTuning Defaults()
        {
            return new ScoreTuning
            {
                OverkillOnset = TargetWeights.DefaultOverkillOnset,
                OverkillMinFactor = TargetWeights.DefaultOverkillMinFactor,
                OverkillSpan = TargetWeights.DefaultOverkillSpan,
                FrontFloor = TargetWeights.DefaultFrontFloor,
                FrontGain = TargetWeights.DefaultFrontGain,
                RequiredMargin = ClaimPlanner.DefaultRequiredMargin,
                SaturationSuppression = ClaimPlanner.DefaultSaturationSuppression,
                NeglectBonus = ClaimPlanner.DefaultNeglectBonus,
                PrimaryEnemyBoost = StrategicPriority.DefaultPrimaryEnemyBoost,
                SecondaryEnemyDamp = StrategicPriority.DefaultSecondaryEnemyDamp,
                ConsolidationBonus = StrategicPriority.DefaultConsolidationBonus,
                SalientPenalty = StrategicPriority.DefaultSalientPenalty,
                PursuitStickiness = EngagementHysteresis.DefaultPursuitStickiness,
                MinimumWeightFloor = 0.25f,
                DistractionOnset = ForceCommitment.DefaultDistractionOnset,
                DistractionExposureBonus = ForceCommitment.DefaultExposureBonus,
                KnowledgeLifetimeHours = 240f,
                UnknownPenalty = SightingNetwork.DefaultUnknownPenalty
            };
        }

        /// <summary>A representative attack: strong lord, modest garrison, deep in enemy land.</summary>
        private static ScoreInputs Typical()
        {
            return new ScoreInputs
            {
                AttackerStrength = 900f,
                DefenderStrength = 300f,
                AvailableDefence = 900f,
                FrontOwnedByUs = 0,
                FrontNotOwnedByTarget = 4,
                CommittedStrength = 2000f,
                IsPursuingTarget = false,
                IsPrimaryEnemy = false,
                StancePriority = 0,
                HoldabilityFriendlyNeighbours = 0,
                HoldabilityHostileNeighbours = 4,
                EnemyDistraction = 0f,
                TargetBordersOurLand = false,
                HoursSinceObserved = -1f
            };
        }

        [Fact]
        public void EverythingOff_LeavesTheVanillaScoreUntouched()
        {
            ScoreWeights w = ScoreComposer.Compose(Typical(), new WeightToggles(), Defaults());

            Assert.Equal(1f, w.Visibility, 4);
            Assert.Equal(1f, w.Overkill, 4);
            Assert.Equal(1f, w.Front, 4);
            Assert.Equal(1f, w.Coordination, 4);
            Assert.Equal(1f, w.Strategy, 4);
            Assert.Equal(1f, w.Commitment, 4);
            Assert.Equal(1f, w.Raw, 4);
            Assert.Equal(1f, w.Combined, 4);
            Assert.False(w.WasFloored);
        }

        [Fact]
        public void OnlyNearbyDefenders_StillComputesFromTheRealDefenceFigure()
        {
            // The exact historical bug: this weight was computed from a defender
            // estimate that only two OTHER features caused to be gathered. With
            // those off the estimate was zero, and the weight damped every target
            // hard - scoring worse than vanilla.
            ScoreInputs inputs = Typical();
            WeightToggles toggles = new WeightToggles { CountNearbyDefenders = true };

            ScoreWeights w = ScoreComposer.Compose(inputs, toggles, Defaults());

            // 300 counted, 900 actually available -> a third.
            Assert.Equal(0.3333f, w.Visibility, 4);
            Assert.Equal(1f, w.Overkill, 4);
            Assert.Equal(1f, w.Front, 4);
            Assert.Equal(1f, w.Coordination, 4);
            Assert.Equal(1f, w.Strategy, 4);
            Assert.Equal(1f, w.Commitment, 4);
        }

        [Fact]
        public void OnlyDeGreed_TouchesOnlyOverkillAndFront()
        {
            ScoreWeights w = ScoreComposer.Compose(Typical(), new WeightToggles { DeGreedTargets = true }, Defaults());

            Assert.True(w.Overkill < 1f, "an attacker at 3x should be damped");
            Assert.Equal(0.6f, w.Front, 4);   // no friendly ground near it -> the floor
            Assert.Equal(1f, w.Visibility, 4);
            Assert.Equal(1f, w.Coordination, 4);
            Assert.Equal(1f, w.Strategy, 4);
            Assert.Equal(1f, w.Commitment, 4);
        }

        [Fact]
        public void OnlyCoordination_TouchesOnlyCoordination()
        {
            ScoreWeights w = ScoreComposer.Compose(Typical(), new WeightToggles { Coordination = true }, Defaults());

            Assert.True(w.Coordination < 1f, "a target already over-subscribed should repel arrivals");
            Assert.Equal(1f, w.Visibility, 4);
            Assert.Equal(1f, w.Overkill, 4);
            Assert.Equal(1f, w.Front, 4);
            Assert.Equal(1f, w.Strategy, 4);
            Assert.Equal(1f, w.Commitment, 4);
        }

        [Fact]
        public void OnlyEnemyFocus_TouchesOnlyStrategy()
        {
            ScoreWeights w = ScoreComposer.Compose(Typical(), new WeightToggles { EnemyFocus = true }, Defaults());

            Assert.Equal(StrategicPriority.DefaultSecondaryEnemyDamp, w.Strategy, 4);
            Assert.Equal(1f, w.Visibility, 4);
            Assert.Equal(1f, w.Overkill, 4);
            Assert.Equal(1f, w.Front, 4);
            Assert.Equal(1f, w.Coordination, 4);
            Assert.Equal(1f, w.Commitment, 4);
        }

        [Fact]
        public void OnlyHoldability_TouchesOnlyStrategy()
        {
            ScoreWeights w = ScoreComposer.Compose(Typical(), new WeightToggles { Holdability = true }, Defaults());

            Assert.True(w.Strategy < 1f, "a fief ringed by enemies is a salient");
            Assert.Equal(w.HoldabilityBias, w.Strategy, 4);
            Assert.Equal(1f, w.Overkill, 4);
            Assert.Equal(1f, w.Coordination, 4);
            Assert.Equal(1f, w.Commitment, 4);
        }

        [Fact]
        public void OnlyCommitment_AppliesOnlyToAPursuedTarget()
        {
            ScoreInputs notPursuing = Typical();
            ScoreWeights idle = ScoreComposer.Compose(notPursuing, new WeightToggles { CommitmentStickiness = true }, Defaults());
            Assert.Equal(1f, idle.Commitment, 4);

            ScoreInputs pursuing = Typical();
            pursuing.IsPursuingTarget = true;
            ScoreWeights held = ScoreComposer.Compose(pursuing, new WeightToggles { CommitmentStickiness = true }, Defaults());

            Assert.Equal(EngagementHysteresis.DefaultPursuitStickiness, held.Commitment, 4);
            Assert.Equal(1f, held.Overkill, 4);
            Assert.Equal(1f, held.Front, 4);
        }

        [Fact]
        public void CoordinationSpares_ALordAlreadyCommittedHere()
        {
            ScoreInputs pursuing = Typical();
            pursuing.IsPursuingTarget = true;

            ScoreWeights w = ScoreComposer.Compose(pursuing, new WeightToggles { Coordination = true }, Defaults());

            Assert.Equal(1f, w.Coordination, 4);
        }

        [Fact]
        public void EveryWeightIsReachableOnItsOwn()
        {
            // Turning exactly one feature on must move exactly one weight. This is
            // the property that was violated three times.
            ScoreInputs inputs = Typical();
            inputs.IsPursuingTarget = true;
            ScoreTuning tuning = Defaults();

            Assert.NotEqual(1f, ScoreComposer.Compose(inputs, new WeightToggles { CountNearbyDefenders = true }, tuning).Visibility);
            Assert.NotEqual(1f, ScoreComposer.Compose(inputs, new WeightToggles { DeGreedTargets = true }, tuning).Front);
            Assert.NotEqual(1f, ScoreComposer.Compose(inputs, new WeightToggles { EnemyFocus = true }, tuning).Strategy);
            Assert.NotEqual(1f, ScoreComposer.Compose(inputs, new WeightToggles { Holdability = true }, tuning).Strategy);
            Assert.NotEqual(1f, ScoreComposer.Compose(inputs, new WeightToggles { CommitmentStickiness = true }, tuning).Commitment);

            ScoreInputs arriving = Typical();
            Assert.NotEqual(1f, ScoreComposer.Compose(arriving, new WeightToggles { Coordination = true }, tuning).Coordination);

            Assert.NotEqual(1f, ScoreComposer.Compose(inputs, new WeightToggles { RequireKnowledge = true }, tuning).Knowledge);

            ScoreInputs enemyBusy = Typical();
            enemyBusy.EnemyDistraction = 0.9f;
            Assert.NotEqual(1f, ScoreComposer.Compose(enemyBusy, new WeightToggles { ExploitDistraction = true }, tuning).Exposure);
        }

        [Fact]
        public void KnowledgeIsNotAppliedUnlessAskedFor()
        {
            // The knowledge weight rides on the sighting network, and the caller
            // switches it off when there is no network to read. If it leaked into
            // an unrelated toggle, every target would silently be penalised for
            // being unseen by a system that was never running.
            ScoreInputs unseen = Typical();

            ScoreWeights w = ScoreComposer.Compose(unseen, new WeightToggles
            {
                CountNearbyDefenders = true,
                DeGreedTargets = true,
                Coordination = true,
                EnemyFocus = true,
                CommitmentStickiness = true,
                ExploitDistraction = true
            }, Defaults());

            Assert.Equal(1f, w.Knowledge, 4);
        }

        [Fact]
        public void AWatchedBorderFiefOutranksAnUnseenOneAllElseEqual()
        {
            // What the whole feature is for: given two identical targets, the AI
            // should march on the one it has eyes on.
            ScoreTuning tuning = Defaults();
            WeightToggles toggles = new WeightToggles { RequireKnowledge = true };

            ScoreInputs watched = Typical();
            watched.TargetBordersOurLand = true;

            ScoreInputs unseen = Typical();

            Assert.True(ScoreComposer.Compose(watched, toggles, tuning).Combined
                > ScoreComposer.Compose(unseen, toggles, tuning).Combined);
        }

        [Fact]
        public void TheFloorIsAlwaysApplied()
        {
            // Everything stacked against this target at once.
            ScoreInputs grim = Typical();
            WeightToggles all = new WeightToggles
            {
                CountNearbyDefenders = true,
                DeGreedTargets = true,
                Coordination = true,
                EnemyFocus = true,
                Holdability = true,
                CommitmentStickiness = true
            };

            ScoreWeights w = ScoreComposer.Compose(grim, all, Defaults());

            Assert.True(w.Raw < 0.25f, $"the unfloored product should be severe, was {w.Raw}");
            Assert.Equal(0.25f, w.Combined, 4);
            Assert.True(w.WasFloored);
        }
    }
}
