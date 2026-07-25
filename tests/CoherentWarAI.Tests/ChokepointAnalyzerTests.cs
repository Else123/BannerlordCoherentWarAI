using System.Collections.Generic;
using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class ChokepointAnalyzerTests
    {
        private static IList<int>[] Graph(params int[][] adjacency)
        {
            IList<int>[] result = new IList<int>[adjacency.Length];
            for (int i = 0; i < adjacency.Length; i++)
            {
                result[i] = new List<int>(adjacency[i]);
            }
            return result;
        }

        [Fact]
        public void SingleCorridor_TheOutermostFiefGuardsEverythingBehindIt()
        {
            // enemy(0) - A(1) - B(2) - C(3), all three ours, one road in.
            IList<int>[] graph = Graph(
                new[] { 1 },
                new[] { 0, 2 },
                new[] { 1, 3 },
                new[] { 2 });

            float[] weights = ChokepointAnalyzer.ComputeGatewayWeights(
                graph,
                new[] { false, true, true, true },
                new[] { true, false, false, false });

            Assert.Equal(3f, weights[1], 4);  // A shields the whole province
            Assert.Equal(2f, weights[2], 4);
            Assert.Equal(1f, weights[3], 4);  // dead end, covers only itself
            Assert.Equal(0f, weights[0], 4);  // not ours
        }

        [Fact]
        public void TwoRoadsIn_NeitherIsATrueBottleneck()
        {
            // enemy(0) reaches C(3) through either A(1) or B(2).
            IList<int>[] graph = Graph(
                new[] { 1, 2 },
                new[] { 0, 3 },
                new[] { 0, 3 },
                new[] { 1, 2 });

            float[] weights = ChokepointAnalyzer.ComputeGatewayWeights(
                graph,
                new[] { false, true, true, true },
                new[] { true, false, false, false });

            // Each covers itself plus half of C - decidedly less than a sole gate.
            Assert.Equal(1.5f, weights[1], 4);
            Assert.Equal(1.5f, weights[2], 4);
            Assert.Equal(1f, weights[3], 4);
            Assert.True(weights[1] < 3f, "a bypassable route must not rate as a full gate");
        }

        [Fact]
        public void ASingleGateOutranksTwoAlternativeRoutesCoveringTheSameLand()
        {
            IList<int>[] corridor = Graph(new[] { 1 }, new[] { 0, 2 }, new[] { 1 });
            float[] single = ChokepointAnalyzer.ComputeGatewayWeights(
                corridor, new[] { false, true, true }, new[] { true, false, false });

            IList<int>[] forked = Graph(new[] { 1, 2 }, new[] { 0 }, new[] { 0 });
            float[] split = ChokepointAnalyzer.ComputeGatewayWeights(
                forked, new[] { false, true, true }, new[] { true, false, false });

            Assert.True(single[1] > split[1], "the sole entrance must matter more than one of two");
        }

        [Fact]
        public void UnreachableTerritoryScoresNothing()
        {
            // Island province (2,3) with no connection to the enemy at all.
            IList<int>[] graph = Graph(
                new[] { 1 },
                new[] { 0 },
                new[] { 3 },
                new[] { 2 });

            float[] weights = ChokepointAnalyzer.ComputeGatewayWeights(
                graph,
                new[] { false, true, true, true },
                new[] { true, false, false, false });

            Assert.Equal(0f, weights[2], 4);
            Assert.Equal(0f, weights[3], 4);
        }

        [Fact]
        public void NoEnemyAtAll_NothingIsAGateway()
        {
            IList<int>[] graph = Graph(new[] { 1 }, new[] { 0 });
            float[] weights = ChokepointAnalyzer.ComputeGatewayWeights(
                graph, new[] { true, true }, new[] { false, false });

            Assert.Equal(0f, weights[0], 4);
            Assert.Equal(0f, weights[1], 4);
        }

        [Fact]
        public void HandlesEmptyAndMalformedInputWithoutThrowing()
        {
            Assert.Empty(ChokepointAnalyzer.ComputeGatewayWeights(new IList<int>[0], new bool[0], new bool[0]));
            Assert.Empty(ChokepointAnalyzer.ComputeGatewayWeights(null, new bool[0], new bool[0]));

            // Out-of-range neighbour indices must be ignored, not crash.
            IList<int>[] broken = Graph(new[] { 1, 99, -3 }, new[] { 0 });
            float[] weights = ChokepointAnalyzer.ComputeGatewayWeights(
                broken, new[] { false, true }, new[] { true, false });
            Assert.Equal(1f, weights[1], 4);
        }

        [Theory]
        [InlineData(1f, 4f, 0f)]        // covers only itself -> no gateway value
        [InlineData(0.5f, 4f, 0f)]      // below parity -> clamped
        [InlineData(3f, 4f, 0.3333f)]   // excess 2 -> 2/6
        [InlineData(5f, 4f, 0.5f)]      // excess 4 -> 4/8
        [InlineData(21f, 4f, 0.8333f)]  // large province, saturating
        [InlineData(3f, 0f, 1f)]        // saturation disabled
        public void NormalizeGatewayWeight_SaturatesAcrossRealmSizes(float weight, float saturation, float expected)
        {
            Assert.Equal(expected, ChokepointAnalyzer.NormalizeGatewayWeight(weight, saturation), 4);
        }
    }
}
