using System.Collections.Generic;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Works out which settlements are the gateways into each realm, by walking the
    /// campaign map's real travel graph outward from foreign ground.
    ///
    /// Recomputed daily and whenever a fief changes hands - the routes into a realm
    /// only change when the map does. Results live in memory only; nothing is saved.
    /// </summary>
    public class ChokepointMapBehavior : CampaignBehaviorBase
    {
        private static bool _dirty;

        /// <summary>Latest gateway scores, 0..1 per settlement. Empty until first computed.</summary>
        public static Dictionary<Settlement, float> GatewayScores { get; private set; } = new Dictionary<Settlement, float>();

        /// <summary>
        /// Whether the route analysis has actually run. Callers must check this
        /// before treating a score of 0 as meaningful: "no routes lead through here"
        /// is a real answer, and must not be confused with "not worked out yet".
        /// </summary>
        public static bool HasComputedScores { get; private set; }

        /// <summary>
        /// Gateway score for a settlement. A result of 0 means "not a gateway" once
        /// <see cref="HasComputedScores"/> is set - most importantly for settlements
        /// the enemy can simply go around.
        /// </summary>
        public static float GetGatewayScore(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0f;
            }
            return GatewayScores.TryGetValue(settlement, out float score) ? score : 0f;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        /// <summary>Derived from the live map; nothing to save.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Recompute();
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            // Taking a fief redraws the routes into both realms - but a front
            // collapsing can flip several fiefs in a row, so just mark the map stale
            // and redraw it once on the next tick rather than mid-siege.
            _dirty = true;
        }

        private void OnDailyTick()
        {
            _dirty = false;
            Recompute();
        }

        private void Recompute()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableChokepointAnalysis)
            {
                GatewayScores = new Dictionary<Settlement, float>();
                HasComputedScores = false;
                return;
            }

            List<Settlement> nodes = CollectFortifications();
            if (nodes.Count == 0)
            {
                return;
            }

            Dictionary<Settlement, int> index = new Dictionary<Settlement, int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                index[nodes[i]] = i;
            }

            IList<int>[] adjacency = BuildAdjacency(nodes, index);
            Dictionary<Settlement, float> scores = new Dictionary<Settlement, float>(nodes.Count);

            // Each realm sees a different map: what is a gateway depends on whose
            // land lies behind it, so the walk is repeated per faction.
            foreach (IFaction faction in CollectFactions(nodes))
            {
                bool[] isOurs = new bool[nodes.Count];
                bool[] isForeign = new bool[nodes.Count];
                bool hasAny = false;

                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].MapFaction == faction)
                    {
                        isOurs[i] = true;
                        hasAny = true;
                    }
                    else
                    {
                        // Ownership, not the current war: a gate stays a gate in
                        // peacetime, which is what lets garrisons be ready in advance.
                        isForeign[i] = true;
                    }
                }
                if (!hasAny)
                {
                    continue;
                }

                float[] weights = ChokepointAnalyzer.ComputeGatewayWeights(adjacency, isOurs, isForeign);
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (isOurs[i] && weights[i] > 1f)
                    {
                        scores[nodes[i]] = ChokepointAnalyzer.NormalizeGatewayWeight(weights[i], settings.GatewaySaturation);
                    }
                }
            }

            GatewayScores = scores;
            HasComputedScores = true;
        }

        private static List<Settlement> CollectFortifications()
        {
            List<Settlement> nodes = new List<Settlement>();
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement != null && settlement.IsFortification && settlement.Town != null)
                {
                    nodes.Add(settlement);
                }
            }
            return nodes;
        }

        private static IList<int>[] BuildAdjacency(List<Settlement> nodes, Dictionary<Settlement, int> index)
        {
            IList<int>[] adjacency = new IList<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                List<int> neighbors = new List<int>();
                foreach (Settlement neighbor in nodes[i].Town.GetNeighborFortifications(MobileParty.NavigationType.All))
                {
                    if (neighbor != null && index.TryGetValue(neighbor, out int neighborIndex))
                    {
                        neighbors.Add(neighborIndex);
                    }
                }
                adjacency[i] = neighbors;
            }
            return adjacency;
        }

        private static IEnumerable<IFaction> CollectFactions(List<Settlement> nodes)
        {
            HashSet<IFaction> factions = new HashSet<IFaction>();
            foreach (Settlement settlement in nodes)
            {
                IFaction faction = settlement.MapFaction;
                if (faction != null)
                {
                    factions.Add(faction);
                }
            }
            return factions;
        }
    }
}
