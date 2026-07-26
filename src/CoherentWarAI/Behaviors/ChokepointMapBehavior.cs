using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
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
            // Loading a different campaign in the same process would otherwise leave
            // settlements of the previous one in the adjacency cache.
            Models.SettlementNeighbors.Clear();
            Recompute();
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            // Taking a fief redraws the routes into both realms, but a collapsing
            // front can flip several in a row. The daily rebuild below picks the new
            // shape up on its own, so nothing is recomputed mid-siege; garrison sizes
            // do not need to react within the same day.
        }

        private void OnDailyTick()
        {
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
            LogTopGateways(scores);
        }

        /// <summary>
        /// Reports the strongest gateways so the route analysis can be sanity-checked
        /// against the actual map - these are the fiefs worth holding and watching.
        /// </summary>
        private static void LogTopGateways(Dictionary<Settlement, float> scores)
        {
            if (!WarAiLog.Enabled || scores.Count == 0)
            {
                return;
            }

            List<KeyValuePair<Settlement, float>> ranked = new List<KeyValuePair<Settlement, float>>(scores);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            WarAiLog.Section(WarAiLog.GameDate() + " - gateways into each realm");
            WarAiLog.Write("Gateway", "settlement                gate  garrison  faction");

            int shown = ranked.Count < 15 ? ranked.Count : 15;
            for (int i = 0; i < shown; i++)
            {
                Settlement settlement = ranked[i].Key;

                // Show what the gate rating actually does to the garrison, so the
                // two halves of this feature can be judged together.
                float garrisonMultiplier = Models.CoherentGarrisonModel.GetMultiplier(settlement);

                WarAiLog.Write("Gateway", string.Format("{0,-24} {1:F2}    x{2:F2}  {3}",
                    settlement.Name, ranked[i].Value, garrisonMultiplier, settlement.MapFaction?.Name));
            }

            // The player's own holdings matter more to them than the global top 15.
            LogPlayerHoldings(scores);
            WarAiLog.Flush();
        }

        /// <summary>
        /// Reports the player's own fiefs regardless of where they rank globally -
        /// these are the ones whose garrisons they will actually notice.
        /// </summary>
        private static void LogPlayerHoldings(Dictionary<Settlement, float> scores)
        {
            IFaction playerFaction = Clan.PlayerClan?.MapFaction;
            if (playerFaction == null)
            {
                return;
            }

            bool wroteHeading = false;
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || !settlement.IsFortification || settlement.MapFaction != playerFaction)
                {
                    continue;
                }

                if (!wroteHeading)
                {
                    WarAiLog.Write("Gateway", "-- your realm --");
                    wroteHeading = true;
                }

                scores.TryGetValue(settlement, out float gate);
                WarAiLog.Write("Gateway", string.Format("{0,-24} {1:F2}    x{2:F2}",
                    settlement.Name, gate, Models.CoherentGarrisonModel.GetMultiplier(settlement)));
            }
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
                foreach (Settlement neighbor in Models.SettlementNeighbors.Of(nodes[i]))
                {
                    if (index.TryGetValue(neighbor, out int neighborIndex))
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
