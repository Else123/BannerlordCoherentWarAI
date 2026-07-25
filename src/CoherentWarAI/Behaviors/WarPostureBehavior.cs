using System.Collections.Generic;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Slice B-def - defense-first posture.
    ///
    /// Vanilla never assigns a party objective to AI lords (nothing in the campaign
    /// assembly calls <c>SetPartyObjective</c>), so every lord stays Neutral and
    /// nobody holds their own territory; defense only reacts once a settlement is
    /// already under attack. This behavior assigns objectives explicitly: most
    /// lords default to defending, and only a limited, threat-scaled number are
    /// released to attack. Vanilla scoring already honors the objective, so no
    /// Harmony patch is needed.
    ///
    /// Runs once a day per kingdom. Nothing is persisted - the posture is recomputed
    /// from live state, so the mod stays save-compatible.
    /// </summary>
    public class WarPostureBehavior : CampaignBehaviorBase
    {
        private readonly List<MobileParty> _candidates = new List<MobileParty>();
        private readonly List<float> _scores = new List<float>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        /// <summary>Posture is derived from live state; nothing to save.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTick()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableDefensivePosture)
            {
                return;
            }

            WarAiLog.Section(WarAiLog.GameDate() + " - war posture");

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                {
                    continue;
                }
                AssignPostures(kingdom, settings);
            }

            WarAiLog.Flush();
        }

        private void AssignPostures(Kingdom kingdom, CoherentWarAISettings settings)
        {
            CollectCandidates(kingdom, settings);
            if (_candidates.Count == 0)
            {
                return;
            }

            float threatRatio = CalculateThreatRatio(kingdom);
            int aggressiveSlots = PosturePlanner.AggressiveSlotCount(
                _candidates.Count, threatRatio, settings.AggressiveShare, settings.MinimumDefenders);

            WarAiLog.Write("Posture", string.Format(
                "{0}: {1} parties, {2:P0} of realm threatened -> {3} may attack, {4} defend",
                kingdom.Name, _candidates.Count, threatRatio, aggressiveSlots, _candidates.Count - aggressiveSlots));

            // Rank by aggression score (descending) so the strongest, boldest lords
            // take the offensive slots. Selection sort keeps it allocation-free on a
            // list that is at most a few dozen entries.
            for (int rank = 0; rank < _candidates.Count; rank++)
            {
                int best = rank;
                for (int i = rank + 1; i < _candidates.Count; i++)
                {
                    if (_scores[i] > _scores[best])
                    {
                        best = i;
                    }
                }
                if (best != rank)
                {
                    MobileParty swapParty = _candidates[best];
                    _candidates[best] = _candidates[rank];
                    _candidates[rank] = swapParty;

                    float swapScore = _scores[best];
                    _scores[best] = _scores[rank];
                    _scores[rank] = swapScore;
                }

                Posture posture = PosturePlanner.DecidePosture(rank, aggressiveSlots);
                MobileParty party = _candidates[rank];
                party.SetPartyObjective(ToPartyObjective(posture));

                // Only the lords released to attack are named individually - listing
                // every defender each day would drown the interesting lines.
                if (posture == Posture.Aggressive)
                {
                    WarAiLog.Write("Posture", string.Format(
                        "  attacks: {0} (strength {1:F0}, valor {2}{3})",
                        party.LeaderHero.Name,
                        party.Party.EstimatedStrength,
                        party.LeaderHero.GetTraitLevel(DefaultTraits.Valor),
                        party.LeaderHero.Clan == Clan.PlayerClan ? ", your clan" : string.Empty));
                }
            }
        }

        /// <summary>
        /// Lord parties whose objective we may set: no player main party, no
        /// attached army members (they follow their leader), no disbanding parties.
        /// The player's own clan parties are only included when opted in.
        /// </summary>
        private void CollectCandidates(Kingdom kingdom, CoherentWarAISettings settings)
        {
            _candidates.Clear();
            _scores.Clear();

            foreach (WarPartyComponent component in kingdom.WarPartyComponents)
            {
                MobileParty party = component?.MobileParty;
                if (party == null || party == MobileParty.MainParty || !party.IsActive || party.Party == null)
                {
                    continue;
                }
                if (party.IsDisbanding || party.LeaderHero == null)
                {
                    continue;
                }
                // Army members follow the army leader; only the leader decides.
                if (party.Army != null && party.Army.LeaderParty != party)
                {
                    continue;
                }
                if (party.LeaderHero.Clan == Clan.PlayerClan && !settings.ManagePlayerClanParties)
                {
                    continue;
                }

                _candidates.Add(party);
                _scores.Add(PosturePlanner.AggressionScore(
                    party.Party.EstimatedStrength,
                    party.LeaderHero.GetTraitLevel(DefaultTraits.Valor),
                    settings.ValorWeight));
            }
        }

        /// <summary>
        /// Share of the kingdom's fiefs currently under threat. Drives how many
        /// lords may be spared for offense - an invaded realm pulls its lords home.
        /// </summary>
        private static float CalculateThreatRatio(Kingdom kingdom)
        {
            int total = 0;
            int threatened = 0;

            foreach (Settlement settlement in kingdom.Settlements)
            {
                if (!settlement.IsFortification)
                {
                    continue;
                }
                total++;

                float threat = settlement.NearbyLandThreatIntensity + settlement.NearbyNavalThreatIntensity;
                float ally = settlement.NearbyLandAllyIntensity;
                if (settlement.IsUnderSiege || threat > ally)
                {
                    threatened++;
                }
            }

            return total == 0 ? 0f : (float)threatened / total;
        }

        private static MobileParty.PartyObjective ToPartyObjective(Posture posture)
        {
            switch (posture)
            {
                case Posture.Aggressive:
                    return MobileParty.PartyObjective.Aggressive;
                case Posture.Defensive:
                    return MobileParty.PartyObjective.Defensive;
                default:
                    return MobileParty.PartyObjective.Neutral;
            }
        }
    }
}
