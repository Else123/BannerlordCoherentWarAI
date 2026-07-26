using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class ClaimPlannerTests
    {
        [Theory]
        [InlineData(500f, 2f, 1000f)]
        [InlineData(50f, 2f, 200f)]    // defenders floored at 100 first
        [InlineData(0f, 2f, 200f)]     // empty fief still warrants a real party
        [InlineData(500f, 0f, 500f)]   // margin 0 never drops below the defenders
        [InlineData(500f, 1f, 500f)]
        public void RequiredStrength_ScalesWithDefendersAndNeverUnderstates(
            float defenderStrength, float margin, float expected)
        {
            Assert.Equal(expected, ClaimPlanner.RequiredStrength(defenderStrength, margin), 4);
        }

        // suppression=0.3, neglectBonus=1.25
        [Theory]
        [InlineData(0f, 1000f, 1f)]         // nobody going -> neutral (see DefaultNeglectBonus)
        [InlineData(500f, 1000f, 1f)]       // under-committed -> reinforcing is fine
        [InlineData(1000f, 1000f, 1f)]      // exactly enough -> still neutral
        [InlineData(1500f, 1000f, 0.65f)]   // 50% over -> halfway to the floor
        [InlineData(2000f, 1000f, 0.3f)]    // twice what is needed -> full suppression
        [InlineData(9000f, 1000f, 0.3f)]    // clamped, never inverts
        [InlineData(500f, 0f, 1f)]          // no requirement known -> neutral
        public void SaturationBias_DampsOnlyOnceEnoughForceIsCommitted(
            float committed, float required, float expected)
        {
            Assert.Equal(expected, ClaimPlanner.SaturationBias(
                committed, required,
                ClaimPlanner.DefaultSaturationSuppression,
                ClaimPlanner.DefaultNeglectBonus), 4);
        }

        [Fact]
        public void SaturationBias_ReproducesTheDogpileBeingPrevented()
        {
            // A weak fief needing 1000: the first lord is unimpeded...
            Assert.Equal(1f, ClaimPlanner.SaturationBias(0f, 1000f,
                ClaimPlanner.DefaultSaturationSuppression, ClaimPlanner.DefaultNeglectBonus), 4);

            // ...a second still helps if the first cannot manage alone...
            Assert.Equal(1f, ClaimPlanner.SaturationBias(600f, 1000f,
                ClaimPlanner.DefaultSaturationSuppression, ClaimPlanner.DefaultNeglectBonus), 4);

            // ...but once the job is covered twice over, everyone else is pushed away.
            float crowded = ClaimPlanner.SaturationBias(2200f, 1000f,
                ClaimPlanner.DefaultSaturationSuppression, ClaimPlanner.DefaultNeglectBonus);
            Assert.True(crowded < 0.4f, $"a covered target must repel further lords, got {crowded}");
        }

        [Theory]
        [InlineData(0f, 0.8f, 1f)]        // not a gateway -> no pull
        [InlineData(1f, 0.8f, 1.8f)]      // full gateway -> strong pull
        [InlineData(0.5f, 0.8f, 1.4f)]
        [InlineData(2f, 0.8f, 1.8f)]      // score clamped
        [InlineData(-1f, 0.8f, 1f)]       // score clamped
        [InlineData(1f, 0f, 1f)]          // gain 0 -> feature off
        public void GatewayDefenseBias_DrawsDefendersToTheGates(
            float gatewayScore, float gain, float expected)
        {
            Assert.Equal(expected, ClaimPlanner.GatewayDefenseBias(gatewayScore, gain), 4);
        }

        [Fact]
        public void AGatewayOutranksAnOrdinaryFiefForDefenders()
        {
            float gate = ClaimPlanner.GatewayDefenseBias(0.6f, ClaimPlanner.DefaultGatewayDefenseGain);
            float ordinary = ClaimPlanner.GatewayDefenseBias(0f, ClaimPlanner.DefaultGatewayDefenseGain);
            Assert.True(gate > ordinary, $"gate {gate} should beat {ordinary}");
        }

        [Fact]
        public void GatewayPullStaysModestEnoughNotToOutrankRealTrouble()
        {
            // Patrolling scores compete with the scores for answering an actual
            // attack, so the strongest possible gate pull must stay bounded.
            float strongest = ClaimPlanner.GatewayDefenseBias(1f, ClaimPlanner.DefaultGatewayDefenseGain);
            Assert.True(strongest <= 2f, $"gate pull {strongest} is too strong to be safe against siege response");
        }
    }
}
