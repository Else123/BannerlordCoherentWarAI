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

        // ---- CommandCompetence / CommandBonus (AC6) ----
        //
        // API contract assumed by these tests (added to CoherentWarAI.Logic.MarshalPlanner,
        // SPEC SS4.2 / slice-1 step 9 - names and formulas fixed verbatim by the spec):
        //   public const float AccomplishedCommandSkill = 250f;
        //   public const float DefaultCommandWeight = 0.5f;
        //   public static float CommandCompetence(float leadership, float tactics)
        //       => Sqrt(Clamp01((0.6f * leadership + 0.4f * tactics) / AccomplishedCommandSkill));
        //   public static float CommandBonus(float competence, float weight)
        //       => 1f + Max(0f, weight) * Clamp01(competence);

        [Fact]
        public void CommandCompetence_NoSkillAtAll_IsZero()
        {
            Assert.Equal(0f, MarshalPlanner.CommandCompetence(0f, 0f), 3);
        }

        [Fact]
        public void CommandCompetence_IsMonotoneNonDecreasing_InLeadership()
        {
            float low = MarshalPlanner.CommandCompetence(50f, 100f);
            float high = MarshalPlanner.CommandCompetence(150f, 100f);
            Assert.True(high >= low, "more Leadership should never lower command competence");
        }

        [Fact]
        public void CommandCompetence_IsMonotoneNonDecreasing_InTactics()
        {
            float low = MarshalPlanner.CommandCompetence(100f, 50f);
            float high = MarshalPlanner.CommandCompetence(100f, 150f);
            Assert.True(high >= low, "more Tactics should never lower command competence");
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(150f, 50f)]
        [InlineData(250f, 250f)]
        [InlineData(1000f, 1000f)]
        public void CommandCompetence_NeverExceedsOne(float leadership, float tactics)
        {
            Assert.True(MarshalPlanner.CommandCompetence(leadership, tactics) <= 1f);
        }

        [Theory]
        [InlineData(250f, 250f)]   // 0.6*250 + 0.4*250 = 250 -> saturates exactly at the blend
        [InlineData(1000f, 1000f)] // well past saturation
        public void CommandCompetence_Saturates_AtOrAboveThe250Blend(float leadership, float tactics)
        {
            Assert.Equal(1f, MarshalPlanner.CommandCompetence(leadership, tactics), 3);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(0.7f)]
        [InlineData(1f)]
        public void CommandBonus_WithZeroWeight_IsANoOp(float competence)
        {
            Assert.Equal(1f, MarshalPlanner.CommandBonus(competence, 0f), 3);
        }

        [Fact]
        public void CommandBonus_ScalesWithWeightAndCompetence()
        {
            Assert.Equal(1.5f, MarshalPlanner.CommandBonus(1f, 0.5f), 3);
        }

        [Fact]
        public void CommandBonus_NeverBelowOne_ForNegativeWeight()
        {
            Assert.Equal(1f, MarshalPlanner.CommandBonus(1f, -2f), 3);
        }

        [Fact]
        public void DefaultCommandWeight_IsOneHalf()
        {
            Assert.Equal(0.5f, MarshalPlanner.DefaultCommandWeight, 3);
        }

        [Fact]
        public void AccomplishedCommandSkill_Is250()
        {
            Assert.Equal(250f, MarshalPlanner.AccomplishedCommandSkill, 3);
        }

        // ---- OrderLifetimeHours (AC6/AC7, Slice 2a step 5) ----
        //
        // API contract assumed by these tests (added to CoherentWarAI.Logic.MarshalPlanner,
        // COMMAND-SLICE-2A.md step 5 / COMMAND-SPEC.md SS4.2 - names and formula fixed
        // verbatim by the slice spec):
        //   public const float MinOrderLifetimeHours = 24f;
        //   public const float MaxOrderLifetimeHours = 96f;
        //   public const float DefaultOrderLifetimeHours = 48f;
        //   public static float OrderLifetimeHours(float competence, float weight)
        //       // competence and weight each clamped to [0,1] before use
        //       => (1f - w) * DefaultOrderLifetimeHours + w * (MinOrderLifetimeHours + (MaxOrderLifetimeHours - MinOrderLifetimeHours) * c);

        [Theory]
        [InlineData(0f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(1f)]
        public void OrderLifetimeHours_WeightZero_IsExactlyTheSlice1Constant_ForEveryCompetence(float competence)
        {
            // The no-op is Slice 1's shipped 48h constant, not an approximation of it.
            Assert.Equal(48f, MarshalPlanner.OrderLifetimeHours(competence, 0f));
        }

        [Fact]
        public void OrderLifetimeHours_WeightOne_ZeroCompetence_Is24Hours()
        {
            // m5, post-review: no precision arg - the endpoints are exact in float, not
            // merely close to it.
            Assert.Equal(24f, MarshalPlanner.OrderLifetimeHours(0f, 1f));
        }

        [Fact]
        public void OrderLifetimeHours_WeightOne_FullCompetence_Is96Hours()
        {
            Assert.Equal(96f, MarshalPlanner.OrderLifetimeHours(1f, 1f));
        }

        [Fact]
        public void OrderLifetimeHours_IsMonotoneNonDecreasing_InCompetence()
        {
            float low = MarshalPlanner.OrderLifetimeHours(0.2f, 1f);
            float high = MarshalPlanner.OrderLifetimeHours(0.8f, 1f);
            Assert.True(high >= low, "a more competent commander should never shorten the order lifetime");
        }

        [Fact]
        public void OrderLifetimeHours_ClampsCompetenceBelowZero_ToZero()
        {
            Assert.Equal(
                MarshalPlanner.OrderLifetimeHours(0f, 1f),
                MarshalPlanner.OrderLifetimeHours(-5f, 1f), 3);
        }

        [Fact]
        public void OrderLifetimeHours_ClampsCompetenceAboveOne_ToOne()
        {
            Assert.Equal(
                MarshalPlanner.OrderLifetimeHours(1f, 1f),
                MarshalPlanner.OrderLifetimeHours(5f, 1f), 3);
        }

        [Fact]
        public void OrderLifetimeHours_ClampsNegativeWeight_ToZero()
        {
            Assert.Equal(
                MarshalPlanner.OrderLifetimeHours(0.5f, 0f),
                MarshalPlanner.OrderLifetimeHours(0.5f, -2f), 3);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(1f)]
        public void OrderLifetimeHours_WeightSaturatesAtOne_WeightTwoBehavesLikeWeightOne(float competence)
        {
            Assert.Equal(
                MarshalPlanner.OrderLifetimeHours(competence, 1f),
                MarshalPlanner.OrderLifetimeHours(competence, 2f), 3);
        }
    }
}
