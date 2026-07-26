using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class StrategicPriorityTests
    {
        [Theory]
        [InlineData(true, 1.25f, 0.8f, 1.25f)]
        [InlineData(false, 1.25f, 0.8f, 0.8f)]
        [InlineData(true, -1f, 0.8f, 0f)]      // nonsensical config clamped
        [InlineData(false, 1.25f, -1f, 0f)]
        public void EnemyFocusBias_ConcentratesOnOneWarAtATime(
            bool isPrimary, float boost, float damp, float expected)
        {
            Assert.Equal(expected, StrategicPriority.EnemyFocusBias(isPrimary, boost, damp), 4);
        }

        [Fact]
        public void SecondaryEnemiesAreDampedNotAbandoned()
        {
            // A realm must still be able to answer a second front.
            float secondary = StrategicPriority.EnemyFocusBias(false,
                StrategicPriority.DefaultPrimaryEnemyBoost, StrategicPriority.DefaultSecondaryEnemyDamp);
            Assert.True(secondary > 0.5f, $"secondary war damped too hard: {secondary}");
        }

        // consolidationBonus=0.35, salientPenalty=0.4
        [Theory]
        [InlineData(4, 0, 1.35f)]    // fully enclosed by our own -> rounds off the border
        [InlineData(3, 1, 1.175f)]
        [InlineData(2, 2, 1f)]       // balanced -> neutral
        [InlineData(1, 3, 0.8f)]
        [InlineData(0, 4, 0.6f)]     // ringed by enemies -> a salient, discouraged
        [InlineData(0, 0, 1f)]       // isolated, nothing to say
        public void HoldabilityBias_PrefersConquestsThatRoundOffTheBorder(
            int friendlyNeighbors, int hostileNeighbors, float expected)
        {
            Assert.Equal(expected, StrategicPriority.HoldabilityBias(
                friendlyNeighbors, hostileNeighbors,
                StrategicPriority.DefaultConsolidationBonus,
                StrategicPriority.DefaultSalientPenalty), 4);
        }

        [Fact]
        public void ASalientIsDiscouragedButNeverVetoed()
        {
            float salient = StrategicPriority.HoldabilityBias(0, 5,
                StrategicPriority.DefaultConsolidationBonus, StrategicPriority.DefaultSalientPenalty);
            Assert.True(salient > 0f, "this nudges target choice, it must not veto conquests");
            Assert.True(salient < 1f, "a salient should still be less attractive than neutral ground");
        }

        [Fact]
        public void RoundingOffTheBorderBeatsJuttingOutIntoEnemyGround()
        {
            float consolidating = StrategicPriority.HoldabilityBias(4, 1,
                StrategicPriority.DefaultConsolidationBonus, StrategicPriority.DefaultSalientPenalty);
            float salient = StrategicPriority.HoldabilityBias(1, 4,
                StrategicPriority.DefaultConsolidationBonus, StrategicPriority.DefaultSalientPenalty);
            Assert.True(consolidating > salient * 1.4f, $"{consolidating} vs {salient}");
        }

        [Theory]
        [InlineData(2, false, 1.25f)]   // engine says prioritised -> wins over our reading
        [InlineData(1, true, 0.8f)]     // engine says secondary -> wins even against our pick
        [InlineData(0, true, 1.25f)]    // engine silent -> our heuristic speaks
        [InlineData(0, false, 0.8f)]
        public void CombinedWarFocus_ChoosesBetweenTheTwoOpinionsInsteadOfStackingThem(
            int behaviorPriority, bool isPrimary, float expected)
        {
            Assert.Equal(expected, StrategicPriority.CombinedWarFocus(
                behaviorPriority, isPrimary,
                StrategicPriority.DefaultPrimaryEnemyBoost,
                StrategicPriority.DefaultSecondaryEnemyDamp), 4);
        }

        [Fact]
        public void CombinedWarFocus_NeverDampsAFrontTwiceForOneReason()
        {
            // Both sources calling a war secondary must not compound.
            float combined = StrategicPriority.CombinedWarFocus(1, false,
                StrategicPriority.DefaultPrimaryEnemyBoost, StrategicPriority.DefaultSecondaryEnemyDamp);
            float stacked = StrategicPriority.DefaultSecondaryEnemyDamp * StrategicPriority.DefaultSecondaryEnemyDamp;
            Assert.True(combined > stacked, $"combined {combined} must beat the stacked {stacked}");
        }

        [Theory]
        [InlineData(1f, 0.25f, 1f)]
        [InlineData(0.5f, 0.25f, 0.5f)]
        [InlineData(0.1f, 0.25f, 0.25f)]   // floored
        [InlineData(0f, 0.25f, 0.25f)]
        [InlineData(-1f, 0.25f, 0f)]       // nonsensical input
        [InlineData(0.1f, 0f, 0.1f)]       // floor disabled
        public void ApplyWeightFloor_KeepsAnOffensiveOnTheTable(
            float combinedWeight, float floor, float expected)
        {
            Assert.Equal(expected, StrategicPriority.ApplyWeightFloor(combinedWeight, floor), 4);
        }

        [Fact]
        public void CompoundedWeights_CannotSilenceALordEntirely()
        {
            // Worst realistic case: overkill damped, off our front, target already
            // covered, secondary war, and a salient conquest. Individually each is a
            // mild nudge; multiplied they must still leave an attack worth making.
            float worst =
                TargetWeights.Overkill(1000f, 100f, TargetWeights.DefaultOverkillOnset,
                    TargetWeights.DefaultOverkillMinFactor, TargetWeights.DefaultOverkillSpan)
                * TargetWeights.FrontCoherence(0, 4, TargetWeights.DefaultFrontFloor, TargetWeights.DefaultFrontGain)
                * ClaimPlanner.SaturationBias(5000f, 1000f,
                    ClaimPlanner.DefaultSaturationSuppression, ClaimPlanner.DefaultNeglectBonus)
                * StrategicPriority.CombinedWarFocus(1, false,
                    StrategicPriority.DefaultPrimaryEnemyBoost, StrategicPriority.DefaultSecondaryEnemyDamp)
                * StrategicPriority.HoldabilityBias(0, 4,
                    StrategicPriority.DefaultConsolidationBonus, StrategicPriority.DefaultSalientPenalty);

            Assert.True(worst < 0.1f, $"sanity: the unfloored worst case should be severe, was {worst}");

            float floored = StrategicPriority.ApplyWeightFloor(worst, 0.25f);
            Assert.Equal(0.25f, floored, 4);
        }

        [Theory]
        [InlineData(2, 1.25f, 0.8f, 1.25f)]   // vanilla "prioritised" war
        [InlineData(1, 1.25f, 0.8f, 0.8f)]    // vanilla "de-prioritised" war
        [InlineData(0, 1.25f, 0.8f, 1f)]      // unset -> neutral
        [InlineData(7, 1.25f, 0.8f, 1f)]      // unknown value -> neutral
        public void WarPriorityBias_HonoursTheVanillaStanceField(
            int behaviorPriority, float boost, float damp, float expected)
        {
            Assert.Equal(expected, StrategicPriority.WarPriorityBias(behaviorPriority, boost, damp), 4);
        }
    }
}
