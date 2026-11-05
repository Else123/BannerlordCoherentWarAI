// API contract assumed by these tests (namespace CoherentWarAI.Logic, engine-free,
// System.* only). Hand-off contract for the implementer of Logic/NavalPatrolPlanner.cs
// (Slice-1 step 11). Names/inputs are taken from COMMAND-SPEC.md SS4.1 and the slice-1
// step list where fixed; the lane-selection shape below is genuinely open in the spec
// (SS4.1 says only "rank ports by net threat ... place a patrol point at the midpoint of
// the two most threatened ports ... one port -> patrol that port's PortPosition ... zero
// -> nothing") and is resolved here with a documented ASSUMPTION.
//
//   public readonly struct PortSignal
//   {
//       public readonly float X, Y;
//       public readonly float ThreatIntensity;   // Settlement.NearbyNavalThreatIntensity
//       public readonly float AllyIntensity;     // Settlement.NearbyNavalAllyIntensity
//       public PortSignal(float x, float y, float threatIntensity, float allyIntensity);
//       // Net threat = ThreatIntensity - AllyIntensity (SPEC SS4.1: "rank our ports by net
//       // naval threat"). Ranking must stay relative only (never against an absolute
//       // constant, SPEC SS4.1 / O10), which is exercised by the scaled-vector test below.
//   }
//
//   public readonly struct PatrolLane
//   {
//       public readonly float X, Y;
//       public readonly float Urgency;   // ASSUMPTION: average net threat of the port(s) the lane covers
//   }
//
//   public static class NavalPatrolPlanner
//   {
//       public static PatrolLane[] SelectLanes(PortSignal[] ports, int wanted);
//           // ASSUMPTION (the spec fixes the two-port/one-port/zero-port cases but not how a
//           // list of >2 ports maps to >1 lane, since SS4.1's prose only ever describes a
//           // single lane). Resolved as: sort ports by net threat descending (ties broken by
//           // original array index ascending, for determinism), then pair them off in that
//           // order - lane 0 = midpoint(sorted[0], sorted[1]), lane 1 = midpoint(sorted[2],
//           // sorted[3]), etc. A leftover unpaired port (odd count) becomes its own lane at
//           // its own position. `wanted` caps the number of lanes returned (never generates
//           // more than `wanted`, never more than ceil(ports.Length / 2)). Zero ports -> empty
//           // array. One port -> a single lane at that port's own (X, Y). Deterministic: no
//           // Random, stable tie-break by index, same input always yields the same output.
//
//       public static int PatrolCount(int capableLords, float competence, int maxPatrols);
//           // 0 capable lords -> 0; result never exceeds maxPatrols; non-decreasing in
//           // competence (0..1) and non-decreasing in capableLords.
//   }

