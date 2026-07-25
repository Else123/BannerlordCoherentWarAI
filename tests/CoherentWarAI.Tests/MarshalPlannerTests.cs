using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class MarshalPlannerTests
    {
        // slotsPerMarshal=6, maxMarshals=3
        [Theory]
        [InlineData(0, 6, 3, 0)]     // nobody attacking -> no marshal
        [InlineData(1, 6, 3, 1)]     // a lone offensive still needs a leader
        [InlineData(6, 6, 3, 1)]
        [InlineData(7, 6, 3, 2)]
        [InlineData(12, 6, 3, 2)]
        [InlineData(18, 6, 3, 3)]
        [InlineData(50, 6, 3, 3)]    // capped: many converging campaigns is the problem
        [InlineData(2, 6, 3, 1)]
        [InlineData(1, 0, 3, 1)]     // degenerate config -> capped by lords available
        public void MarshalCount_KeepsOffensivesFewAndLed(
            int aggressiveSlots, int slotsPerMarshal, int maxMarshals, int expected)
        {
            Assert.Equal(expected, MarshalPlanner.MarshalCount(aggressiveSlots, slotsPerMarshal, maxMarshals));
        }

        [Fact]
        public void MarshalCount_NeverExceedsTheLordsAvailable()
        {
            Assert.Equal(2, MarshalPlanner.MarshalCount(2, 1, 5));
        }

        [Fact]
        public void TheScatterOfSmallPartiesBecomesAFewLedHosts()
        {
            // The empire from the campaign log: 17 lords sent out individually.
            int marshals = MarshalPlanner.MarshalCount(17,
                MarshalPlanner.DefaultSlotsPerMarshal, MarshalPlanner.DefaultMaxMarshals);
            Assert.Equal(3, marshals);
            Assert.True(marshals < 17 / 4, "seventeen separate attacks must collapse into a handful of hosts");
        }

        [Theory]
        [InlineData(400f, 0, false, 400f)]     // plain lord
        [InlineData(400f, 0, true, 600f)]      // ruler outranks for the post
        [InlineData(400f, 2, false, 600f)]     // bold lord, valor 2 at weight 0.25
        [InlineData(400f, 2, true, 900f)]      // both
        [InlineData(0f, 2, true, 0f)]          // no troops, no command
        public void MarshalSuitability_PrefersStrengthThenBoldnessThenRank(
            float partyStrength, int valorTraitLevel, bool isRuler, float expected)
        {
            float actual = MarshalPlanner.MarshalSuitability(partyStrength, valorTraitLevel, isRuler,
                PosturePlanner.DefaultValorWeight, MarshalPlanner.DefaultRulerBonus);
            Assert.Equal(expected, actual, 3);
        }

        [Fact]
        public void AStrongLordStillOutranksAWeakRuler()
        {
            float weakRuler = MarshalPlanner.MarshalSuitability(100f, 0, true,
                PosturePlanner.DefaultValorWeight, MarshalPlanner.DefaultRulerBonus);
            float strongLord = MarshalPlanner.MarshalSuitability(500f, 0, false,
                PosturePlanner.DefaultValorWeight, MarshalPlanner.DefaultRulerBonus);
            Assert.True(strongLord > weakRuler, "a marshal has to be worth following");
        }

        [Theory]
        [InlineData(true, true, true)]    // marshal, doctrine on -> may raise
        [InlineData(false, true, false)]  // ordinary lord falls in behind one
        [InlineData(false, false, true)]  // doctrine off -> vanilla behaviour
        [InlineData(true, false, true)]
        public void MayRaiseArmy_RestrictsHostRaisingToMarshals(
            bool isMarshal, bool doctrineEnabled, bool expected)
        {
            Assert.Equal(expected, MarshalPlanner.MayRaiseArmy(isMarshal, doctrineEnabled));
        }
    }
}
