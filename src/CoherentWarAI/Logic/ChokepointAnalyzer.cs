using System.Collections.Generic;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Finds the gateways into a realm: the settlements an invader has to pass
    /// through to reach everything behind them.
    ///
    /// A settlement is not a chokepoint because it has many neighbours - it is one
    /// because the routes into our territory run through it and there is no way
    /// around. So this walks the actual travel graph outward from enemy ground and
    /// asks, for each of our settlements, how much of our realm sits behind it.
    ///
    /// Alternative routes dissolve the score on purpose: if two roads lead in, then
    /// neither is a true bottleneck and both are rated accordingly. That is the
    /// difference between a gate worth holding and a road worth watching.
    ///
    /// Engine-free (plain graph indices), so it is unit-tested without the game.
    /// </summary>
    public static class ChokepointAnalyzer
    {
        /// <summary>
        /// Gateway weight per node: roughly "how many of our settlements are covered
        /// by this one", counting the settlement itself. A dead-end holding scores 1;
        /// the single gate to a whole province scores as high as the province is big.
        /// Nodes that are not ours, or that the enemy cannot reach, score 0.
        /// </summary>
        /// <param name="adjacency">Travel graph: adjacency[i] lists neighbours of node i.</param>
        /// <param name="isOurs">Nodes belonging to the realm being analyzed.</param>
        /// <param name="isEnemySource">Nodes an invasion can start from.</param>
        public static float[] ComputeGatewayWeights(IList<int>[] adjacency, bool[] isOurs, bool[] isEnemySource)
        {
            int nodeCount = adjacency?.Length ?? 0;
            float[] weights = new float[nodeCount];
            if (nodeCount == 0 || isOurs == null || isEnemySource == null)
            {
                return weights;
            }

            int[] distance = new int[nodeCount];
            float[] pathCount = new float[nodeCount];
            List<int>[] predecessors = new List<int>[nodeCount];
            List<int> visitOrder = new List<int>(nodeCount);
            Queue<int> queue = new Queue<int>();

            for (int i = 0; i < nodeCount; i++)
            {
                distance[i] = -1;
                predecessors[i] = new List<int>();
            }

            // Breadth-first outward from every enemy holding at once, so distance is
            // "how far from the nearest hostile ground" and pathCount records how many
            // equally short routes reach each node.
            for (int i = 0; i < nodeCount; i++)
            {
                if (i < isEnemySource.Length && isEnemySource[i])
                {
                    distance[i] = 0;
                    pathCount[i] = 1f;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                visitOrder.Add(current);

                IList<int> neighbors = adjacency[current];
                if (neighbors == null)
                {
                    continue;
                }

                foreach (int neighbor in neighbors)
                {
                    if (neighbor < 0 || neighbor >= nodeCount)
                    {
                        continue;
                    }
                    if (distance[neighbor] < 0)
                    {
                        distance[neighbor] = distance[current] + 1;
                        queue.Enqueue(neighbor);
                    }
                    if (distance[neighbor] == distance[current] + 1)
                    {
                        pathCount[neighbor] += pathCount[current];
                        predecessors[neighbor].Add(current);
                    }
                }
            }

            // Walk back from the far side: each of our settlements contributes itself,
            // and passes its accumulated weight to whatever shields it - split across
            // every equally short route, so detours reduce a gate's importance.
            float[] accumulated = new float[nodeCount];
            for (int i = visitOrder.Count - 1; i >= 0; i--)
            {
                int node = visitOrder[i];
                if (node < isOurs.Length && isOurs[node])
                {
                    accumulated[node] += 1f;
                }

                if (pathCount[node] <= 0f)
                {
                    continue;
                }

                foreach (int predecessor in predecessors[node])
                {
                    accumulated[predecessor] += pathCount[predecessor] / pathCount[node] * accumulated[node];
                }
            }

            for (int i = 0; i < nodeCount; i++)
            {
                weights[i] = (i < isOurs.Length && isOurs[i]) ? accumulated[i] : 0f;
            }
            return weights;
        }

        /// <summary>
        /// Turns a raw gateway weight into a 0..1 score comparable across realms of
        /// different sizes, saturating so a big kingdom's gate does not dwarf a small
        /// one's simply for having more land behind it.
        /// </summary>
        public static float NormalizeGatewayWeight(float weight, float saturation)
        {
            if (weight <= 1f)
            {
                return 0f;
            }
            if (saturation <= 0f)
            {
                return 1f;
            }

            // Only the part beyond "covers just itself" counts as gateway value.
            float excess = weight - 1f;
            return excess / (excess + saturation);
        }
    }
}
