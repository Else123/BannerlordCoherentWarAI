using System;
using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Logic;
using CoherentWarAI.Models;
using CoherentWarAI.Settings;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// The single engine-facing connector for chain of command (Slice 1, widened in
    /// Slice 2a).
    ///
    /// Daily, each AI kingdom's three command domains - <see cref="Domain.Defense"/>,
    /// <see cref="Domain.Offense"/> and <see cref="Domain.Naval"/> - are each delegated
    /// to their own durable, term-held holder (SPEC SS3.3/SS3.4), through the shared
    /// <see cref="ProcessDomainDelegation"/> mechanism. Offense is staffed from
    /// <see cref="WarPostureBehavior"/>'s existing marshal path, which publishes its
    /// result here (<see cref="PublishOffenseHolders"/>) so <see cref="IsMarshal(Hero)"/>
    /// and <see cref="HoldsDomain"/> both see it. Naval's holder additionally gets patrol
    /// lanes from <see cref="NavalPatrolPlanner"/>; Defense and Offense are delegated but
    /// generate no order of their own in this slice (2b adds their verbs). Hourly, active
    /// patrol directives are re-issued: vanilla's think loop reverts a <c>SetMove*</c>
    /// order within hours, the lesson already paid for at
    /// <see cref="BanditHuntBehavior"/> (:119-126).
    ///
    /// Invariant P: the mod never generates an order on behalf of the player. A
    /// kingdom led by the player is skipped entirely; the player's own clan is never
    /// a candidate holder or addressee in any other kingdom either.
    ///
    /// SyncData stays empty: every domain holder and order here is re-derived daily,
    /// exactly like every other behavior's "derive, never persist" state (Slice 4
    /// adds persistence for player-made decisions only).
    /// </summary>
    public class CommandBehavior : CampaignBehaviorBase
    {
        /// <summary>Consecutive daily wins a challenger needs before unseating an incumbent early (SPEC SS3.4).</summary>
        private const int ChallengerStreakToUnseat = 3;

        /// <summary>How much a challenger must outscore the incumbent by, per daily tick, to count toward the streak above.</summary>
        private const float ChallengerMargin = 1.25f;

        private static Dictionary<Kingdom, DelegationTable> _delegations = new Dictionary<Kingdom, DelegationTable>();
        private static Dictionary<DomainKey<Hero>, Order> _orders = new Dictionary<DomainKey<Hero>, Order>();
        private static Dictionary<DomainKey<MobileParty>, Order> _ordersByParty = new Dictionary<DomainKey<MobileParty>, Order>();

        /// <summary>Every single-flag <see cref="Domain"/> value, for iterating the domain-agnostic readers over the (owner, Domain)-keyed stores.</summary>
        private static readonly Domain[] AllDomainFlags = { Domain.Defense, Domain.Offense, Domain.Naval };

        /// <summary>Today's offense-domain holders as published by <see cref="WarPostureBehavior.AppointMarshals"/>.</summary>
        private static Dictionary<Kingdom, HashSet<Hero>> _offenseHolders = new Dictionary<Kingdom, HashSet<Hero>>();

        /// <summary>Per (kingdom, domain) challenger streak for the 25%-over-3-days re-election rule.</summary>
        private static Dictionary<Kingdom, Dictionary<Domain, ChallengerStreak>> _challengers
            = new Dictionary<Kingdom, Dictionary<Domain, ChallengerStreak>>();

        private class ChallengerStreak
        {
            public Hero Challenger;
            public int ConsecutiveDays;
        }

        /// <summary>Whether this hero currently holds at least one command domain - Naval here, or Offense as published by <see cref="WarPostureBehavior"/>.</summary>
        public static bool IsMarshal(Hero hero)
        {
            if (hero == null)
            {
                return false;
            }
            Kingdom kingdom = hero.Clan != null ? hero.Clan.Kingdom : null;
            if (kingdom == null)
            {
                return false;
            }
            if (_offenseHolders.TryGetValue(kingdom, out HashSet<Hero> offenseHolders) && offenseHolders.Contains(hero))
            {
                return true;
            }
            return _delegations.TryGetValue(kingdom, out DelegationTable table) && table.IsMarshal(hero.StringId);
        }

        /// <summary>
        /// Whether this hero holds exactly <paramref name="domain"/> - "holds *this*
        /// domain", what army raising wants (decision D1), as opposed to
        /// <see cref="IsMarshal(Hero)"/>'s "holds *any* domain", what
        /// <see cref="BanditHuntBehavior"/> and <see cref="WarPostureBehavior.IsMarshal(MobileParty)"/>
        /// want. For <see cref="Domain.Offense"/> this also ORs in
        /// <see cref="_offenseHolders"/> - the realm's existing offensive-marshal path -
        /// so the set of lords who may raise an army stays exactly today's set.
        /// <paramref name="domain"/> must be a single flag (Defense, Offense or Naval) -
        /// a multi-flag combination or <see cref="Domain.None"/> throws via
        /// <see cref="DelegationTable.HolderFor"/>, unlike <see cref="IsMarshal(Hero)"/>,
        /// which never throws.
        /// </summary>
        public static bool HoldsDomain(Hero hero, Domain domain)
        {
            if (hero == null)
            {
                return false;
            }
            Kingdom kingdom = hero.Clan != null ? hero.Clan.Kingdom : null;
            if (kingdom == null)
            {
                return false;
            }
            if (domain == Domain.Offense && _offenseHolders.TryGetValue(kingdom, out HashSet<Hero> offenseHolders) && offenseHolders.Contains(hero))
            {
                return true;
            }
            return _delegations.TryGetValue(kingdom, out DelegationTable table) && table.HolderFor(domain) == hero.StringId;
        }

        /// <summary>Whether this party is bound by an active directive right now - the single predicate other subsystems yield to (SPEC SS5.2).</summary>
        public static bool HasDirective(MobileParty party)
        {
            if (party == null)
            {
                return false;
            }
            float nowHours = (float)CampaignTime.Now.ToHours;
            for (int i = 0; i < AllDomainFlags.Length; i++)
            {
                if (_ordersByParty.TryGetValue(new DomainKey<MobileParty>(party, AllDomainFlags[i]), out Order order) && order.IsBinding(nowHours))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Publishes today's offense-domain holders so <see cref="IsMarshal(Hero)"/> covers the existing marshal path too.</summary>
        public static void PublishOffenseHolders(Kingdom kingdom, IEnumerable<Hero> holders)
        {
            if (kingdom == null)
            {
                return;
            }
            HashSet<Hero> set = new HashSet<Hero>();
            if (holders != null)
            {
                foreach (Hero hero in holders)
                {
                    if (hero != null)
                    {
                        set.Add(hero);
                    }
                }
            }
            _offenseHolders[kingdom] = set;
        }

        /// <summary>
        /// Clears the Offense holders a previous campaign in this process left in the
        /// static. Done here rather than in <see cref="OnSessionLaunched"/>: the set is
        /// written by <see cref="WarPostureBehavior"/>'s own session-launch listener,
        /// and the engine runs session-launch listeners in an order this behavior does
        /// not control (reverse registration, and other mods add theirs). Behaviors are
        /// constructed in <c>OnGameStart</c>, before any campaign event fires, for a
        /// new game and for a load alike - so this reset can never discard a set that
        /// was already published this session.
        /// </summary>
        public CommandBehavior()
        {
            _offenseHolders = new Dictionary<Kingdom, HashSet<Hero>>();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnAfterSessionLaunched);
        }

        /// <summary>Every domain holder and order is derived daily; nothing to save (Slice 1).</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _delegations = new Dictionary<Kingdom, DelegationTable>();
            _orders = new Dictionary<DomainKey<Hero>, Order>();
            _ordersByParty = new Dictionary<DomainKey<MobileParty>, Order>();
            // _offenseHolders is deliberately not reset here - see the constructor.
            _challengers = new Dictionary<Kingdom, Dictionary<Domain, ChallengerStreak>>();
            // Statics survive a second campaign in the same process; the contention
            // counter must not carry yesterday's (or a previous campaign's) pending
            // writes into today's diagnostics.
            WarAiStats.ResetContention();

            // AC1/AC2: this line must name the real chain regardless of the feature
            // toggle below - it diagnoses model registration, not marshal doctrine.
            LogChain();

            OnDailyTick();
        }

        /// <summary>
        /// Reports the Offense holders in effect once every session-launch listener -
        /// ours and other mods' - has run, so the count reflects the final launch-time
        /// set whatever order those listeners ran in. The exact text is parsed by the
        /// test rig's log comparer; do not reword it.
        /// </summary>
        private void OnAfterSessionLaunched(CampaignGameStarter starter)
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableChainOfCommand)
            {
                return;
            }

            int kingdomsWithHolders = 0;
            foreach (HashSet<Hero> holders in _offenseHolders.Values)
            {
                if (holders.Count > 0)
                {
                    kingdomsWithHolders++;
                }
            }
            WarAiLog.Write("Command", string.Format(
                "Offense holders from session launch: {0} kingdom(s)", kingdomsWithHolders));
            WarAiLog.Flush();
        }

        /// <summary>
        /// Names each of our models' actual <c>BaseModel</c> so a with-DLC and a
        /// without-DLC campaign - and a third mod silently breaking the chain -
        /// can be diagnosed from one log line (SPEC SS8.1).
        /// </summary>
        private static void LogChain()
        {
            string targetScore = ModelName(Campaign.Current.Models.TargetScoreCalculatingModel);
            string garrison = ModelName(Campaign.Current.Models.SettlementGarrisonModel);
            string army = ModelName(Campaign.Current.Models.ArmyManagementCalculationModel);
            WarAiLog.Write("Command", string.Format(
                "Command chain: TargetScore->{0}, Garrison->{1}, Army->{2}", targetScore, garrison, army));
            WarAiLog.Flush();
        }

        private static string ModelName(object model)
        {
            CoherentTargetScoreModel targetScoreModel = model as CoherentTargetScoreModel;
            if (targetScoreModel != null)
            {
                return targetScoreModel.BaseModelName ?? "none";
            }
            CoherentGarrisonModel garrisonModel = model as CoherentGarrisonModel;
            if (garrisonModel != null)
            {
                return garrisonModel.BaseModelName ?? "none";
            }
            CoherentArmyManagementModel armyModel = model as CoherentArmyManagementModel;
            if (armyModel != null)
            {
                return armyModel.BaseModelName ?? "none";
            }
            // Not ours - a third mod registered after us. Naming it instead of
            // crashing is the whole point of this diagnostic (SPEC SS8.1 residual risk).
            return model != null ? model.GetType().Name + " (not ours)" : "none";
        }

        private void OnDailyTick()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableChainOfCommand)
            {
                ResetIfNotEmpty();
                return;
            }

            float nowHours = (float)CampaignTime.Now.ToHours;
            // Independent of EnableNavalOrders and any per-kingdom gate below (player-led,
            // eliminated): a stale order must stop binding its party even when generation
            // is skipped for it, or BanditHuntBehavior keeps yielding to a directive
            // nobody is dispatching any more.
            SweepExpiredOrders(nowHours);
            WarAiLog.Section(WarAiLog.GameDate() + " - chain of command");

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                {
                    continue;
                }
                // Invariant P: a player-led realm generates nothing - the player
                // must coordinate any domain he holds himself.
                if (kingdom.Leader == Hero.MainHero)
                {
                    continue;
                }

                List<MobileParty> navalCandidates = CollectNavalCandidates(kingdom, settings);
                List<MobileParty> defenseCandidates = CollectDefenseCandidates(kingdom, settings);
                float threatRatio = WarPostureBehavior.GetThreatRatio(kingdom);
                int aggressiveSlots = PosturePlanner.AggressiveSlotCount(
                    defenseCandidates.Count, threatRatio, settings.AggressiveShare, settings.MinimumDefenders);
                int ports = CountPorts(kingdom);
                Domain desired = DelegationPlanner.LimitToHolderBudget(
                    DelegationPlanner.DesiredDomains(aggressiveSlots, kingdom.FactionsAtWarWith.Count, threatRatio, ports, navalCandidates.Count),
                    settings.MaxMarshals);

                // Priority order per SPEC SS3.3: Offense, Naval, Defense. None of the
                // three takes a DelegationTable parameter - each writes _delegations[kingdom]
                // itself, so only the table re-read after the last call is still current.
                List<MobileParty> offenseCandidates = CollectOffenseCandidates(kingdom, settings);
                ProcessOffenseDelegation(kingdom, settings, nowHours, offenseCandidates, desired);
                ProcessNavalDelegation(kingdom, settings, nowHours, navalCandidates, desired);
                ProcessDefenseDelegation(kingdom, settings, nowHours, defenseCandidates, desired);
                DelegationTable table = GetOrCreateTable(kingdom);

                if (settings.EnableNavalOrders)
                {
                    GenerateNavalOrders(kingdom, table, navalCandidates, settings, nowHours);
                }
            }

            RebuildOrdersByParty();
            WarAiLog.Flush();
        }

        private static void ResetIfNotEmpty()
        {
            // Clear on the way out, or yesterday's delegations and orders linger
            // forever and keep binding parties other subsystems should own again.
            if (_delegations.Count > 0)
            {
                _delegations = new Dictionary<Kingdom, DelegationTable>();
            }
            if (_orders.Count > 0)
            {
                _orders = new Dictionary<DomainKey<Hero>, Order>();
            }
            if (_ordersByParty.Count > 0)
            {
                _ordersByParty = new Dictionary<DomainKey<MobileParty>, Order>();
            }
            if (_offenseHolders.Count > 0)
            {
                _offenseHolders = new Dictionary<Kingdom, HashSet<Hero>>();
            }
            if (_challengers.Count > 0)
            {
                _challengers = new Dictionary<Kingdom, Dictionary<Domain, ChallengerStreak>>();
            }
        }

        /// <summary>Thin caller: Naval eligibility comes straight off <paramref name="desired"/> (SPEC SS3.3 rule 2, computed by <see cref="DelegationPlanner.DesiredDomains"/>).</summary>
        private static DelegationTable ProcessNavalDelegation(
            Kingdom kingdom, CoherentWarAISettings settings, float nowHours, List<MobileParty> navalCandidates, Domain desired)
        {
            bool eligible = (desired & Domain.Naval) != 0;
            return ProcessDomainDelegation(kingdom, Domain.Naval, eligible, navalCandidates, settings, nowHours);
        }

        /// <summary>Thin caller: Defense eligibility comes straight off <paramref name="desired"/> (SPEC SS3.3 rule 3).</summary>
        private static DelegationTable ProcessDefenseDelegation(
            Kingdom kingdom, CoherentWarAISettings settings, float nowHours, List<MobileParty> defenseCandidates, Domain desired)
        {
            bool eligible = (desired & Domain.Defense) != 0;
            return ProcessDomainDelegation(kingdom, Domain.Defense, eligible, defenseCandidates, settings, nowHours);
        }

        /// <summary>Thin caller: Offense eligibility comes straight off <paramref name="desired"/> (SPEC SS3.3 rule 1).</summary>
        private static DelegationTable ProcessOffenseDelegation(
            Kingdom kingdom, CoherentWarAISettings settings, float nowHours, List<MobileParty> offenseCandidates, Domain desired)
        {
            bool eligible = (desired & Domain.Offense) != 0;
            return ProcessDomainDelegation(kingdom, Domain.Offense, eligible, offenseCandidates, settings, nowHours);
        }

        /// <summary>
        /// Decides who holds <paramref name="domain"/> for this kingdom today: reclaims
        /// when the realm no longer qualifies (<paramref name="eligible"/> is false, SPEC
        /// SS3.3 rule 2), appoints immediately when there is no valid incumbent, otherwise
        /// applies the term (<see cref="CoherentWarAISettings.MarshalTermDays"/>) and the
        /// 25%-over-3-days challenger rule, with the existing army-leader grandfathering
        /// (SPEC SS3.4, WarPostureBehavior.cs:187-191). Shared by all three domains -
        /// <paramref name="eligible"/> and <paramref name="candidates"/> are the only
        /// per-domain inputs, supplied by the three thin callers above.
        /// </summary>
        private static DelegationTable ProcessDomainDelegation(
            Kingdom kingdom, Domain domain, bool eligible, List<MobileParty> candidates, CoherentWarAISettings settings, float nowHours)
        {
            DelegationTable table = GetOrCreateTable(kingdom);
            string currentHolderId = table.HolderFor(domain);

            if (!eligible)
            {
                if (currentHolderId != "")
                {
                    table = table.WithReclaim(domain, nowHours);
                    _delegations[kingdom] = table;
                    ClearChallengerStreak(kingdom, domain);
                }
                return table;
            }

            Hero best = null;
            float bestScore = -1f;
            foreach (MobileParty candidate in candidates)
            {
                float score = CandidateScore(kingdom, candidate, settings);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate.LeaderHero;
                }
            }

            Hero currentHolder = FindByStringId(candidates, currentHolderId);
            if (currentHolder == null)
            {
                // No incumbent, or the old one is dead/captured/left/lost his ships -
                // any of those means he is no longer in candidates at all.
                return AppointHolder(kingdom, table, domain, best, nowHours);
            }
            if (currentHolder == best)
            {
                ClearChallengerStreak(kingdom, domain);
                return table;
            }

            // Term expiry is checked before army-leader grandfathering: grandfathering
            // protects an incumbent against being out-ranked, not against his term simply
            // ending (SPEC SS3.4) - an incumbent whose term is up is re-evaluated even
            // while he leads a host.
            bool termExpired = nowHours - GrantedAtHoursFor(table, domain) >= settings.MarshalTermDays * 24f;
            if (termExpired)
            {
                return AppointHolder(kingdom, table, domain, best, nowHours);
            }

            MobileParty currentHolderParty = FindPartyForHero(candidates, currentHolder);
            bool leadsHost = currentHolderParty != null
                && currentHolderParty.Army != null
                && currentHolderParty.Army.LeaderParty == currentHolderParty;
            if (leadsHost)
            {
                ClearChallengerStreak(kingdom, domain);
                return table;
            }

            float incumbentScore = CandidateScore(kingdom, currentHolderParty, settings);
            bool challengerAhead = incumbentScore > 0f && bestScore >= incumbentScore * ChallengerMargin;
            if (!challengerAhead)
            {
                ClearChallengerStreak(kingdom, domain);
                return table;
            }

            int streak = BumpChallengerStreak(kingdom, domain, best);
            return streak >= ChallengerStreakToUnseat ? AppointHolder(kingdom, table, domain, best, nowHours) : table;
        }

        private static DelegationTable AppointHolder(Kingdom kingdom, DelegationTable table, Domain domain, Hero holder, float nowHours)
        {
            if (holder == null)
            {
                // No eligible candidate this tick (e.g. Defense is desired but the realm
                // currently has no eligible war party): reclaim a dangling incumbent
                // instead of leaving him bound forever, or a dead/captured/left holder
                // would keep IsMarshal true and stay out of the bandit pool (m1,
                // post-review). Same reclaim shape as the !eligible branch above.
                if (table.HolderFor(domain) != "")
                {
                    table = table.WithReclaim(domain, nowHours);
                    _delegations[kingdom] = table;
                    ClearChallengerStreak(kingdom, domain);
                }
                return table;
            }
            string previousHolderId = table.HolderFor(domain);
            DelegationTable updated = table.WithDelegation(domain, holder.StringId, nowHours);
            _delegations[kingdom] = updated;
            ClearChallengerStreak(kingdom, domain);

            if (previousHolderId != holder.StringId)
            {
                WarAiLog.Write("Command", string.Format("{0} delegates {1} to {2}", kingdom.Name, domain, holder.Name));
            }
            return updated;
        }

        /// <summary>
        /// Generates this kingdom's <see cref="OrderVerb.Patrol"/> naval directives
        /// from <see cref="NavalPatrolPlanner"/> and addresses them to naval-capable
        /// lords, ranked by <see cref="NavalPatrolPlanner.RankPatrolAddressees"/> (the
        /// same command score used to pick the holder, with defenders prioritised over
        /// lords WarPostureBehavior already released to attack - doctrine "one attacks,
        /// the rest defend"). Clears
        /// yesterday's naval patrol orders for this kingdom's candidates first, so a
        /// lord dropped from the plan - or a realm that lost naval eligibility - does
        /// not keep a stale directive; never touches a <see cref="Provenance.Player"/>
        /// order either way (Invariant P) - this generator only ever produces AI orders,
        /// but the guard holds even if a future slice adds a second writer to <c>_orders</c>.
        /// </summary>
        private static void GenerateNavalOrders(
            Kingdom kingdom, DelegationTable table, List<MobileParty> navalCandidates, CoherentWarAISettings settings, float nowHours)
        {
            foreach (MobileParty candidate in navalCandidates)
            {
                Hero lord = candidate.LeaderHero;
                DomainKey<Hero> clearKey = new DomainKey<Hero>(lord, Domain.Naval);
                if (_orders.TryGetValue(clearKey, out Order existing) && existing.IsNaval && existing.Verb == OrderVerb.Patrol
                    && existing.Provenance != Provenance.Player)
                {
                    _orders.Remove(clearKey);
                }
            }

            string holderId = table.HolderFor(Domain.Naval);
            if (string.IsNullOrEmpty(holderId))
            {
                return;
            }
            Hero holder = FindByStringId(navalCandidates, holderId);
            if (holder == null)
            {
                return;
            }

            List<Settlement> portSettlements;
            PortSignal[] ports = BuildPortSignals(kingdom, out portSettlements);
            if (ports.Length == 0)
            {
                return;
            }

            float competence = MarshalPlanner.CommandCompetence(
                holder.GetSkillValue(DefaultSkills.Leadership), holder.GetSkillValue(DefaultSkills.Tactics));
            int wanted = NavalPatrolPlanner.PatrolCount(navalCandidates.Count, competence, settings.MaxNavalPatrolsPerRealm);
            PatrolLane[] lanes = NavalPatrolPlanner.SelectLanes(ports, wanted);
            if (lanes.Length == 0)
            {
                return;
            }

            int candidateCount = navalCandidates.Count;
            bool[] isAggressive = new bool[candidateCount];
            float[] scores = new float[candidateCount];
            bool[] leadsArmy = new bool[candidateCount];
            for (int i = 0; i < candidateCount; i++)
            {
                MobileParty candidate = navalCandidates[i];
                // Army leaders stay naval candidates - they may hold the Naval domain -
                // but are never addressed: dispatch drops their patrol the next hour
                // (NavalPatrolDispatch.DropLeadsArmy), which left the lane unmanned.
                leadsArmy[i] = LeadsArmy(candidate);
                // Two independent attacker signals: today's posture (only written when
                // EnableDefensivePosture is on - vanilla never sets PartyObjective) and the
                // realm's Offense holders. The second keeps this ranking meaningful when
                // posture is switched off, where every lord would otherwise read Neutral.
                isAggressive[i] = candidate.Objective == MobileParty.PartyObjective.Aggressive
                    || HoldsDomain(candidate.LeaderHero, Domain.Offense);
                scores[i] = CandidateScore(kingdom, candidate, settings);
            }
            // Doctrine ("one attacks, the rest defend"): WarPostureBehavior runs earlier
            // in this same daily tick and may already have released the realm's
            // strongest lords to attack. Those lords are also the highest-scoring naval
            // candidates, so a plain score sort could hand the offense marshal himself a
            // defensive patrol lane (found in a live 2026-09-10 log). Defenders are
            // ranked ahead of attackers here; an attacker is used only when there are
            // not enough defending candidates left to fill every lane.
            int[] rankedIndices = NavalPatrolPlanner.RankPatrolAddressees(isAggressive, scores, leadsArmy);
            List<MobileParty> pool = new List<MobileParty>(candidateCount);
            for (int i = 0; i < rankedIndices.Length; i++)
            {
                pool.Add(navalCandidates[rankedIndices[i]]);
            }

            int assignCount = lanes.Length < pool.Count ? lanes.Length : pool.Count;
            for (int i = 0; i < assignCount; i++)
            {
                MobileParty party = pool[i];
                Hero lord = party.LeaderHero;
                PatrolLane lane = lanes[i];

                // Deviation from SPEC SS4.1 (recorded, "O11: true mid-lane dispatch once a
                // navigability query is verified"): the lane's own midpoint (lane.X/Y) can
                // fall on land, so the party is dispatched to the anchor port's own
                // PortPosition instead - guaranteed to sit on the naval mesh - while the
                // midpoint stays the lane's ranking/logging/overlay point.
                Settlement anchorSettlement = portSettlements[lane.AnchorIndex];
                Vec2 dispatchPosition = anchorSettlement.PortPosition.ToVec2();

                Order order = new Order
                {
                    IssuerId = holder.StringId,
                    Scope = OrderScope.Lord,
                    AddresseeId = lord.StringId,
                    DomainScope = Domain.Naval,
                    Verb = OrderVerb.Patrol,
                    // The anchor port is the patrol's TargetSettlement at dispatch:
                    // vanilla's lord naval patrol cannot run without one (Order.HasPortAnchor).
                    TargetSettlementId = anchorSettlement.StringId,
                    TargetX = dispatchPosition.x,
                    TargetY = dispatchPosition.y,
                    IsNaval = true,
                    TargetIsPort = true,
                    Bindingness = Bindingness.Directive,
                    Priority = 2,
                    IssuedAtHours = nowHours,
                    ExpiresAtHours = nowHours + MarshalPlanner.OrderLifetimeHours(competence, settings.MarshalCommandWeight),
                    Provenance = kingdom.Leader == holder ? Provenance.AiKing : Provenance.AiMarshal
                };

                if (order.DomainScope == Domain.None)
                {
                    // Invariant: DomainScope must be a single real flag - HasDirective
                    // only probes AllDomainFlags, so a Domain.None order would bind
                    // nobody and never be seen by any reader (m2, post-review).
                    continue;
                }

                // Never blindly overwrite: an order only replaces whatever this lord
                // currently holds when it actually outranks it (SPEC SS3.1 Supersedes).
                // Supersedes has no domain dimension (ChainOfCommand.cs), so the bucket
                // itself - keyed off the order's own DomainScope - is what keeps two
                // domains' orders for the same lord from colliding.
                DomainKey<Hero> key = new DomainKey<Hero>(lord, order.DomainScope);
                if (!_orders.TryGetValue(key, out Order previous) || order.Supersedes(previous))
                {
                    _orders[key] = order;
                    WarAiLog.Write("Command", string.Format(
                        "{0} orders {1} to patrol ({2:F0}, {3:F0}) near {4}",
                        holder.Name, lord.Name, dispatchPosition.x, dispatchPosition.y, anchorSettlement.Name));
                }
            }
        }

        private void OnHourlyTick()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableChainOfCommand)
            {
                // Feature switched off mid-day: drop any orders issued earlier today so
                // HasDirective stops binding parties (BanditHuntBehavior yields to it).
                ResetIfNotEmpty();
                return;
            }

            float nowHours = (float)CampaignTime.Now.ToHours;
            // Independent of EnableNavalOrders below: a stale order must stop binding its
            // party even when dispatch is skipped this hour, or BanditHuntBehavior keeps
            // yielding to a directive nobody is dispatching any more.
            SweepExpiredOrders(nowHours);

            if (settings.EnableNavalOrders && _orders.Count > 0)
            {
                DispatchActiveOrders();
            }

            RebuildOrdersByParty();
        }

        /// <summary>
        /// Drops every order past its <c>ExpiresAtHours</c>, unconditionally - not gated by
        /// <c>EnableNavalOrders</c> or any per-kingdom state, so a stale directive always
        /// stops binding its party on both the daily and hourly paths even when order
        /// generation or dispatch is skipped for it.
        /// </summary>
        private static void SweepExpiredOrders(float nowHours)
        {
            List<DomainKey<Hero>> expired = null;
            foreach (KeyValuePair<DomainKey<Hero>, Order> pair in _orders)
            {
                if (!pair.Value.IsActive(nowHours))
                {
                    if (expired == null)
                    {
                        expired = new List<DomainKey<Hero>>();
                    }
                    expired.Add(pair.Key);
                }
            }
            if (expired != null)
            {
                foreach (DomainKey<Hero> key in expired)
                {
                    _orders.Remove(key);
                }
            }
        }

        /// <summary>Re-issues movement for each still-active directive; drops orders whose addressee no longer resolves to a live party, and naval patrols <see cref="NavalPatrolDispatch.Decide"/> rules undispatchable.</summary>
        private static void DispatchActiveOrders()
        {
            List<DomainKey<Hero>> toRemove = null;

            foreach (KeyValuePair<DomainKey<Hero>, Order> pair in _orders)
            {
                Hero lord = pair.Key.Owner;
                Order order = pair.Value;

                MobileParty party = lord.PartyBelongedTo;
                if (party == null || !party.IsActive || party.LeaderHero != lord)
                {
                    if (toRemove == null)
                    {
                        toRemove = new List<DomainKey<Hero>>();
                    }
                    toRemove.Add(pair.Key);
                    continue;
                }

                if (!IsEligibleForDispatch(party))
                {
                    continue;
                }

                if (order.IsNaval && order.Verb == OrderVerb.Patrol)
                {
                    // A lord's naval patrol must be anchored to a port settlement:
                    // MobilePartyAi.GetNavalPatrolBehavior reads TargetSettlement.PortPosition
                    // while the lord is still ashore, and NavalDLC's GetPatrolRadius reads
                    // TargetSettlement.MapFaction once he is at sea. SetMovePatrolAroundPoint
                    // clears TargetSettlement, which crashed the parallel AI tick.
                    Settlement anchor = order.HasPortAnchor ? Settlement.Find(order.TargetSettlementId) : null;
                    // Re-checked at dispatch, not only at issue: a lord can lose his last
                    // ship between the two (SPEC SS4.1).
                    bool hasNavalCapability = party.HasNavalNavigationCapability;

                    // Same route vanilla's AiPatrollingBehavior + AiPartyThinkBehavior take
                    // for a naval patrol: best navigation type to the port (Naval, or All
                    // for a lord who must first walk to a port) plus the exit-from-port flag.
                    MobileParty.NavigationType navigationType = MobileParty.NavigationType.None;
                    bool isFromPort = false;
                    if (anchor != null && anchor.HasPort && hasNavalCapability)
                    {
                        AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(
                            party, anchor, true, out navigationType, out float _, out isFromPort);
                    }

                    NavalDispatchVerdict verdict = NavalPatrolDispatch.Decide(new NavalDispatchFacts
                    {
                        // Army leaders are dropped, never dispatched: vanilla's patrol action
                        // sets Army.ArmyType = Defender and Army.AiBehaviorObject on every
                        // call, which would rewrite what the army does each hour (D1/AC25a).
                        LeadsArmy = LeadsArmy(party),
                        HasPortAnchor = order.HasPortAnchor,
                        AnchorFound = anchor != null,
                        AnchorHasPort = anchor != null && anchor.HasPort,
                        AnchorOwnedByPartyFaction = anchor != null && anchor.MapFaction == party.MapFaction,
                        HasNavalCapability = hasNavalCapability,
                        HasRoute = navigationType != MobileParty.NavigationType.None
                    });

                    if (NavalPatrolDispatch.IsDrop(verdict))
                    {
                        // Logged once per order: removal guarantees it is not seen again.
                        WarAiLog.Write("Command", string.Format(
                            "{0} drops naval patrol at {1}: {2}",
                            lord.Name, anchor != null ? anchor.Name.ToString() : order.TargetSettlementId, verdict));
                        if (toRemove == null)
                        {
                            toRemove = new List<DomainKey<Hero>>();
                        }
                        toRemove.Add(pair.Key);
                        continue;
                    }
                    if (verdict != NavalDispatchVerdict.Dispatch)
                    {
                        continue;
                    }

                    // Vanilla's think loop reverts SetMove* within hours, so this is
                    // re-applied every hour rather than trusted to stick - the lesson
                    // already paid for at BanditHuntBehavior.cs:119-126. SetPartyAiAction
                    // only rewrites movement when it differs, so an ongoing patrol is not
                    // reset back to its centre each hour - as long as AiHelper keeps
                    // returning the same navigation type. A Naval/All flip near the coast
                    // differs and resets the patrol once, which is harmless.
                    SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, anchor, navigationType, isFromPort, true);
                    // Domain-qualified so the contention counter can see two of our own
                    // domain generators racing on one party (WarAiStats.cs, untouched by
                    // this slice). 2b further qualifies this by verb once a second Naval
                    // generator exists ("Command:Naval:Defend") - not built here, since
                    // with exactly one verb the extra qualifier would be unreachable.
                    WarAiStats.RecordPartyWrite(party, "Command:" + order.DomainScope, WarAiStats.WriteChannel.Movement);
                }
            }

            if (toRemove != null)
            {
                foreach (DomainKey<Hero> key in toRemove)
                {
                    _orders.Remove(key);
                }
            }
        }

        /// <summary>Whether this party leads an army - the one condition under which a naval patrol is never addressed nor dispatched.</summary>
        private static bool LeadsArmy(MobileParty party)
        {
            return party.Army != null && party.Army.LeaderParty == party;
        }

        /// <summary>Not tied down by a siege/battle, and either independent or leading its own army (SPEC SS5.2).</summary>
        private static bool IsEligibleForDispatch(MobileParty party)
        {
            if (party.Army != null && party.Army.LeaderParty != party)
            {
                return false;
            }
            return !WarCoordinatorBehavior.IsTiedDown(party);
        }

        private static void RebuildOrdersByParty()
        {
            _ordersByParty.Clear();
            foreach (KeyValuePair<DomainKey<Hero>, Order> pair in _orders)
            {
                MobileParty party = pair.Key.Owner.PartyBelongedTo;
                if (party != null)
                {
                    _ordersByParty[new DomainKey<MobileParty>(party, pair.Value.DomainScope)] = pair.Value;
                }
            }
        }

        /// <summary>
        /// Naval-capable, non-player lords this kingdom could hand the fleet to -
        /// <see cref="WarPostureBehavior.IsEligibleCandidate"/>'s filter (no main
        /// party, no attached army member, no disbanding/leaderless party) plus the
        /// two filters specific to this domain: never the player (Invariant P,
        /// unconditionally - stronger than <c>ManagePlayerClanParties</c>, which only
        /// governs posture/bandit duty), and only lords who actually own a ship.
        /// </summary>
        private static List<MobileParty> CollectNavalCandidates(Kingdom kingdom, CoherentWarAISettings settings)
        {
            List<MobileParty> result = new List<MobileParty>();
            foreach (WarPartyComponent component in kingdom.WarPartyComponents)
            {
                MobileParty party = component != null ? component.MobileParty : null;
                if (!WarPostureBehavior.IsEligibleCandidate(party, settings))
                {
                    continue;
                }
                if (party.LeaderHero.Clan == Clan.PlayerClan)
                {
                    continue;
                }
                if (!party.HasNavalNavigationCapability)
                {
                    continue;
                }
                result.Add(party);
            }
            return result;
        }

        /// <summary>
        /// Non-player lords eligible to hold <see cref="Domain.Defense"/> -
        /// <see cref="WarPostureBehavior.IsEligibleCandidate"/>'s filter plus the
        /// unconditional player-clan exclusion (Invariant P), with no further narrowing:
        /// any eligible lord is a defense candidate (SPEC SS3.4).
        /// </summary>
        private static List<MobileParty> CollectDefenseCandidates(Kingdom kingdom, CoherentWarAISettings settings)
        {
            List<MobileParty> result = new List<MobileParty>();
            foreach (WarPartyComponent component in kingdom.WarPartyComponents)
            {
                MobileParty party = component != null ? component.MobileParty : null;
                if (!WarPostureBehavior.IsEligibleCandidate(party, settings))
                {
                    continue;
                }
                if (party.LeaderHero.Clan == Clan.PlayerClan)
                {
                    continue;
                }
                result.Add(party);
            }
            return result;
        }

        /// <summary>
        /// Non-player lords eligible to hold <see cref="Domain.Offense"/> - drawn from
        /// today's offense marshals (<see cref="_offenseHolders"/>, published by
        /// <see cref="WarPostureBehavior.AppointMarshals"/> earlier the same daily tick,
        /// SPEC SS3.4 "as today"), re-filtered through
        /// <see cref="WarPostureBehavior.IsEligibleCandidate"/> and the player-clan
        /// exclusion so a stale entry never becomes a ghost holder.
        /// </summary>
        private static List<MobileParty> CollectOffenseCandidates(Kingdom kingdom, CoherentWarAISettings settings)
        {
            List<MobileParty> result = new List<MobileParty>();
            if (!_offenseHolders.TryGetValue(kingdom, out HashSet<Hero> holders))
            {
                return result;
            }
            foreach (Hero hero in holders)
            {
                MobileParty party = hero != null ? hero.PartyBelongedTo : null;
                if (!WarPostureBehavior.IsEligibleCandidate(party, settings))
                {
                    continue;
                }
                if (party.LeaderHero != hero || party.LeaderHero.Clan == Clan.PlayerClan)
                {
                    continue;
                }
                result.Add(party);
            }
            return result;
        }

        private static int CountPorts(Kingdom kingdom)
        {
            int count = 0;
            foreach (Settlement settlement in kingdom.Settlements)
            {
                if (settlement != null && settlement.HasPort)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// <paramref name="portSettlements"/> is parallel to the returned array (same
        /// index order) - <c>PatrolLane.AnchorIndex</c> indexes back into it so the
        /// connector can resolve the anchor port's own settlement (M5, post-review).
        /// </summary>
        private static PortSignal[] BuildPortSignals(Kingdom kingdom, out List<Settlement> portSettlements)
        {
            List<PortSignal> signals = new List<PortSignal>();
            portSettlements = new List<Settlement>();
            foreach (Settlement settlement in kingdom.Settlements)
            {
                if (settlement == null || !settlement.HasPort)
                {
                    continue;
                }
                Vec2 position = settlement.PortPosition.ToVec2();
                signals.Add(new PortSignal(
                    position.x, position.y,
                    settlement.NearbyNavalThreatIntensity, settlement.NearbyNavalAllyIntensity));
                portSettlements.Add(settlement);
            }
            return signals.ToArray();
        }

        /// <summary>
        /// <c>MarshalSuitability</c> (strength/Valor/ruler) scaled by command
        /// competence (Leadership/Tactics) - the exact SPEC SS3.4 formula, reusing
        /// the doctrine weights so Naval selection feels like the same institution
        /// as offensive marshal selection.
        /// </summary>
        private static float CandidateScore(Kingdom kingdom, MobileParty party, CoherentWarAISettings settings)
        {
            Hero hero = party.LeaderHero;
            bool isRuler = kingdom.Leader != null && hero == kingdom.Leader;
            float suitability = MarshalPlanner.MarshalSuitability(
                party.Party.EstimatedStrength,
                hero.GetTraitLevel(DefaultTraits.Valor),
                isRuler,
                settings.ValorWeight,
                settings.RulerBonus);
            float competence = MarshalPlanner.CommandCompetence(
                hero.GetSkillValue(DefaultSkills.Leadership), hero.GetSkillValue(DefaultSkills.Tactics));
            return suitability * MarshalPlanner.CommandBonus(competence, settings.MarshalCommandWeight);
        }

        private static Hero FindByStringId(List<MobileParty> candidates, string heroId)
        {
            if (string.IsNullOrEmpty(heroId))
            {
                return null;
            }
            foreach (MobileParty party in candidates)
            {
                if (party.LeaderHero.StringId == heroId)
                {
                    return party.LeaderHero;
                }
            }
            return null;
        }

        private static MobileParty FindPartyForHero(List<MobileParty> candidates, Hero hero)
        {
            foreach (MobileParty party in candidates)
            {
                if (party.LeaderHero == hero)
                {
                    return party;
                }
            }
            return null;
        }

        private static DelegationTable GetOrCreateTable(Kingdom kingdom)
        {
            string kingId = kingdom.Leader != null ? kingdom.Leader.StringId : "";
            if (_delegations.TryGetValue(kingdom, out DelegationTable table))
            {
                if (table.KingId == kingId)
                {
                    return table;
                }
                // The throne changed hands - carry existing entries forward under the new king.
                DelegationTable rebuilt = new DelegationTable(kingId, table.NavalDelegatedSeparately);
                foreach (DelegationEntry entry in table.Entries)
                {
                    rebuilt = rebuilt.WithDelegation(entry.Domain, entry.HolderId, entry.GrantedAtHours);
                }
                _delegations[kingdom] = rebuilt;
                return rebuilt;
            }

            DelegationTable fresh = new DelegationTable(kingId, navalDelegatedSeparately: true);
            _delegations[kingdom] = fresh;
            return fresh;
        }

        private static float GrantedAtHoursFor(DelegationTable table, Domain domain)
        {
            foreach (DelegationEntry entry in table.Entries)
            {
                if (entry.Domain == domain)
                {
                    return entry.GrantedAtHours;
                }
            }
            return 0f;
        }

        private static int BumpChallengerStreak(Kingdom kingdom, Domain domain, Hero challenger)
        {
            if (!_challengers.TryGetValue(kingdom, out Dictionary<Domain, ChallengerStreak> byDomain))
            {
                byDomain = new Dictionary<Domain, ChallengerStreak>();
                _challengers[kingdom] = byDomain;
            }
            if (!byDomain.TryGetValue(domain, out ChallengerStreak streak) || streak.Challenger != challenger)
            {
                streak = new ChallengerStreak { Challenger = challenger, ConsecutiveDays = 0 };
                byDomain[domain] = streak;
            }
            streak.ConsecutiveDays++;
            return streak.ConsecutiveDays;
        }

        private static void ClearChallengerStreak(Kingdom kingdom, Domain domain)
        {
            if (_challengers.TryGetValue(kingdom, out Dictionary<Domain, ChallengerStreak> byDomain))
            {
                byDomain.Remove(domain);
            }
        }
    }
}
