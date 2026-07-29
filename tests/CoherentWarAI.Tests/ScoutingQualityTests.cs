using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class ScoutingQualityTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(250f, 1f)]      // accomplished
        [InlineData(500f, 1f)]      // capped
        [InlineData(-10f, 0f)]
        [InlineData(62.5f, 0.5f)]   // a quarter of the way in skill is half the capability
        public void Competence_RisesQuicklyThenLevelsOff(float skill, float expected)
        {
            Assert.Equal(expected, ScoutingQuality.Competence(skill), 4);
        }

        [Fact]
        public void TheFirstStepsOfSkillMatterMostForARaggedScout()
        {
            // Going from nobody to passable should buy more than going from good to
            // excellent - a party with any scout at all is far better placed than
            // one with none.
            float noneToPassable = ScoutingQuality.Competence(60f) - ScoutingQuality.Competence(0f);
            float goodToExcellent = ScoutingQuality.Competence(250f) - ScoutingQuality.Competence(190f);

            Assert.True(noneToPassable > goodToExcellent, $"{noneToPassable} vs {goodToExcellent}");
        }

        [Theory]
        [InlineData(0f, 1f)]        // sees no further than the base radius
        [InlineData(250f, 2f)]      // an accomplished scout doubles it
        [InlineData(62.5f, 1.5f)]
        public void ReachMultiplier_AGoodScoutSeesFurther(float skill, float expected)
        {
            Assert.Equal(expected, ScoutingQuality.ReachMultiplier(
                skill, ScoutingQuality.DefaultReachBonus), 4);
        }

        [Theory]
        [InlineData(0f, 0.5f)]      // an untrained eye still says something
        [InlineData(250f, 1f)]      // proper reconnaissance
        [InlineData(62.5f, 0.75f)]
        public void Confidence_NeverZeroButFarFromEqual(float skill, float expected)
        {
            Assert.Equal(expected, ScoutingQuality.Confidence(
                skill, ScoutingQuality.DefaultMinimumConfidence), 4);
        }

        [Fact]
        public void EvenAPoorObserverIsWorthSomething()
        {
            // Someone riding straight into an army will say so, however little he
            // understands of what he saw.
            Assert.True(ScoutingQuality.Confidence(0f, ScoutingQuality.DefaultMinimumConfidence) > 0f);
        }

        [Fact]
        public void AGiftedScoutIsWorthFarMoreThanNone()
        {
            float gifted = ScoutingQuality.ReachMultiplier(250f, ScoutingQuality.DefaultReachBonus)
                * ScoutingQuality.Confidence(250f, ScoutingQuality.DefaultMinimumConfidence);
            float none = ScoutingQuality.ReachMultiplier(0f, ScoutingQuality.DefaultReachBonus)
                * ScoutingQuality.Confidence(0f, ScoutingQuality.DefaultMinimumConfidence);

            // Sees twice as far and is believed twice as readily.
            Assert.Equal(4f, gifted / none, 3);
        }
    }
}
