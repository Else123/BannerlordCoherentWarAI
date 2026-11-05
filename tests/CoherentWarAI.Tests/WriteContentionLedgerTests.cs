using System.Linq;
using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class WriteContentionLedgerTests
    {
        private const string Move = "Movement";

        private static WriteContentionLedger<string, string> NewLedger()
        {
            return new WriteContentionLedger<string, string>();
        }

        [Fact]
        public void WriteReleaseWrite_IsSequentialHandoff_NoContention()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "Command:Naval");
            ledger.Release("vidar", Move, "Command:Naval");
            ledger.Write("vidar", Move, "BanditHunt");

            Assert.Empty(ledger.Contentions());
        }

        [Fact]
        public void TwoWritersWithoutRelease_IsOneContention()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "Command:Naval");
            ledger.Write("vidar", Move, "BanditHunt");

            var contentions = ledger.Contentions();
            Assert.Single(contentions);
            Assert.Equal("vidar", contentions[0].Party);
            Assert.Equal(Move, contentions[0].Channel);
            Assert.Equal(new[] { "BanditHunt", "Command:Naval" }, contentions[0].Writers);
        }

        [Fact]
        public void ReleaseAfterOverlap_StillCounts()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Write("vidar", Move, "B");
            ledger.Release("vidar", Move, "A");

            Assert.Single(ledger.Contentions());
        }

        [Fact]
        public void SameSubsystemWritingRepeatedly_NoContention()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Write("vidar", Move, "A");
            ledger.Write("vidar", Move, "A");

            Assert.Empty(ledger.Contentions());
        }

        [Fact]
        public void ReleaseWithoutWrite_IsNoOp()
        {
            var ledger = NewLedger();
            ledger.Release("vidar", Move, "A");
            ledger.Write("vidar", Move, "B");

            Assert.Empty(ledger.Contentions());
            Assert.Equal(1, ledger.TrackedPartyCount);
        }

        [Fact]
        public void ReleaseByNonHolder_DoesNotFreeTheRealHolder()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Release("vidar", Move, "B");
            ledger.Write("vidar", Move, "B");

            Assert.Single(ledger.Contentions());
        }

        [Fact]
        public void LaterWriterOverlappingARemainingHolder_ContendsAfterAnotherReleased()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Release("vidar", Move, "A");
            ledger.Write("vidar", Move, "B");
            ledger.Write("vidar", Move, "C");

            var contentions = ledger.Contentions();
            Assert.Single(contentions);
            Assert.Equal(new[] { "B", "C" }, contentions[0].Writers);
        }

        [Fact]
        public void WriterRejoiningAfterRelease_WhileOtherHolds_IsStillOneContention()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Write("vidar", Move, "B");
            ledger.Release("vidar", Move, "A");
            ledger.Write("vidar", Move, "A");

            var contentions = ledger.Contentions();
            Assert.Single(contentions);
            Assert.Equal(new[] { "A", "B" }, contentions[0].Writers);
        }

        [Fact]
        public void BothReleasedThenNewWriter_IsNotAddedToEarlierContention()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Write("vidar", Move, "B");
            ledger.Release("vidar", Move, "A");
            ledger.Release("vidar", Move, "B");
            ledger.Write("vidar", Move, "C");

            var contentions = ledger.Contentions();
            Assert.Single(contentions);
            Assert.Equal(new[] { "A", "B" }, contentions[0].Writers);
        }

        [Fact]
        public void ReleaseOnNeverWrittenParty_CreatesNoEntry()
        {
            var ledger = NewLedger();
            ledger.Release("ghost", Move, "A");

            Assert.Equal(0, ledger.TrackedPartyCount);
        }

        [Fact]
        public void ReleaseWithEmptySubsystem_IsIgnored()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Release("vidar", Move, "");
            ledger.Release("vidar", Move, null);
            ledger.Write("vidar", Move, "B");

            Assert.Single(ledger.Contentions());
        }

        [Fact]
        public void WriterNames_AreOrdinalSorted_RegardlessOfJoinOrder()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "Zeta");
            ledger.Write("vidar", Move, "Alpha");

            Assert.Equal(new[] { "Alpha", "Zeta" }, ledger.Contentions()[0].Writers);
        }

        [Fact]
        public void ChannelsAndPartiesAreIndependent()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", "Objective", "Posture");
            ledger.Write("vidar", Move, "Command:Naval");
            ledger.Write("other", Move, "BanditHunt");

            Assert.Empty(ledger.Contentions());
            Assert.Equal(2, ledger.TrackedPartyCount);
        }

        [Fact]
        public void ReleasedParty_StaysTracked()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Release("vidar", Move, "A");

            Assert.Equal(1, ledger.TrackedPartyCount);
        }

        [Fact]
        public void Clear_ForgetsEverything_IncludingHolds()
        {
            var ledger = NewLedger();
            ledger.Write("vidar", Move, "A");
            ledger.Write("vidar", Move, "B");
            ledger.Clear();

            Assert.Empty(ledger.Contentions());
            Assert.Equal(0, ledger.TrackedPartyCount);

            // A's hold did not survive the reset, so B alone is no overlap.
            ledger.Write("vidar", Move, "B");
            Assert.Empty(ledger.Contentions());
        }

        [Fact]
        public void NullPartyOrEmptySubsystem_IsIgnored()
        {
            var ledger = NewLedger();
            ledger.Write(null, Move, "A");
            ledger.Write("vidar", Move, "");
            ledger.Write("vidar", Move, null);
            ledger.Release(null, Move, "A");

            Assert.Equal(0, ledger.TrackedPartyCount);
            Assert.Empty(ledger.Contentions().ToList());
        }
    }
}
