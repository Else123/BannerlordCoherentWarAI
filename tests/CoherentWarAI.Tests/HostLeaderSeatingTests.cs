using System.Collections.Generic;
using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    /// <summary>
    /// T-0115: a lord already leading a host ranks first - for the day's offensive
    /// slots and for the marshal posts - so the realm never runs a host the doctrine
    /// does not count. The slot count itself is never raised for him.
    /// </summary>
    public class HostLeaderSeatingTests
    {
        private sealed class Lord
        {
            public Lord(string name, bool leadsHost, float score, bool hostIsDefender = false)
            {
                Name = name;
                LeadsHost = leadsHost;
                Score = score;
                HostIsDefender = hostIsDefender;
            }

            public string Name { get; }
            public bool LeadsHost { get; }
            public float Score { get; }
            public bool HostIsDefender { get; }
        }

        /// <summary>
        /// One realm's day through the production helpers: offensive ranking, the
        /// plain slot allowance, then the marshal posts. Returns the attackers in
        /// post order and how many of them (from the front) are marshals.
        /// </summary>
        private static (List<Lord> Attackers, int Marshals) PlanDay(List<Lord> lords, int aggressiveSlots, int maxMarshals)
        {
            var scores = new List<float>();
            foreach (Lord lord in lords)
            {
                scores.Add(lord.Score);
            }
            PosturePlanner.RankForOffense(lords, scores, l => PosturePlanner.TakesSlotFirst(l.LeadsHost, l.HostIsDefender));

            var attackers = new List<Lord>();
            for (int rank = 0; rank < lords.Count; rank++)
            {
                if (PosturePlanner.DecidePosture(rank, aggressiveSlots) == Posture.Aggressive)
                {
                    attackers.Add(lords[rank]);
                }
            }

            int marshalCount = MarshalPlanner.MarshalCount(attackers.Count, MarshalPlanner.DefaultSlotsPerMarshal, maxMarshals);
            int marshals = MarshalPlanner.RankForPosts(attackers, l => l.LeadsHost, l => l.Score, marshalCount);
            return (attackers, marshals);
        }

        [Fact]
        public void CompareForOffense_HostLeaderRanksAheadOfAStrongerLord()
        {
            Assert.True(PosturePlanner.CompareForOffense(true, 10f, false, 500f) < 0);
            Assert.True(PosturePlanner.CompareForOffense(false, 500f, true, 10f) > 0);
        }

        [Fact]
        public void CompareForOffense_WithinTheSameGroupHigherScoreRanksFirst()
        {
            Assert.True(PosturePlanner.CompareForOffense(false, 500f, false, 10f) < 0);
            Assert.True(PosturePlanner.CompareForOffense(true, 10f, true, 500f) > 0);
            Assert.Equal(0, PosturePlanner.CompareForOffense(true, 42f, true, 42f));
        }

        [Theory]
        [InlineData(false, 0, 1, true)]   // a free post is filled by rank
        [InlineData(false, 1, 1, false)]  // posts full -> an ordinary attacker is not appointed
        [InlineData(true, 1, 1, true)]    // posts full -> a host leader still is
        public void IsAppointed_AHostLeaderAlwaysHoldsThePost(bool leadsHost, int appointedSoFar, int marshalCount, bool expected)
        {
            Assert.Equal(expected, MarshalPlanner.IsAppointed(leadsHost, appointedSoFar, marshalCount));
        }

        [Fact]
        public void RankForOffense_KeepsScoresAlignedWithTheirLords()
        {
            var lords = new List<Lord> { new Lord("A", false, 900f), new Lord("B", true, 200f), new Lord("C", false, 400f) };
            var scores = new List<float> { 900f, 200f, 400f };

            PosturePlanner.RankForOffense(lords, scores, l => l.LeadsHost);

            Assert.Equal(new[] { "B", "A", "C" }, lords.ConvertAll(l => l.Name));
            Assert.Equal(new[] { 200f, 900f, 400f }, scores);
        }

        /// <summary>
        /// (a) The CW1 case (run 20261103-194528-b0f0): Manteos leads a host but
        /// out-ranks nobody, and Ascyron would take the single slot and post.
        /// Ranked first, Manteos holds both, and no second host is licensed.
        /// </summary>
        [Fact]
        public void HostLeaderWithLowerScoreTakesTheOnlySlotAndIsMarshal()
        {
            var lords = new List<Lord>
            {
                new Lord("Ascyron", false, 900f),
                new Lord("Manteos", true, 200f),
                new Lord("Other", false, 400f),
            };

            var (attackers, marshals) = PlanDay(lords, aggressiveSlots: 1, maxMarshals: MarshalPlanner.DefaultMaxMarshals);

            Assert.Equal(new[] { "Manteos" }, attackers.ConvertAll(l => l.Name));
            Assert.Equal(1, marshals);
        }

        /// <summary>
        /// (b) More host leaders inside the slots than marshal posts: every host
        /// leader is appointed (none of their hosts goes uncounted), and no lord
        /// without a host gets a post on top.
        /// </summary>
        [Fact]
        public void MoreHostLeadersThanPosts_AllHostLeadersAppointed_NoOneElse()
        {
            var lords = new List<Lord>
            {
                new Lord("Strong", false, 990f),
                new Lord("Host1", true, 100f),
                new Lord("Host2", true, 300f),
                new Lord("Host3", true, 200f),
                new Lord("Bold", false, 800f),
            };

            var (attackers, marshals) = PlanDay(lords, aggressiveSlots: 5, maxMarshals: 2);

            Assert.Equal(3, marshals);
            for (int i = 0; i < attackers.Count; i++)
            {
                Assert.Equal(attackers[i].LeadsHost, i < marshals);
            }
        }

        /// <summary>
        /// (c) "0 may attack": the host leader is not pushed into the offensive - he
        /// stays Defensive (so a relief army keeps its defender mission), and nobody
        /// is appointed, so no new army can be raised.
        /// </summary>
        [Fact]
        public void ZeroSlots_HostLeaderStaysDefensive_NobodyAppointed()
        {
            var lords = new List<Lord>
            {
                new Lord("Manteos", true, 200f),
                new Lord("Ascyron", false, 900f),
            };

            var (attackers, marshals) = PlanDay(lords, aggressiveSlots: 0, maxMarshals: MarshalPlanner.DefaultMaxMarshals);

            Assert.Equal("Manteos", lords[0].Name);
            Assert.Empty(attackers);
            Assert.Equal(0, marshals);
        }

        [Theory]
        [InlineData(false, false, false)]  // no host -> plain ranking
        [InlineData(true, false, true)]    // offensive host -> ranks first
        [InlineData(true, true, false)]    // defender (relief) host -> no priority
        public void TakesSlotFirst_OnlyForNonDefenderHosts(bool leadsHost, bool hostIsDefender, bool expected)
        {
            Assert.Equal(expected, PosturePlanner.TakesSlotFirst(leadsHost, hostIsDefender));
        }

        /// <summary>
        /// Doctrine off: no priority at all, which must reproduce the pre-T-0115
        /// ranking exactly (strict "higher score wins" selection sort), ties included.
        /// </summary>
        [Fact]
        public void RankForOffense_WithoutPriority_IsThePlainDescendingRanking()
        {
            var lords = new List<Lord>
            {
                new Lord("A", true, 5f),
                new Lord("B", false, 9f),
                new Lord("C", false, 5f),
                new Lord("D", true, 7f),
            };
            var scores = new List<float> { 5f, 9f, 5f, 7f };

            PosturePlanner.RankForOffense(lords, scores, _ => false);

            Assert.Equal(new[] { "B", "D", "C", "A" }, lords.ConvertAll(l => l.Name));
            Assert.Equal(new[] { 9f, 7f, 5f, 5f }, scores);
        }

        /// <summary>
        /// More host leaders than slots (but at least one slot): the best-scored host
        /// leaders take the slots, the surplus stays Defensive without a post, and
        /// no lord without a host attacks.
        /// </summary>
        [Fact]
        public void MoreHostLeadersThanSlots_BestHostLeadersAttack_RestStayDefensive()
        {
            var lords = new List<Lord>
            {
                new Lord("Strong", false, 990f),
                new Lord("Host1", true, 100f),
                new Lord("Host2", true, 300f),
                new Lord("Host3", true, 200f),
            };

            var (attackers, marshals) = PlanDay(lords, aggressiveSlots: 2, maxMarshals: MarshalPlanner.DefaultMaxMarshals);

            Assert.Equal(new[] { "Host2", "Host3" }, attackers.ConvertAll(l => l.Name));
            Assert.Equal(2, marshals);
        }

        /// <summary>
        /// A relief army (vanilla ArmyType Defender) gets no priority: it is not moved
        /// into the slot and turned Aggressive, and the slot goes to the lord who
        /// ranks first by score - as before T-0115.
        /// </summary>
        [Fact]
        public void DefenderHost_GetsNoPriority_StaysOutOfTheSlot()
        {
            var lords = new List<Lord>
            {
                new Lord("Strong", false, 900f),
                new Lord("Relief", true, 200f, hostIsDefender: true),
            };

            var (attackers, marshals) = PlanDay(lords, aggressiveSlots: 1, maxMarshals: MarshalPlanner.DefaultMaxMarshals);

            Assert.Equal(new[] { "Strong" }, attackers.ConvertAll(l => l.Name));
            Assert.Equal(1, marshals);
        }
    }
}
