using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Carries word of enemy forces between a realm's lords.
    ///
    /// Vanilla AI is omniscient - every lord scores every target from perfect
    /// knowledge of the whole map. This makes knowledge local instead: a force is
    /// noticed by whoever is near enough to see it, and word spreads outward at the
    /// speed a rider could carry it. A realm therefore learns of an invasion late
    /// and nearest-first, and concentrates its defence around where the enemy was
    /// actually reported rather than everywhere at once.
    ///
    /// The reports feed the defensive patrolling score, so lords drift toward
    /// threatened ground of their own accord. Nothing here commands a party.
    /// </summary>
    public class SightingNetworkBehavior : CampaignBehaviorBase
    {
        private class Sighting
        {
            public IFaction Observer;
            public IFaction Seen;
            public CampaignVec2 Where;
            public float EnemyStrength;

            /// <summary>Whether that force was visibly committed to something when seen.</summary>
            public bool WasTiedDown;

            /// <summary>What the observer's scout was worth, 0..1.</summary>
            public float Confidence;

            public CampaignTime When;
        }

        /// <summary>
        /// What each realm believes about how much of an enemy's strength is tied
        /// up, keyed by observer and observed - because belief is not shared.
        /// </summary>
        private static Dictionary<IFaction, Dictionary<IFaction, float>> _believedDistraction
            = new Dictionary<IFaction, Dictionary<IFaction, float>>();

        /// <summary>
        /// How much of an enemy's strength this realm has actually seen tied up
        /// elsewhere, as a share of that enemy's known total.
        ///
        /// Only what scouts have reported counts. A realm cannot exploit an opening
        /// it has not noticed, and letting it do so would give back with one hand
        /// the omniscience the sighting network takes away with the other.
        /// </summary>
        public static float BelievedDistraction(IFaction observer, IFaction observed)
        {
            if (observer == null || observed == null)
            {
                return 0f;
            }
            if (!_believedDistraction.TryGetValue(observer, out Dictionary<IFaction, float> beliefs))
            {
                return 0f;
            }
            return beliefs.TryGetValue(observed, out float ratio) ? ratio : 0f;
        }

        private static readonly List<Sighting> Sightings = new List<Sighting>();

        /// <summary>Reported threat per settlement, rebuilt as reports age and spread.</summary>
        private static Dictionary<Settlement, float> _reportedThreat = new Dictionary<Settlement, float>();

        /// <summary>
        /// When each realm last had a party within sight of each foreign settlement.
        /// Kept per observer, because knowing a place is not something realms share.
        /// </summary>
        private static readonly Dictionary<IFaction, Dictionary<Settlement, CampaignTime>> LastObserved
            = new Dictionary<IFaction, Dictionary<Settlement, CampaignTime>>();

        /// <summary>
        /// Hours since this realm last had eyes on that settlement, or -1 if it
        /// never has. Used to keep lords from marching confidently on places they
        /// know nothing about.
        /// </summary>
        public static float HoursSinceObserved(IFaction observer, Settlement settlement)
        {
            if (observer == null || settlement == null)
            {
                return -1f;
            }
            if (!LastObserved.TryGetValue(observer, out Dictionary<Settlement, CampaignTime> seen))
            {
                return -1f;
            }
            return seen.TryGetValue(settlement, out CampaignTime when) ? when.ElapsedHoursUntilNow : -1f;
        }

        /// <summary>
        /// How alarming the reports reaching this settlement are. Zero when nothing
        /// has been reported, or when no report has arrived here yet.
        /// </summary>
        public static float ReportedThreatAt(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0f;
            }
            return _reportedThreat.TryGetValue(settlement, out float threat) ? threat : 0f;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        /// <summary>Reports are observations, re-made from live state; nothing to save.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Sightings.Clear();
            _reportedThreat = new Dictionary<Settlement, float>();
            _believedDistraction = new Dictionary<IFaction, Dictionary<IFaction, float>>();
            LastObserved.Clear();
        }

        /// <summary>
        /// Notes foreign settlements this party can currently see. Riding past a
        /// castle is how a realm learns what is there - and not having ridden past
        /// is why it should be wary of marching on one.
        /// </summary>
        private static void NoteSettlementsInSight(MobileParty observer, float spotRadius)
        {
            IFaction faction = observer.MapFaction;
            if (faction == null)
            {
                return;
            }

            LocatableSearchData<Settlement> data = Settlement.StartFindingLocatablesAroundPosition(
                observer.Position.ToVec2(), spotRadius);

            for (Settlement settlement = Settlement.FindNextLocatable(ref data);
                 settlement != null;
                 settlement = Settlement.FindNextLocatable(ref data))
            {
                // Villages count as much as castles here: raids are scored through
                // the same path as sieges, so tracking only fortifications would
                // leave every village permanently unknown - which would turn the
                // knowledge weight into a flat penalty on all raiding rather than a
                // distinction between ground we watch and ground we do not.
                if (settlement.IsHideout || !(settlement.IsFortification || settlement.IsVillage)
                    || settlement.MapFaction == faction)
                {
                    continue;
                }

                if (!LastObserved.TryGetValue(faction, out Dictionary<Settlement, CampaignTime> seen))
                {
                    seen = new Dictionary<Settlement, CampaignTime>();
                    LastObserved[faction] = seen;
                }
                seen[settlement] = CampaignTime.Now;
            }
        }

        /// <summary>
        /// The Scouting skill of whoever scouts for this party - its designated
        /// scout if it has one, otherwise its leader, who is doing the job himself
        /// whether he is suited to it or not.
        /// </summary>
        private static float ScoutingSkillOf(MobileParty party, CoherentWarAISettings settings)
        {
            if (!settings.EnableScoutSkill)
            {
                // Treated as thoroughly competent, so the feature switches off into
                // uniform behaviour rather than into universal blindness.
                return ScoutingQuality.AccomplishedSkill;
            }

            Hero scout = party.EffectiveScout ?? party.LeaderHero;
            return scout?.GetSkillValue(DefaultSkills.Scouting) ?? 0f;
        }

        /// <summary>
        /// Works out what each realm believes about its enemies being committed
        /// elsewhere, from what its own scouts reported.
        ///
        /// A realm's overall size is common knowledge - you can see how many castles
        /// a kingdom holds. Where its army happens to be this week is not, and that
        /// is the part that has to be observed. So belief is the strength seen tied
        /// down, measured against a total that needs no scouting.
        /// </summary>
        private static void FormBeliefsAboutDistraction(CoherentWarAISettings settings)
        {
            Dictionary<IFaction, Dictionary<IFaction, float>> fresh
                = new Dictionary<IFaction, Dictionary<IFaction, float>>();

            foreach (Sighting sighting in Sightings)
            {
                if (!sighting.WasTiedDown || sighting.Observer == null || sighting.Seen == null)
                {
                    continue;
                }
                // Stale reports say nothing about where an army is now.
                if (SightingNetwork.Freshness(sighting.When.ElapsedHoursUntilNow,
                        settings.SightingLifetimeHours) <= 0f)
                {
                    continue;
                }

                if (!fresh.TryGetValue(sighting.Observer, out Dictionary<IFaction, float> beliefs))
                {
                    beliefs = new Dictionary<IFaction, float>();
                    fresh[sighting.Observer] = beliefs;
                }

                // Weighted by how good the scout was: a doubtful report of an army
                // being pinned down is a weaker basis for staking a campaign on.
                beliefs.TryGetValue(sighting.Seen, out float running);
                beliefs[sighting.Seen] = running + sighting.EnemyStrength * sighting.Confidence;
            }

            // Convert the observed totals into shares of each enemy's known size.
            foreach (KeyValuePair<IFaction, Dictionary<IFaction, float>> observer in fresh)
            {
                List<IFaction> enemies = new List<IFaction>(observer.Value.Keys);
                foreach (IFaction enemy in enemies)
                {
                    observer.Value[enemy] = ForceCommitment.DistractionRatio(
                        enemy.CurrentTotalStrength, observer.Value[enemy]);
                }
            }

            _believedDistraction = fresh;
        }

        private void OnHourlyTick()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableSightingNetwork)
            {
                if (Sightings.Count > 0)
                {
                    Sightings.Clear();
                    _reportedThreat = new Dictionary<Settlement, float>();
                }
                return;
            }

            float distanceUnit = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(
                MobileParty.NavigationType.Default);
            if (distanceUnit <= 0f)
            {
                return;
            }

            ExpireOldSightings(settings);
            ForgetDeadRealms();
            RecordNewSightings(settings, distanceUnit);
            SpreadToSettlements(settings, distanceUnit);
            FormBeliefsAboutDistraction(settings);
        }

        /// <summary>
        /// Drops what destroyed realms knew. Nothing reads it once they are gone, so
        /// this is only about not carrying every clan that ever formed and fell for
        /// the rest of a long campaign.
        /// </summary>
        private static void ForgetDeadRealms()
        {
            List<IFaction> gone = null;
            foreach (KeyValuePair<IFaction, Dictionary<Settlement, CampaignTime>> pair in LastObserved)
            {
                if (pair.Key == null || pair.Key.IsEliminated)
                {
                    if (gone == null)
                    {
                        gone = new List<IFaction>();
                    }
                    gone.Add(pair.Key);
                }
            }

            if (gone == null)
            {
                return;
            }
            foreach (IFaction faction in gone)
            {
                LastObserved.Remove(faction);
            }
        }

        private static void ExpireOldSightings(CoherentWarAISettings settings)
        {
            for (int i = Sightings.Count - 1; i >= 0; i--)
            {
                if (SightingNetwork.Freshness(Sightings[i].When.ElapsedHoursUntilNow,
                        settings.SightingLifetimeHours) <= 0f)
                {
                    Sightings.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Notes enemy armies that someone is close enough to have seen. Only
        /// forces worth reporting: a lone lord riding past is not an invasion.
        /// </summary>
        private static void RecordNewSightings(CoherentWarAISettings settings, float distanceUnit)
        {
            float baseRadius = distanceUnit * settings.SightingSpotRadiusFactor;

            foreach (MobileParty observer in MobileParty.AllLordParties)
            {
                if (observer == null || !observer.IsActive || observer.MapFaction == null)
                {
                    continue;
                }

                // A party sees as far as whoever scouts for it. Vanilla applies this
                // to the player's sight range but not to AI decisions, so an army
                // led by a gifted scout currently notices no more than one led by
                // nobody in particular.
                float scouting = ScoutingSkillOf(observer, settings);
                float spotRadius = baseRadius * ScoutingQuality.ReachMultiplier(scouting, settings.ScoutingReachBonus);
                float confidence = ScoutingQuality.Confidence(scouting, settings.ScoutingMinimumConfidence);

                NoteSettlementsInSight(observer, spotRadius);

                LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(
                    observer.Position.ToVec2(), spotRadius);

                for (MobileParty seen = MobileParty.FindNextLocatable(ref data);
                     seen != null;
                     seen = MobileParty.FindNextLocatable(ref data))
                {
                    if (seen?.Party == null || !seen.IsActive || seen.MapFaction == null)
                    {
                        continue;
                    }
                    if (!seen.MapFaction.IsAtWarWith(observer.MapFaction))
                    {
                        continue;
                    }

                    // Only the leader of an army is reported, or its strength would
                    // be counted once per attached party.
                    if (seen.Army != null && seen.Army.LeaderParty != seen)
                    {
                        continue;
                    }

                    float strength = seen.Army != null && seen.Army.LeaderParty == seen
                        ? seen.Army.EstimatedStrength
                        : seen.Party.EstimatedStrength;

                    if (strength < settings.SightingMinimumStrength)
                    {
                        continue;
                    }

                    Sightings.Add(new Sighting
                    {
                        Observer = observer.MapFaction,
                        Seen = seen.MapFaction,
                        Where = seen.Position,
                        EnemyStrength = strength,
                        WasTiedDown = WarCoordinatorBehavior.IsTiedDown(seen),
                        Confidence = confidence,
                        When = CampaignTime.Now
                    });
                }
            }
        }

        /// <summary>
        /// Works out how alarming the reports that have reached each settlement are.
        /// A settlement only counts a report once word could plausibly have got
        /// there, which is what makes the realm react outward from the sighting.
        /// </summary>
        private static void SpreadToSettlements(CoherentWarAISettings settings, float distanceUnit)
        {
            Dictionary<Settlement, float> fresh = new Dictionary<Settlement, float>();
            if (Sightings.Count == 0)
            {
                _reportedThreat = fresh;
                return;
            }

            float reach = distanceUnit * settings.SightingReachFactor;

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || !settlement.IsFortification || settlement.MapFaction == null)
                {
                    continue;
                }

                float total = 0f;
                for (int i = 0; i < Sightings.Count; i++)
                {
                    Sighting sighting = Sightings[i];

                    // Only the realm that saw it - and its allies - act on it.
                    if (sighting.Observer != settlement.MapFaction)
                    {
                        continue;
                    }

                    float hours = sighting.When.ElapsedHoursUntilNow;
                    float distance = settlement.Position.ToVec2().Distance(sighting.Where.ToVec2());

                    if (!SightingNetwork.HasReached(distance, hours, settings.SightingRelaySpeed, distanceUnit))
                    {
                        continue;
                    }

                    total += SightingNetwork.ThreatToPlace(
                        sighting.EnemyStrength, distance, reach, hours, settings.SightingLifetimeHours)
                        * sighting.Confidence;
                }

                if (total > 0f)
                {
                    fresh[settlement] = total;
                }
            }

            _reportedThreat = fresh;

            if (fresh.Count > 0)
            {
                WarAiStats.RecordSightings(Sightings.Count, fresh.Count);
            }
        }
    }
}
