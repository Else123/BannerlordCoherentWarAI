using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// One of our ports' naval situation, as vanilla already tracks it.
    /// Net threat (<see cref="ThreatIntensity"/> minus <see cref="AllyIntensity"/>) is
    /// what ranking is based on, and only ever relatively against other ports' net
    /// threat - never against an absolute constant, since the underlying vanilla
    /// scale is not itself fixed (SPEC SS4.1 / O10).
    /// </summary>
    public readonly struct PortSignal
    {
        public readonly float X;
        public readonly float Y;

        /// <summary>Settlement.NearbyNavalThreatIntensity.</summary>
        public readonly float ThreatIntensity;

        /// <summary>Settlement.NearbyNavalAllyIntensity.</summary>
        public readonly float AllyIntensity;

        public PortSignal(float x, float y, float threatIntensity, float allyIntensity)
        {
            X = x;
            Y = y;
            ThreatIntensity = threatIntensity;
            AllyIntensity = allyIntensity;
        }
    }

    /// <summary>A point on the map worth sending a naval patrol to, with how urgent it is.</summary>
    public readonly struct PatrolLane
    {
        public readonly float X;
        public readonly float Y;

        /// <summary>Average net threat of the port(s) this lane covers.</summary>
        public readonly float Urgency;

        /// <summary>
        /// Index into the <c>ports</c> array passed to <see cref="NavalPatrolPlanner.SelectLanes"/>
        /// of the higher-net-threat port of this lane's pair (or the sole port for a
        /// single-port lane). Unlike <see cref="X"/>/<see cref="Y"/> - the lane's own
        /// midpoint, kept for ranking, logging and a future overlay - a port's own position
        /// is guaranteed to sit on the naval mesh, so the connector dispatches patrols here
        /// (SPEC SS4.1 deviation; see the connector's own comment for the O11 follow-up).
        /// </summary>
        public readonly int AnchorIndex;

        public PatrolLane(float x, float y, float urgency, int anchorIndex)
        {
            X = x;
            Y = y;
            Urgency = urgency;
            AnchorIndex = anchorIndex;
        }
    }

    /// <summary>
    /// Engine-free naval lane planning: turns our ports' threat signals into
    /// patrol points, and decides how many of them a realm can actually run.
    ///
    /// Vanilla gives naval navigation zero coordination - each ship-owning lord
    /// would otherwise have to be told individually where to go. This ranks our
    /// own ports by how contested their waters are and places patrols where the
    /// need is greatest, the same way <c>ChokepointAnalyzer</c> ranks gateways
    /// instead of merely counting foreign neighbours.
    /// </summary>
    public static class NavalPatrolPlanner
    {
        /// <summary>
        /// Ranks our ports by net naval threat and pairs them off into patrol lanes,
        /// each lane sitting at the midpoint of two ports. Deterministic: ties in net
        /// threat are broken by the port's original array index, so the same input
        /// always yields the same output and no <see cref="Random"/> is ever used.
        /// A leftover unpaired port (odd count) becomes its own lane at its own
        /// position; a single port is therefore always its own lane. Zero ports
        /// yields no lanes. Never returns more than <paramref name="wanted"/> lanes.
        /// </summary>
        public static PatrolLane[] SelectLanes(PortSignal[] ports, int wanted)
        {
            if (ports == null || ports.Length == 0 || wanted <= 0)
            {
                return new PatrolLane[0];
            }

            int[] rankedIndices = RankByNetThreatDescending(ports);

            int pairCount = ports.Length / 2;
            bool hasLeftover = ports.Length % 2 != 0;
            int laneCount = pairCount + (hasLeftover ? 1 : 0);
            if (laneCount > wanted)
            {
                laneCount = wanted;
            }

            PatrolLane[] lanes = new PatrolLane[laneCount];
            for (int i = 0; i < laneCount; i++)
            {
                int firstIndex = i * 2;
                int secondIndex = firstIndex + 1;
                if (secondIndex < rankedIndices.Length)
                {
                    PortSignal first = ports[rankedIndices[firstIndex]];
                    PortSignal second = ports[rankedIndices[secondIndex]];
                    float x = (first.X + second.X) / 2f;
                    float y = (first.Y + second.Y) / 2f;
                    float urgency = (NetThreat(first) + NetThreat(second)) / 2f;
                    // rankedIndices is sorted by net threat descending, so firstIndex is
                    // always the higher (or, on a tie, the lower-original-index) of the pair.
                    lanes[i] = new PatrolLane(x, y, urgency, rankedIndices[firstIndex]);
                }
                else
                {
                    PortSignal only = ports[rankedIndices[firstIndex]];
                    lanes[i] = new PatrolLane(only.X, only.Y, NetThreat(only), rankedIndices[firstIndex]);
                }
            }
            return lanes;
        }

        /// <summary>
        /// How many patrols a realm can run at once: scales with how many
        /// naval-capable lords are available and how competent their admiral is,
        /// never below zero and never above <paramref name="maxPatrols"/>. Any
        /// capable lord at all gets at least one patrol - competence decides how
        /// many more, not whether the fleet moves at all.
        /// </summary>
        public static int PatrolCount(int capableLords, float competence, int maxPatrols)
        {
            if (capableLords <= 0 || maxPatrols <= 0)
            {
                return 0;
            }

            float clampedCompetence = competence < 0f ? 0f : (competence > 1f ? 1f : competence);
            float scaled = capableLords * (0.5f + 0.5f * clampedCompetence);
            int count = (int)Math.Floor(scaled);
            if (count < 1)
            {
                count = 1;
            }
            if (count > maxPatrols)
            {
                count = maxPatrols;
            }
            return count;
        }

        /// <summary>
        /// <see cref="PatrolCount(int, float, int)"/> over the lords that can actually
        /// be addressed: candidates marked in <paramref name="leadsArmy"/> are never
        /// ranked by <see cref="RankPatrolAddressees(bool[], float[], bool[])"/>, so
        /// the lane count scales with addressable lords only: with a low competence
        /// that means fewer patrols, never lanes beyond the lords left to sail them.
        /// The count never exceeds the length of that ranking. Unlike
        /// <c>RankPatrolAddressees(..., null)</c>, which excludes nobody, a null
        /// <paramref name="leadsArmy"/> here means no candidates and yields zero patrols.
        /// </summary>
        public static int PatrolCount(bool[] leadsArmy, float competence, int maxPatrols)
        {
            int addressable = 0;
            if (leadsArmy != null)
            {
                foreach (bool leads in leadsArmy)
                {
                    if (!leads)
                    {
                        addressable++;
                    }
                }
            }
            return PatrolCount(addressable, competence, maxPatrols);
        }

        /// <summary>
        /// Orders naval candidates for patrol-lane assignment so defenders are used
        /// before attackers: candidates with <paramref name="isAggressive"/> false
        /// (not released to attack this tick) come first, ranked by
        /// <paramref name="score"/> descending; candidates with it true come after,
        /// also by score descending. Doctrine ("one attacks, the rest defend"): a lord
        /// already released to attack should not also be handed a defensive patrol
        /// lane, so he is only used as a fallback once every defender has a lane.
        /// Deterministic, like <see cref="RankByNetThreatDescending"/>: ties within a
        /// bucket are broken by the candidate's original array index ascending, and no
        /// <see cref="Random"/> is ever used. Returns a permutation of
        /// <c>[0, isAggressive.Length)</c> - the caller indexes its own candidate list
        /// with it. <paramref name="isAggressive"/> and <paramref name="score"/> must be
        /// the same length; a null or empty <paramref name="isAggressive"/> yields an
        /// empty result.
        /// </summary>
        public static int[] RankPatrolAddressees(bool[] isAggressive, float[] score)
        {
            int length = isAggressive != null ? isAggressive.Length : 0;
            int[] indices = new int[length];
            for (int i = 0; i < length; i++)
            {
                indices[i] = i;
            }

            Array.Sort(indices, (a, b) =>
            {
                bool aggressiveA = isAggressive[a];
                bool aggressiveB = isAggressive[b];
                if (aggressiveA != aggressiveB)
                {
                    // Non-aggressive (defender) sorts before aggressive (attacker).
                    return aggressiveA.CompareTo(aggressiveB);
                }
                // CompareTo (not !=) so NaN scores still fall through to the index
                // tie-break instead of leaving their relative order to the sort.
                int byScore = score[b].CompareTo(score[a]);
                if (byScore != 0)
                {
                    return byScore;
                }
                // Deterministic tie-break: original array index, ascending.
                return a.CompareTo(b);
            });
            return indices;
        }

        /// <summary>
        /// <see cref="RankPatrolAddressees(bool[], float[])"/> without the candidates
        /// marked in <paramref name="leadsArmy"/>. A lord leading an army is never
        /// dispatched on a patrol (<see cref="NavalPatrolDispatch.Decide"/> drops it,
        /// <see cref="NavalDispatchVerdict.DropLeadsArmy"/>), so a lane handed to him
        /// would stay unmanned; leaving him out lets it go to the next lord instead.
        /// The result indexes the caller's candidate list, as with the unfiltered
        /// ranking; <paramref name="leadsArmy"/> must be the same length as
        /// <paramref name="isAggressive"/>. A null <paramref name="leadsArmy"/> excludes nobody.
        /// </summary>
        public static int[] RankPatrolAddressees(bool[] isAggressive, float[] score, bool[] leadsArmy)
        {
            int[] ranked = RankPatrolAddressees(isAggressive, score);
            if (leadsArmy == null)
            {
                return ranked;
            }

            int[] addressable = new int[ranked.Length];
            int count = 0;
            foreach (int index in ranked)
            {
                if (!leadsArmy[index])
                {
                    addressable[count++] = index;
                }
            }
            Array.Resize(ref addressable, count);
            return addressable;
        }

        private static float NetThreat(PortSignal port)
        {
            return port.ThreatIntensity - port.AllyIntensity;
        }

        private static int[] RankByNetThreatDescending(PortSignal[] ports)
        {
            int[] indices = new int[ports.Length];
            for (int i = 0; i < ports.Length; i++)
            {
                indices[i] = i;
            }

            Array.Sort(indices, (a, b) =>
            {
                float netA = NetThreat(ports[a]);
                float netB = NetThreat(ports[b]);
                if (netA != netB)
                {
                    return netB.CompareTo(netA);
                }
                // Deterministic tie-break: original array index, ascending.
                return a.CompareTo(b);
            });
            return indices;
        }
    }
}
