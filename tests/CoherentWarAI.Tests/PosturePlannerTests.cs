using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class PosturePlannerTests
    {
        // share=0.34, minDefenders=2 unless stated otherwise.
        [Theory]
        [InlineData(0, 0f, 0.34f, 2, 0)]      // no parties -> no attackers
        [InlineData(10, 0f, 0.34f, 2, 3)]     // unthreatened: round(10*0.34)=3
        [InlineData(10, 0.5f, 0.34f, 2, 2)]   // half the realm threatened: round(10*0.17)=2
        [InlineData(10, 1f, 0.34f, 2, 0)]     // fully threatened -> everyone defends
        [InlineData(3, 0f, 0.34f, 2, 1)]      // round(3*0.34)=1, cap 3-2=1
        [InlineData(2, 0f, 0.34f, 2, 1)]      // reserve capped: a small realm can still attack
        [InlineData(1, 0f, 1.0f, 2, 1)]       // lone party, fully aggressive posture
        [InlineData(1, 0f, 0.34f, 2, 0)]      // lone party, normal share -> defends
        [InlineData(2, 1f, 0.34f, 2, 0)]      // small realm under full threat -> defends
        [InlineData(10, 0f, 1.0f, 0, 10)]     // full aggression, no reserve
        [InlineData(10, 0f, 0f, 0, 0)]        // zero share -> pure defense
        [InlineData(10, -1f, 0.34f, 2, 3)]    // threat clamped at 0
        [InlineData(10, 2f, 0.34f, 2, 0)]     // threat clamped at 1
        public void AggressiveSlotCount_ScalesWithThreatAndRespectsReserve(
            int warPartyCount, float threatRatio, float aggressiveShare, int minimumDefenders, int expected)
        {
            Assert.Equal(expected, PosturePlanner.AggressiveSlotCount(warPartyCount, threatRatio, aggressiveShare, minimumDefenders));
        }

        [Fact]
        public void AggressiveSlotCount_NeverExceedsPartyCount()
        {
            Assert.Equal(5, PosturePlanner.AggressiveSlotCount(5, 0f, 2.0f, 0));
        }

        [Theory]
        [InlineData(100f, 0, 0.25f, 100f)]     // neutral valor -> unchanged
        [InlineData(100f, 2, 0.25f, 150f)]     // bold lord ranks higher
        [InlineData(100f, -2, 0.25f, 50f)]     // cautious lord ranks lower
        [InlineData(100f, 5, 0.25f, 150f)]     // valor clamped to +2
        [InlineData(100f, -5, 0.25f, 50f)]     // valor clamped to -2
        [InlineData(100f, 2, 0f, 100f)]        // weight 0 -> trait ignored
        [InlineData(-10f, 0, 0.25f, 0f)]       // negative strength floored
        [InlineData(100f, -2, 2.0f, 0f)]       // extreme weight cannot invert the score
        public void AggressionScore_WeightsStrengthByValor(
            float partyStrength, int valorTraitLevel, float valorWeight, float expected)
        {
            Assert.Equal(expected, PosturePlanner.AggressionScore(partyStrength, valorTraitLevel, valorWeight), 4);
        }

        [Theory]
        [InlineData(0, 3, Posture.Aggressive)]   // top-ranked within allowance
        [InlineData(2, 3, Posture.Aggressive)]   // last slot
        [InlineData(3, 3, Posture.Defensive)]    // just outside allowance
        [InlineData(9, 3, Posture.Defensive)]
        [InlineData(0, 0, Posture.Defensive)]    // no offensive slots at all
        [InlineData(-1, 3, Posture.Defensive)]   // unranked -> defend
        public void DecidePosture_AttacksOnlyWithinAllowance(int aggressionRank, int aggressiveSlots, Posture expected)
        {
            Assert.Equal(expected, PosturePlanner.DecidePosture(aggressionRank, aggressiveSlots));
        }

        [Fact]
        public void DefenseIsTheDefault_WhenRealmIsUnderHeavyThreat()
        {
            // 8 parties, 80% of the realm threatened: nobody should be attacking.
            int slots = PosturePlanner.AggressiveSlotCount(8, 0.8f, PosturePlanner.DefaultAggressiveShare, PosturePlanner.DefaultMinimumDefenders);
            Assert.Equal(1, slots);
            Assert.Equal(Posture.Aggressive, PosturePlanner.DecidePosture(0, slots));
            Assert.Equal(Posture.Defensive, PosturePlanner.DecidePosture(1, slots));
        }
    }
}
