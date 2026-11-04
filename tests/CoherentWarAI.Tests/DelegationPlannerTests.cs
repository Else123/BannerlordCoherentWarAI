// API contract assumed by these tests (namespace CoherentWarAI.Logic, engine-free,
// System.* only, no TaleWorlds reference). Hand-off contract for the implementer of
// Logic/DelegationPlanner.cs (Slice 2a step 4). Signature and rules are taken verbatim
// from COMMAND-SLICE-2A.md's Ansatz + step 4 / COMMAND-SPEC.md SS3.3.
//
//   public static class DelegationPlanner
//   {
//       public const float DefaultDefenseThreatThreshold = 0.25f;
//
//       public static Domain DesiredDomains(int aggressiveSlotCount, int wars, float threatRatio,
//           int ports, int navalCapableLords);
//           // Rule 1: Offense iff aggressiveSlotCount > 0.
//           // Rule 2: Naval   iff ports >= 2 && navalCapableLords >= 1.
//           // Rule 3: Defense iff wars >= 2 || threatRatio > DefaultDefenseThreatThreshold
//           //         (the threatRatio boundary is strictly greater-than).
//           // The three rules are independent and evaluated from the given inputs only -
//           // deterministic, no Random, no static state.
//
//       public static Domain LimitToHolderBudget(Domain desired, int maxMarshals);
//           // Drops flags out of `desired` in reverse of the rules' own priority - Defense
//           // first, then Naval, then Offense - until at most maxMarshals flags remain.
//           // maxMarshals <= 0 -> Domain.None. Never adds a flag that was not already set
//           // in `desired` (a conservative upper bound on distinct holders, not a floor).
//   }

