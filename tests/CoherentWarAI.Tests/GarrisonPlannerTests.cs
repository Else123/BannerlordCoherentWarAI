using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class GarrisonPlannerTests
    {
        // saturation=2: score rises with how much of a hub the settlement is, but
        // only when it both faces the enemy and shields friendly ground.
        [Theory]
        [InlineData(0, 4, 2f, 0f)]        // no enemy neighbours -> interior, not a gate
        [InlineData(4, 0, 2f, 0f)]        // nothing behind it -> exposed outpost, not a gate
        [InlineData(0, 0, 2f, 0f)]        // isolated
        [InlineData(1, 1, 2f, 0.3333f)]   // minor crossing: harmonic 1 -> 1/3
        [InlineData(4, 4, 2f, 0.6667f)]   // real gateway: harmonic 4 -> 4/6
        [InlineData(10, 10, 2f, 0.8333f)] // major hub, saturating
        [InlineData(1, 9, 2f, 0.4737f)]   // lopsided: harmonic 1.8 -> 1.8/3.8
        public void ChokepointScore_RewardsOnlyRealGateways(
            int enemyNeighbors, int friendlyNeighbors, float saturation, float expected)
        {
            Assert.Equal(expected, GarrisonPlanner.ChokepointScore(enemyNeighbors, friendlyNeighbors, saturation), 4);
        }

        [Fact]
        public void ChokepointScore_IsSymmetricInItsTwoSides()
        {
            Assert.Equal(
                GarrisonPlanner.ChokepointScore(2, 6, 2f),
                GarrisonPlanner.ChokepointScore(6, 2, 2f), 4);
        }

        // interiorBase=0.8, borderBase=1.4, threatGain=0.15, threatCap=4, peaceCap=1.1
        [Theory]
        [InlineData(false, 0f, true, 0.8f)]     // quiet interior at war -> shrink, free troops
        [InlineData(true, 0f, true, 1.4f)]      // border, no active threat
        [InlineData(true, 2f, true, 1.82f)]     // border under pressure: 1.4*(1+0.3)
        [InlineData(true, 4f, true, 2.24f)]     // at the threat cap: 1.4*1.6
        [InlineData(true, 99f, true, 2.24f)]    // threat clamped
        [InlineData(true, -5f, true, 1.4f)]     // negative threat ignored
        [InlineData(true, 4f, false, 1.1f)]     // peacetime cap applies
        [InlineData(false, 0f, false, 0.8f)]    // peace, interior: below the cap already
        public void ThreatFactor_ScalesWithExposureAndCapsInPeace(
            bool isBorder, float activeThreat, bool atWar, float expected)
        {
            float actual = GarrisonPlanner.ThreatFactor(isBorder, activeThreat, atWar,
                GarrisonPlanner.DefaultInteriorBase, GarrisonPlanner.DefaultBorderBase,
                GarrisonPlanner.DefaultThreatGain, GarrisonPlanner.DefaultThreatCap,
                GarrisonPlanner.DefaultPeaceCap);
            Assert.Equal(expected, actual, 4);
        }

        [Theory]
        [InlineData(1.4f, 0f, 0.5f, 1.4f)]      // not a chokepoint -> unchanged
        [InlineData(1.4f, 1f, 0.5f, 2.1f)]      // full gateway -> 1.4*1.5
        [InlineData(1.4f, 0.5f, 0.5f, 1.75f)]
        [InlineData(1.4f, 2f, 0.5f, 2.1f)]      // score clamped to 1
        [InlineData(1.4f, -1f, 0.5f, 1.4f)]     // score clamped to 0
        [InlineData(1.4f, 1f, 0f, 1.4f)]        // gain 0 -> chokepoints ignored
        public void GarrisonMultiplier_RaisesGatewaysAboveOrdinaryBorders(
            float threatFactor, float chokepointScore, float chokepointGain, float expected)
        {
            Assert.Equal(expected, GarrisonPlanner.GarrisonMultiplier(threatFactor, chokepointScore, chokepointGain), 4);
        }

        [Theory]
        [InlineData(100, 1.4f, 140)]
        [InlineData(100, 0.8f, 80)]
        [InlineData(100, 1f, 100)]
        [InlineData(0, 2f, 0)]
        [InlineData(-5, 2f, 0)]     // nonsensical vanilla input
        [InlineData(100, 0f, 0)]
        [InlineData(100, -1f, 0)]
        [InlineData(3, 1.5f, 5)]    // 4.5 rounds away from zero
        public void ScaleTroopCount_StaysASaneInteger(int vanillaCount, float multiplier, int expected)
        {
            Assert.Equal(expected, GarrisonPlanner.ScaleTroopCount(vanillaCount, multiplier));
        }

        [Theory]
        [InlineData(1f, 1, 3, 1)]        // parity -> vanilla rate
        [InlineData(0.8f, 1, 3, 1)]      // quiet interior -> vanilla rate
        [InlineData(1.9f, 1, 3, 1)]      // below the next full multiple
        [InlineData(2f, 1, 3, 2)]        // one full multiple above parity
        [InlineData(3.5f, 1, 3, 3)]      // capped at maxCap
        [InlineData(9f, 1, 3, 3)]
        [InlineData(9f, 1, 1, 1)]        // maxCap equal to vanilla disables the boost
        [InlineData(2f, 1, 0, 1)]        // maxCap below vanilla is ignored, never reduces
        public void RecruitmentCap_LetsThreatenedSettlementsRefillFaster(
            float multiplier, int vanillaCap, int maxCap, int expected)
        {
            Assert.Equal(expected, GarrisonPlanner.RecruitmentCap(multiplier, vanillaCap, maxCap));
        }

        [Fact]
        public void AGatewayUnderSiegePressureIsHeldFarHarderThanAQuietInteriorFief()
        {
            float gateway = GarrisonPlanner.GarrisonMultiplier(
                GarrisonPlanner.ThreatFactor(true, 3f, true,
                    GarrisonPlanner.DefaultInteriorBase, GarrisonPlanner.DefaultBorderBase,
                    GarrisonPlanner.DefaultThreatGain, GarrisonPlanner.DefaultThreatCap,
                    GarrisonPlanner.DefaultPeaceCap),
                GarrisonPlanner.ChokepointScore(4, 4, GarrisonPlanner.DefaultChokepointSaturation),
                GarrisonPlanner.DefaultChokepointGain);

            float interior = GarrisonPlanner.GarrisonMultiplier(
                GarrisonPlanner.ThreatFactor(false, 0f, true,
                    GarrisonPlanner.DefaultInteriorBase, GarrisonPlanner.DefaultBorderBase,
                    GarrisonPlanner.DefaultThreatGain, GarrisonPlanner.DefaultThreatCap,
                    GarrisonPlanner.DefaultPeaceCap),
                0f, GarrisonPlanner.DefaultChokepointGain);

            Assert.True(gateway > interior * 2.5f, $"gateway {gateway} should dwarf interior {interior}");
        }
    }
}
