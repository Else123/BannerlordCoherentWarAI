using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class SightingNetworkTests
    {
        private const float Unit = 100f;   // typical distance between neighbouring towns

        // relaySpeed 0.5 -> word covers half that distance per hour
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(1f, 50f)]
        [InlineData(4f, 200f)]
        [InlineData(-1f, 0f)]
        public void SpreadRadius_WordTravelsOutwardOverTime(float hours, float expected)
        {
            Assert.Equal(expected, SightingNetwork.SpreadRadius(
                hours, SightingNetwork.DefaultRelaySpeed, Unit), 3);
        }

        [Fact]
        public void NewsReachesNeighboursBeforeTheFarSideOfTheRealm()
        {
            // A castle 40 units away hears within the hour; one 300 away does not.
            Assert.True(SightingNetwork.HasReached(40f, 1f, SightingNetwork.DefaultRelaySpeed, Unit));
            Assert.False(SightingNetwork.HasReached(300f, 1f, SightingNetwork.DefaultRelaySpeed, Unit));

            // Given a day, word gets there too.
            Assert.True(SightingNetwork.HasReached(300f, 24f, SightingNetwork.DefaultRelaySpeed, Unit));
        }

        [Fact]
        public void WhoeverSawItKnowsImmediately()
        {
            Assert.True(SightingNetwork.HasReached(0f, 0f, SightingNetwork.DefaultRelaySpeed, Unit));
        }

        [Theory]
        [InlineData(0f, 36f, 1f)]
        [InlineData(18f, 36f, 0.5f)]
        [InlineData(36f, 36f, 0f)]      // old news
        [InlineData(100f, 36f, 0f)]
        [InlineData(5f, 0f, 0f)]        // disabled
        public void Freshness_FadesWithAge(float hours, float lifetime, float expected)
        {
            Assert.Equal(expected, SightingNetwork.Freshness(hours, lifetime), 4);
        }

        // reach 200, lifetime 36
        [Theory]
        [InlineData(1000f, 0f, 1000f)]      // at the gates, just seen
        [InlineData(1000f, 100f, 500f)]     // half the reach away
        [InlineData(1000f, 200f, 0f)]       // out of reach
        [InlineData(1000f, 400f, 0f)]
        [InlineData(0f, 50f, 0f)]           // nothing there
        public void ThreatToPlace_WeighsSizeAgainstDistance(
            float enemyStrength, float distance, float expected)
        {
            Assert.Equal(expected, SightingNetwork.ThreatToPlace(
                enemyStrength, distance, 200f, 0f, 36f), 3);
        }

        [Fact]
        public void AStaleReportIsWorthNothingHoweverLargeTheForce()
        {
            Assert.Equal(0f, SightingNetwork.ThreatToPlace(50000f, 10f, 200f, 40f, 36f), 4);
        }

        // typical 400, maxBoost 1.5
        [Theory]
        [InlineData(0f, 1f)]         // nothing reported
        [InlineData(400f, 1f)]       // an ordinary day
        [InlineData(800f, 1.75f)]    // twice the usual -> half the boost
        [InlineData(1200f, 2f)]      // three times -> two thirds
        [InlineData(100000f, 2.494f)]  // saturates toward the cap rather than dominating
        public void DefensiveUrgency_ShiftsDefendersWithoutOverwhelmingEverythingElse(
            float reportedThreat, float expected)
        {
            Assert.Equal(expected, SightingNetwork.DefensiveUrgency(reportedThreat, 400f, 1.5f), 3);
        }

        [Fact]
        public void UrgencyIsBounded()
        {
            // However enormous the reported host, the pull stays finite - defenders
            // should converge on a threat, not abandon everywhere else entirely.
            float huge = SightingNetwork.DefensiveUrgency(1000000f, 400f, 1.5f);
            Assert.True(huge <= 2.5f, $"urgency {huge} must stay bounded");
        }

        [Fact]
        public void ARealmLearnsOfAnInvasionOutwardFromWhereItWasSeen()
        {
            // The point of the whole thing: at one hour the border castle knows and
            // the capital does not; by the next day both do.
            const float border = 40f;
            const float capital = 260f;

            Assert.True(SightingNetwork.HasReached(border, 1f, SightingNetwork.DefaultRelaySpeed, Unit));
            Assert.False(SightingNetwork.HasReached(capital, 1f, SightingNetwork.DefaultRelaySpeed, Unit));

            Assert.True(SightingNetwork.HasReached(border, 12f, SightingNetwork.DefaultRelaySpeed, Unit));
            Assert.True(SightingNetwork.HasReached(capital, 12f, SightingNetwork.DefaultRelaySpeed, Unit));
        }

        // lifetime 240h, penalty 0.55 -> floor 0.45
        [Theory]
        [InlineData(0f, 1f)]        // seen just now
        [InlineData(60f, 0.8625f)]  // fading
        [InlineData(120f, 0.725f)]  // half forgotten
        [InlineData(240f, 0.45f)]   // out of mind
        [InlineData(999f, 0.45f)]   // long out of mind, no worse than never
        [InlineData(-1f, 0.45f)]    // never seen at all
        public void KnowledgeWeight_DecaysFromCertainToTheFloor(float hoursSince, float expected)
        {
            Assert.Equal(expected, SightingNetwork.KnowledgeWeight(false, hoursSince, 240f, 0.55f), 4);
        }

        [Fact]
        public void LandNextToOursCountsAsKnownHoweverLongAgoItWasSeen()
        {
            // A realm does not need a scout to know what its own border villages
            // face. Without this the AI would refuse to attack the one fief it has
            // the most reason to want.
            Assert.Equal(1f, SightingNetwork.KnowledgeWeight(true, -1f, 240f, 0.55f), 4);
            Assert.Equal(1f, SightingNetwork.KnowledgeWeight(true, 5000f, 240f, 0.55f), 4);
        }

        [Fact]
        public void KnowledgeNeverRemovesATargetEntirely()
        {
            // Even at the harshest setting the weight stays positive: an unseen
            // fief should rank last, not become unreachable, or the AI could never
            // expand into ground it has no presence near.
            float harshest = SightingNetwork.KnowledgeWeight(false, -1f, 240f, 1f);
            Assert.True(harshest >= 0f, $"weight {harshest} must not go negative");

            Assert.Equal(1f, SightingNetwork.KnowledgeWeight(false, -1f, 240f, 0f), 4);
        }

        [Fact]
        public void AbsurdPenaltiesAreClampedRatherThanInverted()
        {
            // Settings are bounded in MCM, but the logic is the layer that has to
            // hold: a penalty above 1 would otherwise make the floor negative and
            // an unseen target score below zero, flipping its ranking.
            Assert.Equal(0f, SightingNetwork.KnowledgeWeight(false, -1f, 240f, 4f), 4);
            Assert.Equal(1f, SightingNetwork.KnowledgeWeight(false, -1f, 240f, -2f), 4);
        }
    }
}
