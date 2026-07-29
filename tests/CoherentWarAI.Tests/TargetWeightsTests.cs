using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class TargetWeightsTests
    {
        // onset=1.5, minFactor=0.6, span=1.5 (documented defaults) and a second
        // parameter set (onset=1.0, minFactor=0.5, span=2.0) to prove the params
        // are actually honored rather than hard-coded.
        [Theory]
        [InlineData(100f, 100f, 1.5f, 0.6f, 1.5f, 1.0f)]      // r=1.0 -> at/below onset -> 1.0
        [InlineData(150f, 100f, 1.5f, 0.6f, 1.5f, 1.0f)]      // r=1.5 -> exactly onset (boundary) -> 1.0
        [InlineData(225f, 100f, 1.5f, 0.6f, 1.5f, 0.8f)]      // r=2.25 -> halfway -> lerp(1.0,0.6,0.5)
        [InlineData(300f, 100f, 1.5f, 0.6f, 1.5f, 0.6f)]      // r=3.0 -> exactly onset+span -> minFactor
        [InlineData(500f, 100f, 1.5f, 0.6f, 1.5f, 0.6f)]      // r=5.0 -> beyond onset+span -> clamped to minFactor
        [InlineData(100f, 50f, 1.5f, 0.6f, 1.5f, 1.0f)]       // defenderStrength floored to 100 -> r=1.0
        [InlineData(300f, 10f, 1.5f, 0.6f, 1.5f, 0.6f)]       // defenderStrength floored to 100 -> r=3.0
        [InlineData(100f, 100f, 1.0f, 0.5f, 2.0f, 1.0f)]      // alt params: r=1.0 -> at onset -> 1.0
        [InlineData(150f, 100f, 1.0f, 0.5f, 2.0f, 0.875f)]    // alt params: r=1.5 -> lerp t=0.25
        [InlineData(200f, 100f, 1.0f, 0.5f, 2.0f, 0.75f)]     // alt params: r=2.0 -> lerp t=0.5
        [InlineData(300f, 100f, 1.0f, 0.5f, 2.0f, 0.5f)]      // alt params: r=3.0 -> at onset+span -> minFactor
        [InlineData(500f, 100f, 1.5f, 0.6f, 0.0f, 1.0f)]      // span<=0 guard: r>onset but zero span -> no damping
        public void Overkill_MatchesDocumentedContract(
            float ourStrength,
            float defenderStrength,
            float onset,
            float minFactor,
            float span,
            float expected)
        {
            var actual = TargetWeights.Overkill(ourStrength, defenderStrength, onset, minFactor, span);

            Assert.Equal(expected, actual, 4);
        }

        [Theory]
        [InlineData(500f, 500f, 1f)]        // nothing overlooked
        [InlineData(500f, 1000f, 0.5f)]     // half the defence was invisible
        [InlineData(500f, 2000f, 0.25f)]    // a relieving army stood just outside
        [InlineData(500f, 400f, 1f)]        // never makes a target MORE inviting
        [InlineData(50f, 200f, 0.5f)]       // both floored at 100 first
        public void DefenderVisibilityCorrection_CountsWhoCouldActuallyFight(
            float asVanillaCountedThem, float allWhoCouldDefend, float expected)
        {
            Assert.Equal(expected, TargetWeights.DefenderVisibilityCorrection(
                asVanillaCountedThem, allWhoCouldDefend), 4);
        }

        [Fact]
        public void SteppingOutOfACastleNoLongerInvitesAnAttack()
        {
            // The exploit: vanilla counts the player at half strength, and less
            // again while inside, so walking out made a castle look weaker than it
            // was. Counting everyone who could fight gives the same answer either
            // way - which is the point.
            const float garrison = 400f;
            const float playerParty = 600f;

            float vanillaSawPlayerInside = garrison + playerParty * 0.4f;
            float vanillaSawPlayerOutside = garrison;
            float trulyAvailable = garrison + playerParty;

            float correctedInside = TargetWeights.DefenderVisibilityCorrection(vanillaSawPlayerInside, trulyAvailable);
            float correctedOutside = TargetWeights.DefenderVisibilityCorrection(vanillaSawPlayerOutside, trulyAvailable);

            Assert.True(correctedOutside < correctedInside,
                "standing outside must not make the castle look like an easier target");
            Assert.True(correctedOutside < 0.5f, $"the overlooked force should weigh heavily, got {correctedOutside}");
        }

        [Theory]
        [InlineData(1.5f, 1.2f, 1.5f)]   // quiet campaign -> configured value stands
        [InlineData(1.5f, 2.4f, 2.4f)]   // armies have outgrown garrisons -> threshold follows
        [InlineData(1.5f, 1.5f, 1.5f)]
        public void AdaptiveOnset_TracksTheCampaignButNeverLoosens(
            float configuredOnset, float typicalRatio, float expected)
        {
            Assert.Equal(expected, TargetWeights.AdaptiveOnset(configuredOnset, typicalRatio), 4);
        }

        // frontFloor=0.6, frontGain=0.9 (documented defaults).
        [Theory]
        [InlineData(0, 4, 0.6f, 0.9f, 0.6f)]    // ownShare=0 -> floor
        [InlineData(4, 4, 0.6f, 0.9f, 1.5f)]    // ownShare=1.0 -> floor + gain
        [InlineData(2, 4, 0.6f, 0.9f, 1.05f)]   // ownShare=0.5
        [InlineData(1, 3, 0.6f, 0.9f, 0.9f)]    // ownShare=1/3
        [InlineData(3, 2, 0.6f, 0.9f, 1.5f)]    // ratio 1.5 clamped to 1.0
        [InlineData(0, 0, 0.6f, 0.9f, 0.6f)]    // isolated target (notOwnedByTarget<=0) -> floor
        [InlineData(5, 0, 0.6f, 0.9f, 0.6f)]    // isolated target -> floor regardless of owned
        public void FrontCoherence_MatchesDocumentedContract(
            int ownedByUs,
            int notOwnedByTarget,
            float frontFloor,
            float frontGain,
            float expected)
        {
            var actual = TargetWeights.FrontCoherence(ownedByUs, notOwnedByTarget, frontFloor, frontGain);

            Assert.Equal(expected, actual, 4);
        }
    }
}
