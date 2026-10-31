// API contract assumed by these tests (namespace CoherentWarAI.Logic, engine-free,
// System.* only). Hourly naval patrol dispatch decision for CommandBehavior.DispatchActiveOrders:
//
//   public enum NavalDispatchVerdict
//   {
//       Dispatch, Skip,
//       DropLeadsArmy, DropNoPortAnchor, DropAnchorNotFound, DropAnchorHasNoPort,
//       DropAnchorLost, DropNoRoute
//   }
//
//   public struct NavalDispatchFacts { bool LeadsArmy, HasPortAnchor, AnchorFound,
//       AnchorHasPort, AnchorOwnedByPartyFaction, HasNavalCapability, HasRoute; }
//
//   public static class NavalPatrolDispatch
//   {
//       public static NavalDispatchVerdict Decide(NavalDispatchFacts facts);
//       public static bool IsDrop(NavalDispatchVerdict verdict);
//   }
//
// Each test starts from an all-clear fact set and breaks exactly one fact, so a failing
// test names exactly one reason.

using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class NavalPatrolDispatchTests
    {
        private static NavalDispatchFacts AllClear() => new NavalDispatchFacts
        {
            LeadsArmy = false,
            HasPortAnchor = true,
            AnchorFound = true,
            AnchorHasPort = true,
            AnchorOwnedByPartyFaction = true,
            HasNavalCapability = true,
            HasRoute = true
        };

        [Fact]
        public void Decide_AllClear_Dispatches()
        {
            Assert.Equal(NavalDispatchVerdict.Dispatch, NavalPatrolDispatch.Decide(AllClear()));
        }

        [Fact]
        public void Decide_ArmyLeader_DropsSoArmyFieldsAreNeverTouched()
        {
            var facts = AllClear();
            facts.LeadsArmy = true;
            Assert.Equal(NavalDispatchVerdict.DropLeadsArmy, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_NoPortAnchor_Drops()
        {
            var facts = AllClear();
            facts.HasPortAnchor = false;
            Assert.Equal(NavalDispatchVerdict.DropNoPortAnchor, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_AnchorNotFound_Drops()
        {
            var facts = AllClear();
            facts.AnchorFound = false;
            Assert.Equal(NavalDispatchVerdict.DropAnchorNotFound, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_AnchorWithoutPort_Drops()
        {
            var facts = AllClear();
            facts.AnchorHasPort = false;
            Assert.Equal(NavalDispatchVerdict.DropAnchorHasNoPort, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_AnchorCapturedByOtherFaction_Drops()
        {
            var facts = AllClear();
            facts.AnchorOwnedByPartyFaction = false;
            Assert.Equal(NavalDispatchVerdict.DropAnchorLost, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_NoNavalCapability_SkipsAndKeepsOrder()
        {
            var facts = AllClear();
            facts.HasNavalCapability = false;
            Assert.Equal(NavalDispatchVerdict.Skip, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_NoRouteToAnchor_Drops()
        {
            var facts = AllClear();
            facts.HasRoute = false;
            Assert.Equal(NavalDispatchVerdict.DropNoRoute, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_RouteIsIgnoredWhenNotCapable_SkipWins()
        {
            // The connector only queries a route for a naval-capable party, so HasRoute is
            // false there by construction - that must not turn a temporary skip into a drop.
            var facts = AllClear();
            facts.HasNavalCapability = false;
            facts.HasRoute = false;
            Assert.Equal(NavalDispatchVerdict.Skip, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_ArmyLeaderWithBrokenAnchor_ReportsArmyFirst()
        {
            var facts = AllClear();
            facts.LeadsArmy = true;
            facts.AnchorFound = false;
            Assert.Equal(NavalDispatchVerdict.DropLeadsArmy, NavalPatrolDispatch.Decide(facts));
        }

        [Fact]
        public void Decide_BrokenAnchorWithoutShips_DropsInsteadOfSkipping()
        {
            // A permanently broken anchor is dropped even while the lord is temporarily
            // without ships - skipping would keep binding him to an undispatchable order.
            var facts = AllClear();
            facts.AnchorHasPort = false;
            facts.HasNavalCapability = false;
            Assert.Equal(NavalDispatchVerdict.DropAnchorHasNoPort, NavalPatrolDispatch.Decide(facts));
        }

        [Theory]
        [InlineData(NavalDispatchVerdict.Dispatch, false)]
        [InlineData(NavalDispatchVerdict.Skip, false)]
        [InlineData(NavalDispatchVerdict.DropLeadsArmy, true)]
        [InlineData(NavalDispatchVerdict.DropNoPortAnchor, true)]
        [InlineData(NavalDispatchVerdict.DropAnchorNotFound, true)]
        [InlineData(NavalDispatchVerdict.DropAnchorHasNoPort, true)]
        [InlineData(NavalDispatchVerdict.DropAnchorLost, true)]
        [InlineData(NavalDispatchVerdict.DropNoRoute, true)]
        public void IsDrop_TrueOnlyForDropVerdicts(NavalDispatchVerdict verdict, bool expected)
        {
            Assert.Equal(expected, NavalPatrolDispatch.IsDrop(verdict));
        }
    }
}
