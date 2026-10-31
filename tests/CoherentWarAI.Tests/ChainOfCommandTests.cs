// API contract assumed by these tests (namespace CoherentWarAI.Logic, engine-free,
// System.* only). This is the hand-off contract for the implementer of
// Logic/ChainOfCommand.cs (Slice-1 step 10). Field/member names and semantics are taken
// verbatim from COMMAND-SPEC.md SS3.1/3.2/3.3 where the spec fixes them; where the spec is
// silent (marked ASSUMPTION below) the simplest shape was chosen and is documented here.
//
//   [Flags]
//   public enum Domain { None = 0, Defense = 1, Offense = 2, Naval = 4, All = Defense | Offense | Naval }
//
//   public enum OrderScope { Realm, Domain, Lord }               // SPEC SS3.1 (three values, not two)
//   public enum OrderVerb { Defend, Patrol, Besiege, Raid, JoinArmy, HuntBandits }
//   public enum Bindingness { Recommendation, Directive }
//   public enum Provenance { AiMarshal, AiKing, Player }
//
//   public struct Order
//   {
//       public string IssuerId;            // Hero.StringId of king or marshal; "" for a doctrine default
//       public OrderScope Scope;
//       public string AddresseeId;         // Hero.StringId when Scope == Lord; else ""
//       public Domain DomainScope;         // single flag, never All
//       public OrderVerb Verb;
//       public string TargetSettlementId;  // "" when the target is a point
//       public float TargetX, TargetY;
//       public bool IsNaval;
//       public bool TargetIsPort;
//       public Bindingness Bindingness;
//       public int Priority;               // 1 routine .. 3 urgent
//       public float IssuedAtHours;
//       public float ExpiresAtHours;
//       public Provenance Provenance;
//
//       public bool IsActive(float nowHours);   // nowHours < ExpiresAtHours
//       public bool IsBinding(float nowHours);  // Bindingness == Directive && IsActive(nowHours)
//                                                // ASSUMPTION: IsBinding takes nowHours as a parameter
//                                                // (spec calls it a "derived member" without a fixed
//                                                // signature; without nowHours an Order could never know
//                                                // whether it has expired). A Recommendation is never
//                                                // binding regardless of nowHours.
//       public bool Supersedes(Order other);    // total order per SPEC SS3.1:
//                                                //   1. Provenance.Player beats any AI provenance
//                                                //   2. issuer rank: AiKing beats AiMarshal
//                                                //      (Player is its own top rank, already covered by 1)
//                                                //   3. Scope precision: Lord beats Domain beats Realm
//                                                //   4. Priority descending
//                                                //   5. IssuedAtHours descending (newer wins)
//   }
//
//   public struct DelegationEntry { public Domain Domain; public string HolderId; public float GrantedAtHours; }
//
//   public sealed class DelegationTable
//   {
//       public string KingId { get; }
//       public bool NavalDelegatedSeparately { get; }
//       public System.Collections.Generic.IReadOnlyList<DelegationEntry> Entries { get; }
//           // ASSUMPTION: spec lists only HolderFor/IsMarshal/CanIssue/With* as the public surface;
//           // an inspectable Entries list is added so "at most one holder per Domain, no second
//           // entry on replace" is testable without exposing internal storage assumptions.
//
//       public DelegationTable(string kingId, bool navalDelegatedSeparately);
//           // ASSUMPTION: constructor shape; starts with zero entries.
//
//       public string HolderFor(Domain domain);           // "" == reclaimed / king holds it
//       public bool IsMarshal(string heroId);              // holds >= 1 domain and heroId != KingId
//       public bool CanIssue(string issuerId, string targetLordId, Domain domain);
//           // true iff issuerId == KingId, or issuerId == HolderFor(domain).
//           // targetLordId is accepted per SPEC SS3.3's signature but the authority model in SS3.3
//           // does not filter by which lord is addressed - only by domain holdership - so it is not
//           // otherwise consulted by CanIssue itself (documented ASSUMPTION, matches AC5's wording
//           // "bound by his entries and not by rank").
//       public DelegationTable WithDelegation(Domain domain, string holderId, float nowHours);
//           // pure: returns a new table; if domain already has a holder, replaces that single entry
//           // (no second entry for the same domain).
//       public DelegationTable WithReclaim(Domain domain, float nowHours);
//           // pure: returns a new table with that domain's entry removed (HolderFor == "" afterwards).
//       public DelegationTable Prune(System.Func<string, bool> resolves);
//           // ASSUMPTION (ghost-holder rule, AC4): pure; returns a new table with every entry whose
//           // HolderId no longer resolves (resolves(HolderId) == false) removed. The connector calls
//           // this with a "does this StringId still resolve to a live Hero in this kingdom" predicate.
//   }
//
//   public static class ChainOfCommandFunctions
//   {
//       // ASSUMPTION: the spec writes DomainOf as a free pure function ("Domain
//       // DomainOf(OrderVerb, bool, bool)", SS3.2) without naming its containing type.
//       // Housed as a static method on a static class alongside Order/DelegationTable in
//       // the same file, matching this codebase's convention of one static planner class
//       // per Logic file (MarshalPlanner, ScoutingQuality, PosturePlanner, ...).
//       public static Domain DomainOf(OrderVerb verb, bool isNaval, bool navalDelegatedSeparately);
//           // naval Patrol/Defend -> Naval when navalDelegatedSeparately, else Defense
//           // naval Raid                -> Naval when navalDelegatedSeparately, else Offense
//           // land  Defend/Patrol/HuntBandits -> Defense
//           // land  Besiege/Raid/JoinArmy     -> Offense
//   }

