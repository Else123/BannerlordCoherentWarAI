using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Models;
using CoherentWarAI.Settings;
using CoherentWarAI.UI;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Keeps the unknown-settlement marker overlay attached to the map screen and
    /// decides which enemy settlements it marks.
    ///
    /// The overlay itself (<see cref="UnknownSettlementMarkerView"/>) is a MapView,
    /// and MapScreen.Instance only exists once the campaign map is actually on
    /// screen - there is no event for that, and the screen is torn down and
    /// rebuilt for battles, sieges and loading a save. So this polls on a cheap
    /// tick and re-adds the view whenever it finds it missing, rather than
    /// assuming a single AddMapView call lasts the whole session.
    /// </summary>
    public class MapMarkerBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// The most that will ever be marked at once, so a huge multi-front war
        /// cannot flood the screen with icons.
        /// </summary>
        private const int MaxMarkers = 40;

        /// <summary>How often the map screen's presence is (re)checked.</summary>
        private const float MapViewCheckIntervalSeconds = 1f;

        /// <summary>Settlements marked right now. Empty when the feature is off or nothing qualifies.</summary>
        public static HashSet<Settlement> MarkedSettlements { get; private set; } = new HashSet<Settlement>();

        private float _sinceLastMapViewCheck;
        private int _lastHiddenCount = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        /// <summary>Derived from the live map; nothing to save.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            MarkedSettlements = new HashSet<Settlement>();
            _sinceLastMapViewCheck = 0f;
            _lastHiddenCount = -1;
            Recompute();
        }

        private void OnHourlyTick()
        {
            Recompute();
        }

        /// <summary>
        /// Adds the marker overlay back whenever the map screen exists but does
        /// not have one - covering both the first time it appears and every time
        /// it is rebuilt. Cheap: throttled to once a second, and does nothing
        /// once the view is already attached.
        /// </summary>
        private void OnTick(float dt)
        {
            _sinceLastMapViewCheck += dt;
            if (_sinceLastMapViewCheck < MapViewCheckIntervalSeconds)
            {
                return;
            }
            _sinceLastMapViewCheck = 0f;

            MapScreen screen = MapScreen.Instance;
            if (screen != null && screen.GetMapView<UnknownSettlementMarkerView>() == null)
            {
                screen.AddMapView<UnknownSettlementMarkerView>();
            }
        }

        /// <summary>
        /// Rebuilds the marked-settlement set. Recomputed hourly (and on session
        /// start) rather than every frame - the underlying knowledge state itself
        /// only changes on the same cadence the sighting network reports on.
        /// </summary>
        private void Recompute()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.ShowUnknownSettlementMarkers)
            {
                MarkedSettlements = new HashSet<Settlement>();
                return;
            }

            IFaction ourFaction = Hero.MainHero?.MapFaction;
            if (ourFaction == null)
            {
                MarkedSettlements = new HashSet<Settlement>();
                return;
            }

            HashSet<Settlement> marked = new HashSet<Settlement>();
            int qualified = 0;

            foreach (Settlement settlement in Settlement.All)
            {
                if (!Qualifies(settlement, ourFaction, settings))
                {
                    continue;
                }

                qualified++;
                if (marked.Count < MaxMarkers)
                {
                    marked.Add(settlement);
                }
            }

            MarkedSettlements = marked;

            // Said once per change, not once an hour: a silent cap reads as
            // "everything unknown is marked" when it is not, but a line every hour
            // for the length of a war would bury the rest of the log.
            int hidden = qualified - marked.Count;
            if (hidden != _lastHiddenCount)
            {
                _lastHiddenCount = hidden;
                if (hidden > 0)
                {
                    WarAiLog.Write("MapMarker", string.Format(
                        "marking {0} unscouted enemy settlements; {1} more exist but are left unmarked (cap {2})",
                        marked.Count, hidden, MaxMarkers));
                }
            }
        }

        /// <summary>
        /// Whether a settlement should carry the "we do not know this place"
        /// marker: an at-war fief that is not ours, does not border our own
        /// land, and that our own faction has not scouted recently enough for
        /// <see cref="Logic.SightingNetwork.KnowledgeWeight"/> to treat as known.
        /// </summary>
        private static bool Qualifies(Settlement settlement, IFaction ourFaction, CoherentWarAISettings settings)
        {
            if (settlement == null || settlement.IsHideout || !(settlement.IsFortification || settlement.IsVillage))
            {
                return false;
            }

            IFaction owner = settlement.MapFaction;
            if (owner == null || owner == ourFaction || !ourFaction.IsAtWarWith(owner))
            {
                return false;
            }

            if (BordersOurLand(settlement, ourFaction))
            {
                return false;
            }

            float hoursSinceObserved = SightingNetworkBehavior.HoursSinceObserved(ourFaction, settlement);
            bool knownAndFresh = hoursSinceObserved >= 0f && hoursSinceObserved <= settings.KnowledgeLifetimeHours;
            return !knownAndFresh;
        }

        /// <summary>Same adjacency notion the target score model uses for front coherence.</summary>
        private static bool BordersOurLand(Settlement settlement, IFaction ourFaction)
        {
            foreach (Settlement neighbor in SettlementNeighbors.Of(settlement))
            {
                if (neighbor.MapFaction == ourFaction)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