using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class DelegationPlannerTests
    {
        // ---- DesiredDomains rule 1: Offense (AC1) ----

        [Fact]
        public void DesiredDomains_AggressiveSlotCountZero_OffenseAbsent()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 0, threatRatio: 0f, ports: 0, navalCapableLords: 0);

            Assert.Equal(Domain.None, result);
        }

        [Fact]
        public void DesiredDomains_AggressiveSlotCountOne_OffensePresent()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 1, wars: 0, threatRatio: 0f, ports: 0, navalCapableLords: 0);

            Assert.True(result.HasFlag(Domain.Offense));
        }

        [Theory]
        [InlineData(0, 2, 1f, 2, 1)]    // every other rule maxed out
        [InlineData(0, 0, 0f, 0, 0)]
        public void DesiredDomains_AggressiveSlotCountZero_OffenseAbsent_RegardlessOfOtherInputs(
            int aggressiveSlotCount, int wars, float threatRatio, int ports, int navalCapableLords)
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount, wars, threatRatio, ports, navalCapableLords);

            Assert.False(result.HasFlag(Domain.Offense));
        }

        // ---- DesiredDomains rule 2: Naval (AC2) ----

        [Theory]
        [InlineData(1, 0)]  // one port, no naval-capable lord
        [InlineData(1, 1)]  // one port is never enough, even with a capable lord
        [InlineData(2, 0)]  // two ports but nobody who can sail
        [InlineData(0, 1)]  // no ports at all
        public void DesiredDomains_NavalConditionUnmet_NavalAbsent(int ports, int navalCapableLords)
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 0, threatRatio: 0f, ports, navalCapableLords);

            Assert.False(result.HasFlag(Domain.Naval));
        }

        [Fact]
        public void DesiredDomains_TwoPortsAndACapableLord_NavalPresent()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 0, threatRatio: 0f, ports: 2, navalCapableLords: 1);

            Assert.True(result.HasFlag(Domain.Naval));
        }

        // ---- DesiredDomains rule 3: Defense (AC3) ----

        [Fact]
        public void DesiredDomains_OneWar_AtThreatRatioBoundary_DefenseAbsent()
        {
            // The boundary is strictly greater-than: 0.25f itself does not qualify.
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 1, threatRatio: 0.25f, ports: 0, navalCapableLords: 0);

            Assert.False(result.HasFlag(Domain.Defense));
        }

        [Fact]
        public void DesiredDomains_OneWar_JustAboveThreatRatioBoundary_DefensePresent()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 1, threatRatio: 0.26f, ports: 0, navalCapableLords: 0);

            Assert.True(result.HasFlag(Domain.Defense));
        }

        [Fact]
        public void DesiredDomains_OneWar_ZeroThreatRatio_DefenseAbsent()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 1, threatRatio: 0f, ports: 0, navalCapableLords: 0);

            Assert.False(result.HasFlag(Domain.Defense));
        }

        [Fact]
        public void DesiredDomains_TwoWars_ZeroThreatRatio_DefensePresent()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 2, threatRatio: 0f, ports: 0, navalCapableLords: 0);

            Assert.True(result.HasFlag(Domain.Defense));
        }

        // ---- DesiredDomains: independence, extremes and determinism (AC4) ----

        [Fact]
        public void DesiredDomains_AllThreeRulesSatisfied_ReturnsDomainAll()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 1, wars: 2, threatRatio: 0f, ports: 2, navalCapableLords: 1);

            Assert.Equal(Domain.All, result);
        }

        [Fact]
        public void DesiredDomains_PeacefulRealmWithOnePortAndNoAggressiveSlot_ReturnsDomainNone()
        {
            Domain result = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 0, wars: 0, threatRatio: 0f, ports: 1, navalCapableLords: 1);

            Assert.Equal(Domain.None, result);
        }

        [Fact]
        public void DesiredDomains_IsDeterministic_SameInputsTwiceYieldSameResult()
        {
            Domain first = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 1, wars: 1, threatRatio: 0.3f, ports: 2, navalCapableLords: 1);
            Domain second = DelegationPlanner.DesiredDomains(
                aggressiveSlotCount: 1, wars: 1, threatRatio: 0.3f, ports: 2, navalCapableLords: 1);

            Assert.Equal(first, second);
        }

        // ---- LimitToHolderBudget (AC5) ----

        [Fact]
        public void LimitToHolderBudget_BudgetOfThree_ReturnsDesiredUnchanged()
        {
            Domain result = DelegationPlanner.LimitToHolderBudget(Domain.All, maxMarshals: 3);

            Assert.Equal(Domain.All, result);
        }

        [Fact]
        public void LimitToHolderBudget_BudgetOfTwo_DropsDefenseFirst()
        {
            Domain result = DelegationPlanner.LimitToHolderBudget(Domain.All, maxMarshals: 2);

            Assert.Equal(Domain.Offense | Domain.Naval, result);
        }

        [Fact]
        public void LimitToHolderBudget_BudgetOfOne_DropsDefenseThenNaval_LeavesOffense()
        {
            Domain result = DelegationPlanner.LimitToHolderBudget(Domain.All, maxMarshals: 1);

            Assert.Equal(Domain.Offense, result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void LimitToHolderBudget_BudgetZeroOrLess_ReturnsDomainNone(int maxMarshals)
        {
            Domain result = DelegationPlanner.LimitToHolderBudget(Domain.All, maxMarshals);

            Assert.Equal(Domain.None, result);
        }

        [Fact]
        public void LimitToHolderBudget_NeverAddsAFlagThatWasNotInDesired()
        {
            // A generous budget of 3 applied to a single-flag desire must return exactly
            // that flag, not pad it out to Domain.All.
            Domain result = DelegationPlanner.LimitToHolderBudget(Domain.Defense, maxMarshals: 3);

            Assert.Equal(Domain.Defense, result);
        }

        [Fact]
        public void LimitToHolderBudget_TwoFlagDesire_BudgetOfOne_DropsDefenseFirst_LeavesNaval()
        {
            // m5, post-review: a two-flag (not all-three) desire was previously untested.
            Domain result = DelegationPlanner.LimitToHolderBudget(Domain.Defense | Domain.Naval, maxMarshals: 1);

            Assert.Equal(Domain.Naval, result);
        }
    }
}