using System;
using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class NavalPatrolPlannerTests
    {
        // ---- SelectLanes (AC8) ----

        [Fact]
        public void SelectLanes_TwoPorts_ReturnsMidpointOfBoth()
        {
            var ports = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 2f, allyIntensity: 0f),   // net 2
                new PortSignal(10f, 10f, threatIntensity: 3f, allyIntensity: 0f)  // net 3
            };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 1);

            Assert.Single(lanes);
            Assert.Equal(5f, lanes[0].X, 3);
            Assert.Equal(5f, lanes[0].Y, 3);
        }

        [Fact]
        public void SelectLanes_ThreePorts_MidpointUsesTheTwoHighestNetThreatPorts_RegardlessOfArrayOrder()
        {
            // The lowest-net-threat port sits in the middle of the array on purpose, so a
            // naive "first two in the array" implementation would fail this test.
            var ports = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 5f, allyIntensity: 0f),    // net 5 (highest)
                new PortSignal(100f, 100f, threatIntensity: 1f, allyIntensity: 1f), // net 0 (lowest)
                new PortSignal(10f, 0f, threatIntensity: 4f, allyIntensity: 0f)     // net 4 (second highest)
            };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 1);

            Assert.Single(lanes);
            Assert.Equal(5f, lanes[0].X, 3);
            Assert.Equal(0f, lanes[0].Y, 3);
        }

        [Fact]
        public void SelectLanes_SinglePortRealm_ReturnsThatPortsOwnPosition()
        {
            var ports = new[] { new PortSignal(7f, 3f, threatIntensity: 1f, allyIntensity: 0f) };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 2);

            Assert.Single(lanes);
            Assert.Equal(7f, lanes[0].X, 3);
            Assert.Equal(3f, lanes[0].Y, 3);
        }

        [Fact]
        public void SelectLanes_ZeroPorts_ReturnsEmpty()
        {
            var lanes = NavalPatrolPlanner.SelectLanes(Array.Empty<PortSignal>(), wanted: 2);

            Assert.Empty(lanes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void SelectLanes_WantedZeroOrLess_ReturnsEmpty_EvenWithPorts(int wanted)
        {
            var ports = new[] { new PortSignal(0f, 0f, threatIntensity: 1f, allyIntensity: 0f) };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted);

            Assert.Empty(lanes);
        }

        // ---- AnchorIndex: the dispatch point is the higher-net-threat port's own position,
        // never the lane's midpoint (M5, post-review; AC9) ----

        [Fact]
        public void SelectLanes_TwoPorts_AnchorIndexIsHigherNetThreatPort()
        {
            var ports = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 2f, allyIntensity: 0f),   // net 2, index 0
                new PortSignal(10f, 10f, threatIntensity: 3f, allyIntensity: 0f)  // net 3, index 1 (higher)
            };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 1);

            Assert.Equal(1, lanes[0].AnchorIndex);
        }

        [Fact]
        public void SelectLanes_ThreePorts_AnchorIndexIsHighestNetThreatPort_RegardlessOfArrayOrder()
        {
            var ports = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 5f, allyIntensity: 0f),     // net 5 (highest), index 0
                new PortSignal(100f, 100f, threatIntensity: 1f, allyIntensity: 1f), // net 0 (lowest), index 1
                new PortSignal(10f, 0f, threatIntensity: 4f, allyIntensity: 0f)     // net 4 (second highest), index 2
            };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 1);

            Assert.Equal(0, lanes[0].AnchorIndex);
        }

        [Fact]
        public void SelectLanes_SinglePortRealm_AnchorIndexIsThatPort()
        {
            var ports = new[] { new PortSignal(7f, 3f, threatIntensity: 1f, allyIntensity: 0f) };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 2);

            Assert.Equal(0, lanes[0].AnchorIndex);
        }

        [Fact]
        public void SelectLanes_RankingIsRelative_UniformlyScaledThreatVectorYieldsSameLane()
        {
            var ports = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 1f, allyIntensity: 0f),
                new PortSignal(4f, 0f, threatIntensity: 2f, allyIntensity: 0f),
                new PortSignal(8f, 0f, threatIntensity: 3f, allyIntensity: 0f)
            };
            var scaledPorts = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 100f, allyIntensity: 0f),
                new PortSignal(4f, 0f, threatIntensity: 200f, allyIntensity: 0f),
                new PortSignal(8f, 0f, threatIntensity: 300f, allyIntensity: 0f)
            };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 1);
            var scaledLanes = NavalPatrolPlanner.SelectLanes(scaledPorts, wanted: 1);

            Assert.Equal(lanes[0].X, scaledLanes[0].X, 3);
            Assert.Equal(lanes[0].Y, scaledLanes[0].Y, 3);
        }

        [Fact]
        public void SelectLanes_IsDeterministic_SameInputTwiceYieldsSameOutput()
        {
            var ports = new[]
            {
                new PortSignal(0f, 0f, threatIntensity: 2f, allyIntensity: 0f),
                new PortSignal(5f, 5f, threatIntensity: 2f, allyIntensity: 0f), // tie on net threat
                new PortSignal(9f, 1f, threatIntensity: 4f, allyIntensity: 0f)
            };

            var first = NavalPatrolPlanner.SelectLanes(ports, wanted: 2);
            var second = NavalPatrolPlanner.SelectLanes(ports, wanted: 2);

            Assert.Equal(first.Length, second.Length);
            for (int i = 0; i < first.Length; i++)
            {
                Assert.Equal(first[i].X, second[i].X, 3);
                Assert.Equal(first[i].Y, second[i].Y, 3);
            }
        }

        [Fact]
        public void SelectLanes_Wanted_CapsTheReturnedCount()
        {
            var ports = new[]
            {
                new PortSignal(0f, 0f, 4f, 0f),
                new PortSignal(1f, 0f, 3f, 0f),
                new PortSignal(2f, 0f, 2f, 0f),
                new PortSignal(3f, 0f, 1f, 0f)
            };

            var lanes = NavalPatrolPlanner.SelectLanes(ports, wanted: 1);

            Assert.Single(lanes);
        }

        // ---- PatrolCount (AC8 boundary conditions used by CommandBehavior, step 15/16) ----

        [Fact]
        public void PatrolCount_ZeroCapableLords_ReturnsZero()
        {
            Assert.Equal(0, NavalPatrolPlanner.PatrolCount(0, competence: 1f, maxPatrols: 5));
        }

        [Theory]
        [InlineData(1, 0f, 2)]
        [InlineData(5, 0.5f, 2)]
        [InlineData(10, 1f, 2)]
        [InlineData(100, 1f, 3)]
        public void PatrolCount_NeverExceedsMaxPatrols(int capableLords, float competence, int maxPatrols)
        {
            int count = NavalPatrolPlanner.PatrolCount(capableLords, competence, maxPatrols);
            Assert.True(count <= maxPatrols, $"PatrolCount({capableLords}, {competence}, {maxPatrols}) = {count} exceeds the cap");
        }

        [Fact]
        public void PatrolCount_IsNonDecreasing_InCompetence()
        {
            int low = NavalPatrolPlanner.PatrolCount(capableLords: 4, competence: 0.1f, maxPatrols: 5);
            int high = NavalPatrolPlanner.PatrolCount(capableLords: 4, competence: 0.9f, maxPatrols: 5);

            Assert.True(high >= low, "a more competent admiral should never run fewer patrols");
        }

        [Fact]
        public void PatrolCount_IsNonDecreasing_InCapableLords()
        {
            int few = NavalPatrolPlanner.PatrolCount(capableLords: 1, competence: 0.7f, maxPatrols: 5);
            int many = NavalPatrolPlanner.PatrolCount(capableLords: 8, competence: 0.7f, maxPatrols: 5);

            Assert.True(many >= few, "more naval-capable lords should never yield fewer patrols");
        }

        // ---- RankPatrolAddressees (post-ship behavioural fix, 2026-09-16) ----
        //
        // Defect: GenerateNavalOrders ranked its addressee pool by CandidateScore alone,
        // so a lord WarPostureBehavior already released to attack the same daily tick
        // (MobileParty.Objective == Aggressive) could also be handed a defensive patrol
        // lane - observed in a real log as the realm's own offense marshal being ordered
        // to patrol. Fix: defenders (non-aggressive) are ranked ahead of attackers
        // (aggressive) regardless of score; within each bucket, ranking is by score
        // descending, tie-broken by original index ascending. An attacker is only ever
        // used when there are not enough defending candidates to fill every lane.

        [Fact]
        public void RankPatrolAddressees_AllDefenders_KeepsPureScoreOrder()
        {
            var isAggressive = new[] { false, false, false };
            var score = new[] { 1f, 3f, 2f };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(new[] { 1, 2, 0 }, ranked);
        }

        [Fact]
        public void RankPatrolAddressees_AllAttackers_KeepsPureScoreOrder()
        {
            var isAggressive = new[] { true, true, true };
            var score = new[] { 1f, 3f, 2f };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(new[] { 1, 2, 0 }, ranked);
        }

        [Fact]
        public void RankPatrolAddressees_MixedSet_EveryDefenderRanksAheadOfEveryAttacker()
        {
            // Index 0: attacker, very high score. Index 1: defender, low score.
            // Index 2: attacker, mid score. Index 3: defender, high score.
            var isAggressive = new[] { true, false, true, false };
            var score = new[] { 100f, 1f, 50f, 90f };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(new[] { 3, 1, 0, 2 }, ranked);
        }

        [Fact]
        public void RankPatrolAddressees_HighScoringAttacker_StillLosesToLowScoringDefender()
        {
            var isAggressive = new[] { true, false };
            var score = new[] { 1000f, 0.01f };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(1, ranked[0]);
            Assert.Equal(0, ranked[1]);
        }

        [Fact]
        public void RankPatrolAddressees_IsDeterministic_SameInputTwiceYieldsSameOutput()
        {
            var isAggressive = new[] { true, false, false, true };
            var score = new[] { 5f, 5f, 3f, 3f };

            var first = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);
            var second = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(first, second);
        }

        [Fact]
        public void RankPatrolAddressees_EqualScores_BreakTiesByIndexAscending()
        {
            var isAggressive = new[] { false, false, false };
            var score = new[] { 4f, 4f, 4f };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(new[] { 0, 1, 2 }, ranked);
        }

        [Fact]
        public void RankPatrolAddressees_EmptyInput_ReturnsEmpty()
        {
            var ranked = NavalPatrolPlanner.RankPatrolAddressees(Array.Empty<bool>(), Array.Empty<float>());

            Assert.Empty(ranked);
        }

        [Fact]
        public void RankPatrolAddressees_ReturnedPermutation_ContainsEveryIndexExactlyOnce()
        {
            var isAggressive = new[] { true, false, true, false, false, true };
            var score = new[] { 2f, 9f, 4f, 4f, 1f, 7f };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score);

            Assert.Equal(isAggressive.Length, ranked.Length);
            var sorted = new int[ranked.Length];
            Array.Copy(ranked, sorted, ranked.Length);
            Array.Sort(sorted);
            for (int i = 0; i < sorted.Length; i++)
            {
                Assert.Equal(i, sorted[i]);
            }
        }

        // ---- PatrolCount over addressable lords only (T-0111) ----
        //
        // The lane count used to include army leaders, who RankPatrolAddressees never
        // ranks. Lanes beyond the pool were already dropped (assignCount caps at the
        // pool size), so no lane went unmanned; the effect of the fix is that the lane
        // count now scales with addressable lords only, i.e. fewer patrols at low
        // competence.

        [Theory]
        [InlineData(0f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(1f)]
        public void PatrolCount_NeverExceedsTheAddressableRanking(float competence)
        {
            var leadsArmy = new[] { true, false, false, true, false, false, false };
            var isAggressive = new[] { false, true, false, false, false, true, false };
            var score = new[] { 5f, 4f, 3f, 2f, 1f, 6f, 7f };

            int count = NavalPatrolPlanner.PatrolCount(leadsArmy, competence, maxPatrols: 10);
            int addressable = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score, leadsArmy).Length;

            Assert.True(count <= addressable, $"PatrolCount {count} exceeds {addressable} addressable lords");
        }

        [Fact]
        public void PatrolCount_ArmyLeadersDoNotCount_FourCandidatesTwoLeadingArmies_OneLaneNotTwo()
        {
            var leadsArmy = new[] { true, false, true, false };

            Assert.Equal(2, NavalPatrolPlanner.PatrolCount(4, competence: 0f, maxPatrols: 5));
            Assert.Equal(1, NavalPatrolPlanner.PatrolCount(leadsArmy, competence: 0f, maxPatrols: 5));
        }

        [Fact]
        public void PatrolCount_AllCandidatesLeadArmies_ReturnsZero()
        {
            Assert.Equal(0, NavalPatrolPlanner.PatrolCount(new[] { true, true }, competence: 1f, maxPatrols: 5));
        }

        [Fact]
        public void PatrolCount_NoArmyLeaders_MatchesTheCountOverload()
        {
            var leadsArmy = new[] { false, false, false, false };

            Assert.Equal(
                NavalPatrolPlanner.PatrolCount(4, 0.7f, 5),
                NavalPatrolPlanner.PatrolCount(leadsArmy, 0.7f, 5));
        }

        [Fact]
        public void PatrolCount_NullLeadsArmy_ReturnsZero()
        {
            Assert.Equal(0, NavalPatrolPlanner.PatrolCount((bool[])null, competence: 1f, maxPatrols: 5));
        }

        // ---- Army leaders are never addressed (log analysis 2026-11-03) ----
        //
        // Defect: the ranking handed army leaders patrol lanes, and dispatch drops every
        // such order the next hour (NavalPatrolDispatch DropLeadsArmy), leaving the lane
        // unmanned for the day. A lord leading an army cannot be addressed at all, so
        // the lane must go to the next lord who can actually sail it.

        [Fact]
        public void RankPatrolAddressees_ArmyLeader_IsNeverRanked_EvenAsTheBestDefender()
        {
            var isAggressive = new[] { false, false, true };
            var score = new[] { 9f, 1f, 5f };
            var leadsArmy = new[] { true, false, false };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score, leadsArmy);

            Assert.Equal(new[] { 1, 2 }, ranked);
        }

        [Fact]
        public void RankPatrolAddressees_OnlyArmyLeaders_ReturnsEmpty()
        {
            var isAggressive = new[] { false, true };
            var score = new[] { 3f, 4f };
            var leadsArmy = new[] { true, true };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score, leadsArmy);

            Assert.Empty(ranked);
        }

        [Fact]
        public void RankPatrolAddressees_NoArmyLeaders_MatchesTheUnfilteredRanking()
        {
            var isAggressive = new[] { true, false, true, false };
            var score = new[] { 100f, 1f, 50f, 90f };
            var leadsArmy = new[] { false, false, false, false };

            var ranked = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score, leadsArmy);

            Assert.Equal(NavalPatrolPlanner.RankPatrolAddressees(isAggressive, score), ranked);
        }
    }
}
