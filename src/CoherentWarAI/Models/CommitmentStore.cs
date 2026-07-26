using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Models
{
    /// <summary>
    /// Remembers what each party last committed to, and how it rated that target
    /// when it could still see it clearly. This is what lets a lord act on his last
    /// assessment instead of re-deciding from scratch every tick.
    ///
    /// Deliberately transient: it is rebuilt from play and never saved, so the mod
    /// stays add/remove safe. Entries are pruned lazily - there is no tick here.
    /// </summary>
    public class CommitmentStore
    {
        private const int PruneThreshold = 256;

        private readonly Dictionary<MobileParty, Entry> _entries = new Dictionary<MobileParty, Entry>();

        private class Entry
        {
            public Settlement Target;
            public Army.ArmyTypes Mission;
            public float LastPositiveScore;
            public CampaignTime LastSeen;
            public CampaignTime CommittedAt;
        }

        /// <summary>Records a target the party can currently rate positively.</summary>
        public void Remember(MobileParty party, Settlement target, Army.ArmyTypes mission, float score)
        {
            if (party == null || target == null || score <= 0f)
            {
                return;
            }

            if (_entries.TryGetValue(party, out Entry entry) && entry.Target == target && entry.Mission == mission)
            {
                entry.LastPositiveScore = score;
                entry.LastSeen = CampaignTime.Now;
                return;
            }

            if (_entries.Count >= PruneThreshold)
            {
                Prune();
            }

            _entries[party] = new Entry
            {
                Target = target,
                Mission = mission,
                LastPositiveScore = score,
                LastSeen = CampaignTime.Now,
                CommittedAt = CampaignTime.Now
            };
        }

        /// <summary>
        /// Looks up a remembered assessment for this exact party/target/mission.
        /// Returns false when the party never committed here.
        /// </summary>
        public bool TryGet(MobileParty party, Settlement target, Army.ArmyTypes mission,
            out float lastPositiveScore, out float hoursSinceSeen, out float hoursSinceCommitted)
        {
            lastPositiveScore = 0f;
            hoursSinceSeen = 0f;
            hoursSinceCommitted = 0f;

            if (party == null || target == null)
            {
                return false;
            }
            if (!_entries.TryGetValue(party, out Entry entry) || entry.Target != target || entry.Mission != mission)
            {
                return false;
            }

            lastPositiveScore = entry.LastPositiveScore;
            hoursSinceSeen = entry.LastSeen.ElapsedHoursUntilNow;
            hoursSinceCommitted = entry.CommittedAt.ElapsedHoursUntilNow;
            return true;
        }

        /// <summary>Drops entries for parties that are gone or whose memory is stale.</summary>
        private void Prune()
        {
            List<MobileParty> stale = new List<MobileParty>();
            foreach (KeyValuePair<MobileParty, Entry> pair in _entries)
            {
                // Read from the live settings, not the default: the retention window
                // is configurable, and pruning must follow whatever it is set to.
                float decayHours = Settings.CoherentWarAISettings.Current?.RetentionDecayHours
                    ?? Logic.EngagementHysteresis.DefaultRetentionDecayHours;

                if (pair.Key == null || !pair.Key.IsActive
                    || pair.Value.LastSeen.ElapsedHoursUntilNow > decayHours * 2f)
                {
                    stale.Add(pair.Key);
                }
            }
            foreach (MobileParty party in stale)
            {
                _entries.Remove(party);
            }
        }
    }
}
