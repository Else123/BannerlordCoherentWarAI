using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class ForceCommitmentTests
    {
        [Theory]
        [InlineData(1000f, 0f, 0f)]
        [InlineData(1000f, 250f, 0.25f)]
        [InlineData(1000f, 1000f, 1f)]
        [InlineData(1000f, 1500f, 1f)]   // clamped
        [InlineData(0f, 500f, 0f)]       // nothing to be a share of
        public void DistractionRatio_MeasuresWhatCannotAnswer(
            float totalStrength, float tiedDownStrength, float expected)
        {
            Assert.Equal(expected, ForceCommitment.DistractionRatio(totalStrength, tiedDownStrength), 4);
        }

        // onset 0.3, maxBonus 0.6
        [Theory]
        [InlineData(0f, 1f)]         // nobody busy
        [InlineData(0.2f, 1f)]       // ordinary background activity
        [InlineData(0.3f, 1f)]       // exactly at the onset
        [InlineData(0.65f, 1.3f)]    // half its host committed elsewhere
        [InlineData(1f, 1.6f)]       // everything committed
        public void ExposureBonus_OnlyRewardsAnUnusualConcentration(
            float distractionRatio, float expected)
        {
            Assert.Equal(expected, ForceCommitment.ExposureBonus(
                distractionRatio,
                ForceCommitment.DefaultDistractionOnset,
                ForceCommitment.DefaultExposureBonus), 3);
        }

        [Fact]
        public void EverydayBusynessIsNotAnOpening()
        {
            // Every realm has troops occupied at any moment. Treating that as
            // opportunity would raise every score equally and distinguish nothing.
            float ordinary = ForceCommitment.ExposureBonus(0.25f,
                ForceCommitment.DefaultDistractionOnset, ForceCommitment.DefaultExposureBonus);
            Assert.Equal(1f, ordinary, 4);
        }

        [Fact]
        public void ARealmThatHasThrownItsHostAtASiegeIsVulnerable()
        {
            float committed = ForceCommitment.ExposureBonus(0.8f,
                ForceCommitment.DefaultDistractionOnset, ForceCommitment.DefaultExposureBonus);
            float idle = ForceCommitment.ExposureBonus(0f,
                ForceCommitment.DefaultDistractionOnset, ForceCommitment.DefaultExposureBonus);

            Assert.True(committed > idle * 1.3f, $"{committed} vs {idle}");
        }

        [Theory]
        [InlineData(false, false, 50f, 100f, true)]   // free and close enough
        [InlineData(false, false, 200f, 100f, false)] // free but too far
        [InlineData(true, false, 10f, 100f, false)]   // in a battle at the gate - still no help
        [InlineData(false, true, 10f, 100f, false)]   // holding a siege line of its own
        [InlineData(false, false, 50f, 0f, false)]    // no reach at all
        public void CanIntervene_NearIsNotTheSameAsAvailable(
            bool isEngaged, bool isBesieging, float distance, float reach, bool expected)
        {
            Assert.Equal(expected, ForceCommitment.CanIntervene(isEngaged, isBesieging, distance, reach));
        }
    }
}