using System;
using System.Linq;
using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class ChainOfCommandTests
    {
        // ---- DelegationTable: one-holder-per-domain invariant (AC4) ----

        [Fact]
        public void WithDelegation_GrantsHolderForThatDomainOnly()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Offense, "marshal1", nowHours: 0f);

            Assert.Equal("marshal1", table.HolderFor(Domain.Offense));
            Assert.Equal("", table.HolderFor(Domain.Defense));
            Assert.Equal("", table.HolderFor(Domain.Naval));
        }

        [Fact]
        public void OneHolder_MayHoldSeveralDomains()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Offense, "marshal1", 0f)
                .WithDelegation(Domain.Naval, "marshal1", 0f);

            Assert.Equal("marshal1", table.HolderFor(Domain.Offense));
            Assert.Equal("marshal1", table.HolderFor(Domain.Naval));
        }

        [Fact]
        public void WithDelegation_OnAlreadyHeldDomain_ReplacesHolder_NoSecondEntry()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "marshal1", 0f)
                .WithDelegation(Domain.Naval, "marshal2", 10f);

            Assert.Equal("marshal2", table.HolderFor(Domain.Naval));
            Assert.Single(table.Entries.Where(e => e.Domain == Domain.Naval));
        }

        [Fact]
        public void WithReclaim_LeavesHolderForEmpty()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Defense, "marshal1", 0f)
                .WithReclaim(Domain.Defense, 5f);

            Assert.Equal("", table.HolderFor(Domain.Defense));
        }

        [Fact]
        public void Prune_DropsAHolderIdThatNoLongerResolves()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Defense, "deadHero", 0f)
                .WithDelegation(Domain.Naval, "aliveHero", 0f);

            var pruned = table.Prune(id => id == "aliveHero");

            Assert.Equal("", pruned.HolderFor(Domain.Defense));
            Assert.Equal("aliveHero", pruned.HolderFor(Domain.Naval));
        }

        [Fact]
        public void WithDelegation_IsImmutable_OriginalTableUnchanged()
        {
            var original = new DelegationTable("king", navalDelegatedSeparately: true);
            var withGrant = original.WithDelegation(Domain.Offense, "marshal1", 0f);

            Assert.Equal("", original.HolderFor(Domain.Offense));
            Assert.Equal("marshal1", withGrant.HolderFor(Domain.Offense));
        }

        [Fact]
        public void WithReclaim_IsImmutable_OriginalTableUnchanged()
        {
            var granted = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Offense, "marshal1", 0f);
            var reclaimed = granted.WithReclaim(Domain.Offense, 5f);

            Assert.Equal("marshal1", granted.HolderFor(Domain.Offense));
            Assert.Equal("", reclaimed.HolderFor(Domain.Offense));
        }

        // ---- CanIssue matrix (AC5) ----

        [Fact]
        public void CanIssue_King_AnyLordAnyDomain_IsTrue()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "marshal1", 0f);

            Assert.True(table.CanIssue("king", "anyLord", Domain.Naval));
            Assert.True(table.CanIssue("king", "anyLord", Domain.Defense));
            Assert.True(table.CanIssue("king", "anyLord", Domain.Offense));
        }

        [Fact]
        public void CanIssue_MarshalHoldingOnlyNaval_CannotIssueDefense_EvenToALordHeCommandsNavally()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "admiral", 0f);

            Assert.False(table.CanIssue("admiral", "lordUnderNavalCommand", Domain.Defense));
        }

        [Fact]
        public void CanIssue_MarshalIsTrue_OnlyForDomainsHeHolds()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "admiral", 0f)
                .WithDelegation(Domain.Offense, "warlord", 0f);

            Assert.True(table.CanIssue("admiral", "someLord", Domain.Naval));
            Assert.False(table.CanIssue("admiral", "someLord", Domain.Offense));
            Assert.True(table.CanIssue("warlord", "someLord", Domain.Offense));
            Assert.False(table.CanIssue("warlord", "someLord", Domain.Naval));
        }

        [Fact]
        public void CanIssue_PlainLord_IsAlwaysFalse()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "admiral", 0f);

            Assert.False(table.CanIssue("plainLord", "someLord", Domain.Naval));
            Assert.False(table.CanIssue("plainLord", "someLord", Domain.Defense));
            Assert.False(table.CanIssue("plainLord", "someLord", Domain.Offense));
        }

        // ---- CanIssue: empty/null issuer must never match an empty KingId or an empty
        // HolderFor (a reclaimed domain) - M3 fix (post-review) ----

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void CanIssue_EmptyOrNullIssuer_OnTableWithReclaimedDomain_IsFalse(string issuerId)
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "marshal1", 0f)
                .WithReclaim(Domain.Naval, 5f);

            Assert.False(table.CanIssue(issuerId, "someLord", Domain.Naval));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void CanIssue_EmptyOrNullIssuer_OnTableWithEmptyKingId_IsFalse(string issuerId)
        {
            var table = new DelegationTable("", navalDelegatedSeparately: true);

            Assert.False(table.CanIssue(issuerId, "someLord", Domain.Naval));
        }

        // ---- DelegationTable: multi-flag Domain expands to one entry per set flag - M4
        // fix (post-review) ----

        [Fact]
        public void WithDelegation_AllDomain_ExpandsToOneEntryPerFlag()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.All, "x", 0f);

            Assert.Equal("x", table.HolderFor(Domain.Naval));
            Assert.Equal("x", table.HolderFor(Domain.Defense));
            Assert.Equal("x", table.HolderFor(Domain.Offense));
            Assert.Equal(3, table.Entries.Count);
            Assert.True(table.IsMarshal("x"));
        }

        [Fact]
        public void WithReclaim_MultiFlagDomain_OnlyClearsThoseFlags()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.All, "x", 0f)
                .WithReclaim(Domain.Defense | Domain.Offense, 5f);

            Assert.Single(table.Entries);
            Assert.Equal("x", table.HolderFor(Domain.Naval));
            Assert.Equal("", table.HolderFor(Domain.Defense));
            Assert.Equal("", table.HolderFor(Domain.Offense));
        }

        [Fact]
        public void HolderFor_AllDomain_ThrowsBecauseItIsNotASingleFlag()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true);
            Assert.Throws<ArgumentException>(() => table.HolderFor(Domain.All));
        }

        [Fact]
        public void HolderFor_NoneDomain_ThrowsBecauseItIsNotASingleFlag()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true);
            Assert.Throws<ArgumentException>(() => table.HolderFor(Domain.None));
        }

        // ---- IsMarshal (AC5) ----

        [Fact]
        public void IsMarshal_TrueIffHoldsAtLeastOneDomainAndIsNotKing()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "admiral", 0f);

            Assert.True(table.IsMarshal("admiral"));
            Assert.False(table.IsMarshal("plainLord"));
            Assert.False(table.IsMarshal("king"));
        }

        [Fact]
        public void IsMarshal_KingHoldingADomainOfHimself_IsStillNotAMarshal()
        {
            var table = new DelegationTable("king", navalDelegatedSeparately: true)
                .WithDelegation(Domain.Naval, "king", 0f);

            Assert.False(table.IsMarshal("king"));
        }

        // ---- Order.IsActive / IsBinding (AC7) ----

        private static Order MakeOrder(Bindingness bindingness, float expiresAtHours) => new Order
        {
            IssuerId = "king",
            Scope = OrderScope.Lord,
            AddresseeId = "lord1",
            DomainScope = Domain.Naval,
            Verb = OrderVerb.Patrol,
            TargetSettlementId = "",
            TargetX = 10f,
            TargetY = 20f,
            IsNaval = true,
            TargetIsPort = false,
            Bindingness = bindingness,
            Priority = 1,
            IssuedAtHours = 0f,
            ExpiresAtHours = expiresAtHours,
            Provenance = Provenance.AiKing
        };

        [Fact]
        public void Order_IsActive_TrueBeforeExpiry()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            Assert.True(order.IsActive(24f));
        }

        [Fact]
        public void Order_IsActive_FalseOncePastExpiresAtHours()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            Assert.False(order.IsActive(49f));
        }

        [Fact]
        public void Order_Recommendation_NeverBinding()
        {
            var order = MakeOrder(Bindingness.Recommendation, expiresAtHours: 48f);
            Assert.False(order.IsBinding(24f));
        }

        [Fact]
        public void Order_Directive_IsBindingWhileActive()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            Assert.True(order.IsBinding(24f));
        }

        [Fact]
        public void Order_Directive_NotBindingOnceExpired()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            Assert.False(order.IsBinding(49f));
        }

        // ---- Order.HasPortAnchor (naval patrol dispatch guard) ----
        // Vanilla's lord naval patrol (MobilePartyAi.GetNavalPatrolBehavior, NavalDLC
        // GetPatrolRadius) dereferences TargetSettlement, so a naval patrol may only be
        // dispatched when it names the port it is anchored to.

        [Fact]
        public void Order_HasPortAnchor_FalseForPointOnlyNavalPatrol()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            Assert.False(order.HasPortAnchor);
        }

        [Fact]
        public void Order_HasPortAnchor_TrueWhenSettlementNamedAndTargetingPort()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            order.TargetSettlementId = "town_EN1";
            order.TargetIsPort = true;
            Assert.True(order.HasPortAnchor);
        }

        [Fact]
        public void Order_HasPortAnchor_FalseWhenSettlementNamedButNotTargetingPort()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            order.TargetSettlementId = "town_EN1";
            order.TargetIsPort = false;
            Assert.False(order.HasPortAnchor);
        }

        [Fact]
        public void Order_HasPortAnchor_FalseWhenTargetingPortWithoutSettlement()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            order.TargetSettlementId = null;
            order.TargetIsPort = true;
            Assert.False(order.HasPortAnchor);
        }

        [Fact]
        public void Order_HasPortAnchor_FalseWhenTargetingPortWithEmptySettlement()
        {
            var order = MakeOrder(Bindingness.Directive, expiresAtHours: 48f);
            order.TargetSettlementId = "";
            order.TargetIsPort = true;
            Assert.False(order.HasPortAnchor);
        }

        // ---- Order.Supersedes total ordering (AC7) ----

        private static Order MakeOrderFor(Provenance provenance, OrderScope scope, int priority, float issuedAtHours, Domain domainScope = Domain.Defense) => new Order
        {
            IssuerId = "someone",
            Scope = scope,
            AddresseeId = "lord1",
            DomainScope = domainScope,
            Verb = OrderVerb.Defend,
            TargetSettlementId = "",
            TargetX = 0f,
            TargetY = 0f,
            IsNaval = false,
            TargetIsPort = false,
            Bindingness = Bindingness.Directive,
            Priority = priority,
            IssuedAtHours = issuedAtHours,
            ExpiresAtHours = 1000f,
            Provenance = provenance
        };

        [Fact]
        public void Supersedes_PlayerOrder_BeatsAiKingOrder()
        {
            var player = MakeOrderFor(Provenance.Player, OrderScope.Domain, 1, 0f);
            var aiKing = MakeOrderFor(Provenance.AiKing, OrderScope.Domain, 3, 100f);

            Assert.True(player.Supersedes(aiKing));
            Assert.False(aiKing.Supersedes(player));
        }

        [Fact]
        public void Supersedes_PlayerOrder_BeatsAiMarshalOrder()
        {
            var player = MakeOrderFor(Provenance.Player, OrderScope.Domain, 1, 0f);
            var aiMarshal = MakeOrderFor(Provenance.AiMarshal, OrderScope.Lord, 3, 100f);

            Assert.True(player.Supersedes(aiMarshal));
            Assert.False(aiMarshal.Supersedes(player));
        }

        [Fact]
        public void Supersedes_NoAiOrder_EverSupersedesAPlayerOrder()
        {
            var player = MakeOrderFor(Provenance.Player, OrderScope.Realm, 1, 0f);
            var aiKing = MakeOrderFor(Provenance.AiKing, OrderScope.Lord, 3, 100f);
            var aiMarshal = MakeOrderFor(Provenance.AiMarshal, OrderScope.Lord, 3, 100f);

            Assert.False(aiKing.Supersedes(player));
            Assert.False(aiMarshal.Supersedes(player));
        }

        [Fact]
        public void Supersedes_King_BeatsMarshal_AtEqualScopePriorityAndTime()
        {
            var king = MakeOrderFor(Provenance.AiKing, OrderScope.Domain, 1, 0f);
            var marshal = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 0f);

            Assert.True(king.Supersedes(marshal));
            Assert.False(marshal.Supersedes(king));
        }

        [Fact]
        public void Supersedes_LordScope_BeatsDomainScope_AtEqualRankPriorityAndTime()
        {
            var lordScoped = MakeOrderFor(Provenance.AiMarshal, OrderScope.Lord, 1, 0f);
            var domainScoped = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 0f);

            Assert.True(lordScoped.Supersedes(domainScoped));
            Assert.False(domainScoped.Supersedes(lordScoped));
        }

        [Fact]
        public void Supersedes_HigherPriority_BeatsLowerPriority_AtEqualRankAndScope()
        {
            var urgent = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 3, 0f);
            var routine = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 0f);

            Assert.True(urgent.Supersedes(routine));
            Assert.False(routine.Supersedes(urgent));
        }

        [Fact]
        public void Supersedes_NewerOrder_BeatsOlderOrder_AtEqualRankScopeAndPriority()
        {
            var newer = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 50f);
            var older = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 10f);

            Assert.True(newer.Supersedes(older));
            Assert.False(older.Supersedes(newer));
        }

        [Fact]
        public void Supersedes_IsAntisymmetricForDistinctOrders()
        {
            var a = MakeOrderFor(Provenance.AiKing, OrderScope.Lord, 2, 30f);
            var b = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 10f);

            Assert.False(a.Supersedes(b) && b.Supersedes(a));
        }

        [Fact]
        public void Supersedes_IsTransitiveAcrossThreeDifferentTiers()
        {
            // Wins purely on provenance (Player beats AiMarshal), regardless of its
            // weaker scope/priority.
            var byProvenance = MakeOrderFor(Provenance.Player, OrderScope.Realm, 1, 0f);
            // Loses provenance to byProvenance, but would beat byPriority on scope alone.
            var byScope = MakeOrderFor(Provenance.AiMarshal, OrderScope.Lord, 1, 0f);
            // Loses scope to byScope.
            var byPriority = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 3, 0f);

            Assert.True(byProvenance.Supersedes(byScope));
            Assert.True(byScope.Supersedes(byPriority));
            Assert.True(byProvenance.Supersedes(byPriority));
        }

        [Fact]
        public void Supersedes_OrdersDifferingOnlyInDomainScope_TieInBothDirections()
        {
            // Characterisation test (AC8, Slice 2a step 1): Supersedes is a total
            // comparator with no domain dimension, so two orders identical in
            // Provenance/Scope/Priority/IssuedAtHours but differing only in DomainScope
            // must tie in BOTH directions - it cannot arbitrate between domains, which is
            // exactly why the order store must bucket by (addressee, domain) instead.
            // Green before and after the Slice 2a rekey by design.
            var naval = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 0f, Domain.Naval);
            var defense = MakeOrderFor(Provenance.AiMarshal, OrderScope.Domain, 1, 0f, Domain.Defense);

            Assert.False(naval.Supersedes(defense));
            Assert.False(defense.Supersedes(naval));
        }

        // ---- DomainOf (SPEC SS3.2) ----

        [Theory]
        [InlineData(OrderVerb.Patrol, true, true, Domain.Naval)]
        [InlineData(OrderVerb.Patrol, true, false, Domain.Defense)]
        [InlineData(OrderVerb.Defend, true, true, Domain.Naval)]
        [InlineData(OrderVerb.Defend, true, false, Domain.Defense)]
        [InlineData(OrderVerb.Raid, true, true, Domain.Naval)]
        [InlineData(OrderVerb.Raid, true, false, Domain.Offense)]
        [InlineData(OrderVerb.Defend, false, true, Domain.Defense)]
        [InlineData(OrderVerb.Defend, false, false, Domain.Defense)]
        [InlineData(OrderVerb.Patrol, false, true, Domain.Defense)]
        [InlineData(OrderVerb.HuntBandits, false, true, Domain.Defense)]
        [InlineData(OrderVerb.Besiege, false, true, Domain.Offense)]
        [InlineData(OrderVerb.Raid, false, false, Domain.Offense)]
        [InlineData(OrderVerb.JoinArmy, false, true, Domain.Offense)]
        public void DomainOf_MapsVerbAndNavalFlagToDomain(
            OrderVerb verb, bool isNaval, bool navalDelegatedSeparately, Domain expected)
        {
            Assert.Equal(expected, ChainOfCommandFunctions.DomainOf(verb, isNaval, navalDelegatedSeparately));
        }

        // ---- DomainKey<T> (M2, post-review: moved from a private struct inside
        // Behaviors/CommandBehavior.cs into this engine-free file so it gets offline
        // coverage - it was the slice's headline change with zero prior tests). A plain
        // local reference type stands in for Hero/MobileParty, which this layer cannot
        // reference. ----

        private class TestOwner
        {
        }

        [Fact]
        public void DomainKey_SameOwner_DifferentDomain_AreDistinct()
        {
            var owner = new TestOwner();
            var naval = new DomainKey<TestOwner>(owner, Domain.Naval);
            var defense = new DomainKey<TestOwner>(owner, Domain.Defense);

            Assert.False(naval.Equals(defense));
            Assert.False(defense.Equals(naval));
        }

        [Fact]
        public void DomainKey_DifferentOwners_SameDomain_AreDistinct()
        {
            var a = new DomainKey<TestOwner>(new TestOwner(), Domain.Naval);
            var b = new DomainKey<TestOwner>(new TestOwner(), Domain.Naval);

            Assert.False(a.Equals(b));
        }

        [Fact]
        public void DomainKey_SameOwnerAndDomain_AreEqual_AndHaveEqualHashCodes()
        {
            var owner = new TestOwner();
            var a = new DomainKey<TestOwner>(owner, Domain.Offense);
            var b = new DomainKey<TestOwner>(owner, Domain.Offense);

            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void DomainKey_NullOwner_IsHandledWithoutThrowing()
        {
            var a = new DomainKey<TestOwner>(null, Domain.Naval);
            var b = new DomainKey<TestOwner>(null, Domain.Naval);

            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void DomainKey_EqualsObject_AgreesWithEqualsDomainKey()
        {
            var owner = new TestOwner();
            var a = new DomainKey<TestOwner>(owner, Domain.Defense);
            var sameBucket = new DomainKey<TestOwner>(owner, Domain.Defense);
            var otherBucket = new DomainKey<TestOwner>(owner, Domain.Naval);

            Assert.Equal(a.Equals(sameBucket), a.Equals((object)sameBucket));
            Assert.Equal(a.Equals(otherBucket), a.Equals((object)otherBucket));
            Assert.False(a.Equals("not a DomainKey"));
        }
    }
}
