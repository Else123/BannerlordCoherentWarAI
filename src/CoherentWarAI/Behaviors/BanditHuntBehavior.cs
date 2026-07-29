using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Sends idle defenders after bandits.
    ///
    /// Vanilla lords never seek bandits out: every AI behaviour that picks a target
    /// excludes bandit parties outright, so lords only fight them by walking into
    /// them. Meanwhile lords held back for defence often have nothing to do but
    /// patrol, while bandits burn villages and troops gain no experience.
    ///
    /// Only genuinely idle defenders go, and only when the realm has no real war to
    /// fight - an evenly matched enemy needs every lord. A hunt is left alone for a
    /// few hours once begun, so lords catch a band instead of drifting between them.
    ///
    /// This is the one place the mod overrides a party's movement rather than
    /// weighting vanilla's own choice, so it deliberately only acts on parties that
    /// have nothing else to do.
    /// </summary>
    public class BanditHuntBehavior : CampaignBehaviorBase
    {
        private readonly Dictionary<MobileParty, CampaignTime> _huntStarted = new Dictionary<MobileParty, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        /// <summary>Hunts are re-derived from live state; nothing to save.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnHourlyTick()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableBanditHunting)
            {
                return;
            }

            int dispatched = 0;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                {
                    continue;
                }
                if (!RealmCanSpareLords(kingdom, settings))
                {
                    continue;
                }

                dispatched += DispatchHunters(kingdom, settings);
            }

            if (dispatched > 0)
            {
                WarAiStats.RecordBanditHunts(dispatched);
            }
        }

        /// <summary>
        /// Whether the realm has room for policing: no serious threat at home, and
        /// no war against an enemy who is anything like a match for it.
        /// </summary>
        private static bool RealmCanSpareLords(Kingdom kingdom, CoherentWarAISettings settings)
        {
            float threatRatio = WarPostureBehavior.GetThreatRatio(kingdom);

            float enemyStrength = 0f;
            for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
            {
                IFaction enemy = kingdom.FactionsAtWarWith[i];
                if (enemy != null && enemy.CurrentTotalStrength > enemyStrength)
                {
                    enemyStrength = enemy.CurrentTotalStrength;
                }
            }

            return BanditHuntPlanner.RealmMaySpareLords(
                threatRatio, kingdom.CurrentTotalStrength, enemyStrength,
                settings.BanditMaxThreatRatio, settings.BanditRequiredSuperiority);
        }

        private int DispatchHunters(Kingdom kingdom, CoherentWarAISettings settings)
        {
            int dispatched = 0;

            foreach (WarPartyComponent component in kingdom.WarPartyComponents)
            {
                MobileParty party = component?.MobileParty;
                if (!IsEligibleHunter(party, settings))
                {
                    continue;
                }

                bool onHunt = _huntStarted.TryGetValue(party, out CampaignTime started);

                // Give up on a band that cannot be caught rather than chasing it
                // across the map indefinitely.
                if (onHunt && !BanditHuntPlanner.HuntStillWorthPursuing(
                        started.ElapsedHoursUntilNow, settings.BanditHuntCommitmentHours))
                {
                    _huntStarted.Remove(party);
                    continue;
                }

                // Still closing on the same band: nothing to do. Vanilla's think loop
                // reverts the order every few hours (it never scores bandits, so the
                // objective reads as worthless to it), which is why this re-issues
                // the order instead of trusting it to stick.
                if (onHunt && IsStillChasingBandits(party))
                {
                    continue;
                }

                MobileParty quarry = FindQuarry(party, settings);
                if (quarry == null)
                {
                    _huntStarted.Remove(party);
                    continue;
                }

                party.SetMoveEngageParty(quarry, party.NavigationCapability);
                if (!onHunt)
                {
                    // Timestamp the chase, not each re-issue, so the give-up bound
                    // measures the whole pursuit.
                    _huntStarted[party] = CampaignTime.Now;
                    dispatched++;
                }
            }

            PruneFinishedHunts();
            return dispatched;
        }

        /// <summary>Whether the party is still engaging the band we sent it after.</summary>
        private static bool IsStillChasingBandits(MobileParty party)
        {
            return party.DefaultBehavior == AiBehavior.EngageParty
                && party.TargetParty != null
                && party.TargetParty.IsBandit
                && party.TargetParty.IsActive;
        }

        /// <summary>Drops entries for parties that are gone, so dead lords are not held.</summary>
        private void PruneFinishedHunts()
        {
            if (_huntStarted.Count < 64)
            {
                return;
            }

            List<MobileParty> stale = new List<MobileParty>();
            foreach (KeyValuePair<MobileParty, CampaignTime> pair in _huntStarted)
            {
                if (pair.Key == null || !pair.Key.IsActive)
                {
                    stale.Add(pair.Key);
                }
            }
            foreach (MobileParty party in stale)
            {
                _huntStarted.Remove(party);
            }
        }

        /// <summary>
        /// Idle defenders only: anyone released for the offensive, in an army,
        /// leading one, or already pursuing something of their own stays put.
        /// </summary>
        private static bool IsEligibleHunter(MobileParty party, CoherentWarAISettings settings)
        {
            if (party == null || !party.IsActive || party == MobileParty.MainParty
                || party.LeaderHero == null || party.Party == null)
            {
                return false;
            }
            if (party.IsDisbanding || party.MapEvent != null || party.BesiegedSettlement != null)
            {
                return false;
            }
            if (party.LeaderHero.Clan == Clan.PlayerClan && !settings.ManagePlayerClanParties)
            {
                return false;
            }
            // Respect the same gate vanilla uses before touching a party's plans;
            // these flags mark parties driven by quests or scripted sequences.
            if (party.Ai == null || party.Ai.IsDisabled || party.Ai.DoNotMakeNewDecisions)
            {
                return false;
            }

            // Idling or patrolling means free. Engaging does NOT: vanilla gives
            // defensive lords a bonus for intercepting real enemy parties, which is
            // exactly the behaviour the defence-first posture exists to produce.
            // Only an engagement against bandits - ours, or one already under way -
            // counts as available.
            bool hasOwnObjective;
            switch (party.DefaultBehavior)
            {
                case AiBehavior.Hold:
                case AiBehavior.PatrolAroundPoint:
                    hasOwnObjective = false;
                    break;
                case AiBehavior.EngageParty:
                    hasOwnObjective = !IsStillChasingBandits(party);
                    break;
                default:
                    hasOwnObjective = true;
                    break;
            }

            // Objectives only exist because this mod assigns them - vanilla never
            // calls SetPartyObjective at all. With the posture feature off nobody is
            // ever Defensive, so requiring it would make bandit hunting a switch
            // that silently does nothing. Then an unoccupied lord is simply one
            // vanilla has given no objective.
            bool countsAsDefensive = settings.EnableDefensivePosture
                ? party.Objective == MobileParty.PartyObjective.Defensive
                : party.Objective != MobileParty.PartyObjective.Aggressive;

            return BanditHuntPlanner.LordIsAvailable(
                countsAsDefensive,
                party.Army != null,
                WarPostureBehavior.IsMarshal(party),
                hasOwnObjective);
        }

        /// <summary>
        /// Best bandit band within reach: the largest this lord can still take
        /// comfortably, since bigger bands do more harm and teach troops more.
        /// </summary>
        private static MobileParty FindQuarry(MobileParty hunter, CoherentWarAISettings settings)
        {
            // Typical spacing between neighbouring towns - the unit vanilla itself
            // uses for "nearby". The map-wide maximum distance would have every
            // hunter sweeping essentially the whole map every hour.
            float radius = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(
                MobileParty.NavigationType.Default) * settings.BanditSearchRadiusFactor;
            if (radius <= 0f)
            {
                return null;
            }

            float ourStrength = hunter.Party.EstimatedStrength;
            MobileParty best = null;
            float bestValue = 0f;

            LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(
                hunter.Position.ToVec2(), radius);

            for (MobileParty candidate = MobileParty.FindNextLocatable(ref data);
                 candidate != null;
                 candidate = MobileParty.FindNextLocatable(ref data))
            {
                // Boss parties belong to hideout clearing, which is a quest-driven
                // affair with its own rules - not a field engagement.
                if (!candidate.IsBandit || !candidate.IsActive || candidate.IsBanditBossParty
                    || candidate.Party == null)
                {
                    continue;
                }
                // Bandits sitting in a hideout are a different problem entirely.
                if (candidate.CurrentSettlement != null || candidate.MapEvent != null)
                {
                    continue;
                }

                float value = BanditHuntPlanner.QuarryValue(
                    ourStrength, candidate.Party.EstimatedStrength, settings.BanditRequiredAdvantage);

                if (value > bestValue)
                {
                    bestValue = value;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
