using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Keeps track of what each realm is already committed to, so its lords stop
    /// converging on the same fief.
    ///
    /// Vanilla scores targets for each lord in isolation - there is no term anywhere
    /// in the offensive scoring for "how much of our army is already going there".
    /// So whichever fief looks best to one lord looks best to all of them. This
    /// records committed strength per target and per realm, and the target-score
    /// model reads it to push surplus attackers elsewhere.
    ///
    /// Realms are tracked separately: two kingdoms racing for the same castle are
    /// competing, not piling on, and must not damp each other.
    ///
    /// Derived from live party state each hour; nothing is persisted.
    /// </summary>
    public class WarCoordinatorBehavior : CampaignBehaviorBase
    {
        private static Dictionary<Settlement, Dictionary<IFaction, float>> _committed
            = new Dictionary<Settlement, Dictionary<IFaction, float>>();

        private static Dictionary<IFaction, IFaction> _primaryEnemies = new Dictionary<IFaction, IFaction>();

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Recompute);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, ChoosePrimaryEnemies);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        /// <summary>
        /// Whether this is the enemy the realm has decided to finish first. A
        /// kingdom at war on three fronts otherwise dribbles a party at each and
        /// concludes none of them.
        /// </summary>
        public static bool IsPrimaryEnemy(IFaction ourFaction, IFaction targetFaction)
        {
            if (ourFaction == null || targetFaction == null)
            {
                return false;
            }
            return _primaryEnemies.TryGetValue(ourFaction, out IFaction primary) && primary == targetFaction;
        }

        /// <summary>
        /// Picks one war per realm to press. The choice is where our territory
        /// actually meets theirs: the front with the most contact is the one that
        /// can be pushed, and the one that hurts most if left alone.
        /// </summary>
        private void ChoosePrimaryEnemies()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableEnemyFocus)
            {
                if (_primaryEnemies.Count > 0)
                {
                    _primaryEnemies = new Dictionary<IFaction, IFaction>();
                }
                return;
            }

            Dictionary<IFaction, IFaction> fresh = new Dictionary<IFaction, IFaction>();

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated || kingdom.FactionsAtWarWith.Count == 0)
                {
                    continue;
                }

                IFaction best = null;
                float bestScore = 0f;
                int bestContact = 0;
                float bestStrength = 0f;

                // Fallback for a realm whose wars share no land border at all -
                // fought purely at sea, or against landless clans. Without it every
                // one of its wars would rank secondary for want of a comparison.
                IFaction strongestReachless = null;
                float strongestReachlessStrength = -1f;

                for (int i = 0; i < kingdom.FactionsAtWarWith.Count; i++)
                {
                    IFaction enemy = kingdom.FactionsAtWarWith[i];
                    if (enemy == null)
                    {
                        continue;
                    }

                    int contact = CountSharedBorders(kingdom, enemy);
                    float strength = enemy.CurrentTotalStrength;
                    float score = StrategicPriority.PrimaryEnemyScore(strength, contact, settings.BorderWeight);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = enemy;
                        bestContact = contact;
                        bestStrength = strength;
                    }
                    else if (score <= 0f && strength > strongestReachlessStrength)
                    {
                        strongestReachlessStrength = strength;
                        strongestReachless = enemy;
                    }
                }

                if (best == null)
                {
                    best = strongestReachless;
                }

                if (best != null)
                {
                    fresh[kingdom] = best;
                    WarAiLog.Write("Focus", string.Format(
                        "{0} concentrates on {1} (strength {2:F0}, {3} shared borders) out of {4} wars",
                        kingdom.Name, best.Name, bestStrength, bestContact, kingdom.FactionsAtWarWith.Count));
                }
            }

            _primaryEnemies = fresh;
        }

        /// <summary>Fortifications of ours that directly adjoin theirs.</summary>
        private static int CountSharedBorders(IFaction ours, IFaction theirs)
        {
            int contact = 0;
            foreach (Town fief in ours.Fiefs)
            {
                Settlement settlement = fief?.Settlement;
                if (settlement?.Town == null)
                {
                    continue;
                }

                foreach (Settlement neighbor in Models.SettlementNeighbors.Of(settlement))
                {
                    if (neighbor.MapFaction == theirs)
                    {
                        contact++;
                    }
                }
            }
            return contact;
        }

        /// <summary>Derived from live state; nothing to save.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Recompute();
            ChoosePrimaryEnemies();
        }

        /// <summary>
        /// Strength of the same realm already heading for a target, not counting the
        /// asking party - a lord must not be pushed off a target by his own presence.
        /// </summary>
        public static float GetCommittedStrengthExcluding(Settlement target, MobileParty party)
        {
            if (target == null || party == null)
            {
                return 0f;
            }
            if (!_committed.TryGetValue(target, out Dictionary<IFaction, float> byFaction))
            {
                return 0f;
            }

            IFaction faction = party.MapFaction;
            if (faction == null || !byFaction.TryGetValue(faction, out float total))
            {
                return 0f;
            }

            if (GetCommittedTarget(party) == target)
            {
                total -= GetPartyStrength(party);
            }
            return total < 0f ? 0f : total;
        }

        private void Recompute()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableCoordination)
            {
                if (_committed.Count > 0)
                {
                    _committed = new Dictionary<Settlement, Dictionary<IFaction, float>>();
                }
                return;
            }

            Dictionary<Settlement, Dictionary<IFaction, float>> fresh
                = new Dictionary<Settlement, Dictionary<IFaction, float>>();

            foreach (MobileParty party in MobileParty.AllLordParties)
            {
                if (party == null || !party.IsActive || party.MapFaction == null)
                {
                    continue;
                }

                // Army members ride with their leader; counting them separately
                // would double-count the same men.
                if (party.Army != null && party.Army.LeaderParty != party)
                {
                    continue;
                }

                Settlement target = GetCommittedTarget(party);
                if (target == null)
                {
                    continue;
                }

                if (!fresh.TryGetValue(target, out Dictionary<IFaction, float> byFaction))
                {
                    byFaction = new Dictionary<IFaction, float>();
                    fresh[target] = byFaction;
                }

                IFaction faction = party.MapFaction;
                byFaction.TryGetValue(faction, out float running);
                byFaction[faction] = running + GetPartyStrength(party);
            }

            _committed = fresh;
        }

        /// <summary>
        /// The settlement a party is actually committed to attacking, or null. Only
        /// offensive intent counts - a lord passing by is not a claim.
        /// </summary>
        public static Settlement GetCommittedTarget(MobileParty party)
        {
            if (party.BesiegedSettlement != null)
            {
                return party.BesiegedSettlement;
            }
            if (party.DefaultBehavior == AiBehavior.BesiegeSettlement || party.DefaultBehavior == AiBehavior.RaidSettlement)
            {
                return party.TargetSettlement;
            }
            return null;
        }

        /// <summary>
        /// Strength a party represents. Members of an army report the whole army's
        /// strength, not their own share: that is what was recorded under the army
        /// leader, so subtracting anything else would leave a party seeing its own
        /// army as somebody else's commitment.
        /// </summary>
        private static float GetPartyStrength(MobileParty party)
        {
            if (party.Army != null)
            {
                return party.Army.EstimatedStrength;
            }
            return party.Party?.EstimatedStrength ?? 0f;
        }
    }
}
