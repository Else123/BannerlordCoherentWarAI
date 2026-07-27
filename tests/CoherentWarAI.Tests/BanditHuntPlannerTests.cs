using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class BanditHuntPlannerTests
    {
        // maxThreat=0.25, requiredSuperiority=1.3
        [Theory]
        [InlineData(0f, 1000f, 0f, true)]        // at peace -> free to police
        [InlineData(0f, 1000f, 500f, true)]      // war against someone far weaker
        [InlineData(0f, 1300f, 1000f, true)]     // exactly 1.3x -> still spare
        [InlineData(0f, 1000f, 770f, false)]     // 1.299x -> just short, needed at the front
        [InlineData(0f, 1000f, 800f, false)]     // an even fight -> every lord needed
        [InlineData(0f, 1000f, 2000f, false)]    // losing -> certainly not
        [InlineData(0.5f, 1000f, 0f, false)]     // realm under attack, even at peace
        [InlineData(0.26f, 1000f, 100f, false)]  // just over the threat threshold
        [InlineData(0f, 0f, 500f, false)]        // no strength of our own
        public void RealmMaySpareLords_OnlyWhenNoRealWarNeedsThem(
            float threatRatio, float ourStrength, float primaryEnemyStrength, bool expected)
        {
            Assert.Equal(expected, BanditHuntPlanner.RealmMaySpareLords(
                threatRatio, ourStrength, primaryEnemyStrength,
                BanditHuntPlanner.DefaultMaxThreatRatio,
                BanditHuntPlanner.DefaultRequiredSuperiority));
        }

        [Fact]
        public void AnEvenlyMatchedWarStopsBanditHuntingEntirely()
        {
            // The case the design explicitly calls out: equal or stronger enemy.
            Assert.False(BanditHuntPlanner.RealmMaySpareLords(0f, 1000f, 1000f,
                BanditHuntPlanner.DefaultMaxThreatRatio, BanditHuntPlanner.DefaultRequiredSuperiority));
            Assert.False(BanditHuntPlanner.RealmMaySpareLords(0f, 1000f, 1200f,
                BanditHuntPlanner.DefaultMaxThreatRatio, BanditHuntPlanner.DefaultRequiredSuperiority));
        }

        [Theory]
        [InlineData(true, false, false, false, true)]    // idle defender -> available
        [InlineData(false, false, false, false, false)]  // released to attack
        [InlineData(true, true, false, false, false)]    // in an army
        [InlineData(true, false, true, false, false)]    // marshal
        [InlineData(true, false, false, true, false)]    // already has something to do
        public void LordIsAvailable_OnlyIdleDefendersGo(
            bool isDefensive, bool leadsOrJoinsArmy, bool isMarshal, bool hasOwnObjective, bool expected)
        {
            Assert.Equal(expected, BanditHuntPlanner.LordIsAvailable(
                isDefensive, leadsOrJoinsArmy, isMarshal, hasOwnObjective));
        }

        // requiredAdvantage = 1.5
        [Theory]
        [InlineData(300f, 100f, 0.5f)]    // 3x advantage -> beatable but little gain
        [InlineData(150f, 100f, 1f)]      // exactly at the threshold -> best value
        [InlineData(200f, 100f, 0.75f)]
        [InlineData(140f, 100f, 0f)]      // too strong to take safely
        [InlineData(100f, 200f, 0f)]      // outmatched
        [InlineData(300f, 0f, 0f)]        // nothing there
        [InlineData(0f, 100f, 0f)]        // no troops of our own
        public void QuarryValue_PrefersTheBiggestBandStillSafelyBeatable(
            float ourStrength, float banditStrength, float expected)
        {
            Assert.Equal(expected, BanditHuntPlanner.QuarryValue(
                ourStrength, banditStrength, BanditHuntPlanner.DefaultRequiredHunterAdvantage), 4);
        }

        [Fact]
        public void QuarryValue_NeverSendsALordAgainstOddsItCannotWin()
        {
            // A lord lost to bandits is worse than bandits left alone.
            Assert.Equal(0f, BanditHuntPlanner.QuarryValue(100f, 90f,
                BanditHuntPlanner.DefaultRequiredHunterAdvantage), 4);
        }

        [Theory]
        [InlineData(0f, 24f, true)]
        [InlineData(23.9f, 24f, true)]
        [InlineData(24f, 24f, false)]   // given up as hopeless
        [InlineData(50f, 24f, false)]
        [InlineData(3f, 0f, false)]     // disabled
        [InlineData(-1f, 24f, false)]   // nonsensical
        public void HuntStillWorthPursuing_BoundsAChaseWithoutProtectingIt(
            float hoursSinceStarted, float giveUpAfterHours, bool expected)
        {
            Assert.Equal(expected, BanditHuntPlanner.HuntStillWorthPursuing(hoursSinceStarted, giveUpAfterHours));
        }
    }
}
