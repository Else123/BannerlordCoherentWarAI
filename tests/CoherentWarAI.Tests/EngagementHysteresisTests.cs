using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class EngagementHysteresisTests
    {
        // engage=2.0, abandon=1.4: the band between them is where a party keeps
        // doing whatever it was already doing.
        [Theory]
        [InlineData(2.5f, false, true)]   // clearly strong enough to start
        [InlineData(2.0f, false, true)]   // exactly at the engage threshold
        [InlineData(1.7f, false, false)]  // inside the band, not committed -> don't start
        [InlineData(1.7f, true, true)]    // inside the band, committed -> carry on
        [InlineData(1.4f, true, true)]    // exactly at the abandon threshold
        [InlineData(1.3f, true, false)]   // finally bad enough to abandon
        [InlineData(1.3f, false, false)]
        public void ShouldPursue_UsesDifferentThresholdsToStartAndToContinue(
            float strengthRatio, bool committed, bool expected)
        {
            Assert.Equal(expected, EngagementHysteresis.ShouldPursue(
                strengthRatio, committed,
                EngagementHysteresis.DefaultEngageRatio,
                EngagementHysteresis.DefaultAbandonRatio));
        }

        [Fact]
        public void ShouldPursue_ContinuingNeverDemandsMoreThanStarting()
        {
            // Misconfiguration (abandon above engage) must not make committed
            // parties quit more eagerly than uncommitted ones would start.
            Assert.True(EngagementHysteresis.ShouldPursue(2.0f, committed: true, engageRatio: 2.0f, abandonRatio: 5.0f));
        }

        [Fact]
        public void ShouldPursue_ReproducesTheFlipFlopScenario()
        {
            // Player sits in the castle: defenders look strong, lord does not start.
            Assert.False(EngagementHysteresis.ShouldPursue(1.5f, committed: false,
                EngagementHysteresis.DefaultEngageRatio, EngagementHysteresis.DefaultAbandonRatio));

            // Player steps out: ratio jumps, lord commits.
            Assert.True(EngagementHysteresis.ShouldPursue(2.2f, committed: false,
                EngagementHysteresis.DefaultEngageRatio, EngagementHysteresis.DefaultAbandonRatio));

            // Player walks back in: vanilla would abort here. With hysteresis the
            // lord stays committed, so the bait-and-switch stops working.
            Assert.True(EngagementHysteresis.ShouldPursue(1.5f, committed: true,
                EngagementHysteresis.DefaultEngageRatio, EngagementHysteresis.DefaultAbandonRatio));
        }

        [Theory]
        [InlineData(0f, 12f, true)]      // just committed
        [InlineData(11.9f, 12f, true)]   // still inside the window
        [InlineData(12f, 12f, false)]    // window elapsed
        [InlineData(50f, 12f, false)]
        [InlineData(5f, 0f, false)]      // window disabled
        [InlineData(-1f, 12f, false)]    // nonsensical input -> not protected
        public void IsWithinCommitmentWindow_ProtectsFreshCommitments(
            float hoursSinceCommitted, float minCommitmentHours, bool expected)
        {
            Assert.Equal(expected, EngagementHysteresis.IsWithinCommitmentWindow(hoursSinceCommitted, minCommitmentHours));
        }

        [Theory]
        [InlineData(0f, 24f, 1f)]        // just seen
        [InlineData(6f, 24f, 0.75f)]
        [InlineData(12f, 24f, 0.5f)]
        [InlineData(24f, 24f, 0f)]       // fully stale
        [InlineData(100f, 24f, 0f)]
        [InlineData(-5f, 24f, 1f)]       // clock skew -> treat as fresh
        [InlineData(5f, 0f, 0f)]         // retention disabled
        public void RetentionFactor_DecaysLinearly(float hoursSinceSeen, float decayHours, float expected)
        {
            Assert.Equal(expected, EngagementHysteresis.RetentionFactor(hoursSinceSeen, decayHours), 4);
        }

        [Theory]
        [InlineData(true, 1.4f, 0.5f, 0.5f)]    // fresh: only a collapse breaks it
        [InlineData(false, 1.4f, 0.5f, 1.4f)]   // elapsed: normal abandon threshold
        public void ThresholdForCommitment_RelaxesOnlyWhileFresh(
            bool isFresh, float abandonRatio, float collapseRatio, float expected)
        {
            Assert.Equal(expected, EngagementHysteresis.ThresholdForCommitment(isFresh, abandonRatio, collapseRatio), 4);
        }

        [Fact]
        public void FreshCommitment_StillBreaksOnOutrightCollapse()
        {
            // Defenders flickering (ratio 1.5) must not break a fresh commitment...
            float threshold = EngagementHysteresis.ThresholdForCommitment(
                isFresh: true, EngagementHysteresis.DefaultAbandonRatio, EngagementHysteresis.DefaultCollapseRatio);
            Assert.True(EngagementHysteresis.ShouldPursue(1.5f, committed: true, EngagementHysteresis.DefaultEngageRatio, threshold));

            // ...but a party shattered in battle (ratio 0.3) gives up even while fresh.
            Assert.False(EngagementHysteresis.ShouldPursue(0.3f, committed: true, EngagementHysteresis.DefaultEngageRatio, threshold));
        }

        [Theory]
        [InlineData(2f, 2f, 1f)]        // odds unchanged -> full remembered value
        [InlineData(3f, 2f, 1f)]        // better than at commit -> capped, never inflated
        [InlineData(1f, 2f, 0.5f)]      // odds halved -> value halved
        [InlineData(0f, 2f, 0f)]        // no strength left -> worthless
        [InlineData(1f, 0f, 1f)]        // threshold disabled -> no scaling
        public void OddsFactor_OnlyScalesDown(float currentRatio, float engageRatio, float expected)
        {
            Assert.Equal(expected, EngagementHysteresis.OddsFactor(currentRatio, engageRatio), 4);
        }

        [Theory]
        [InlineData(200f, 100f, 2f)]
        [InlineData(200f, 50f, 2f)]      // defenders floored at 100
        [InlineData(0f, 100f, 0f)]
        [InlineData(-5f, 100f, 0f)]      // negative strength -> no ratio
        [InlineData(300f, 0f, 3f)]       // empty settlement cannot yield infinity
        public void StrengthRatio_FloorsTheDefenderEstimate(float ourStrength, float defenderStrength, float expected)
        {
            Assert.Equal(expected, EngagementHysteresis.StrengthRatio(ourStrength, defenderStrength), 4);
        }
    }
}
