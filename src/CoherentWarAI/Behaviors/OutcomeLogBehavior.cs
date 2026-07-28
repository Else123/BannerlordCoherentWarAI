using CoherentWarAI.Diagnostics;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Records what actually happened, as opposed to what the AI decided.
    ///
    /// The rest of the logging shows intent - how many lords were released to
    /// attack, which targets were damped and why. None of it says whether a castle
    /// ever fell. For judging whether the AI wages war *coherently*, outcomes are
    /// the real evidence: a realm that decides beautifully and never takes anything
    /// is in a stalemate, and that only shows up here.
    ///
    /// Deliberately sparse - these are rare events, so a multi-year campaign stays
    /// readable where per-decision logging would not.
    /// </summary>
    public class OutcomeLogBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        /// <summary>Outcomes are observed, not stored.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement == null || !settlement.IsFortification)
            {
                return;
            }

            WarAiLog.Write("Outcome", string.Format("{0}: {1} changed hands, {2} -> {3} ({4})",
                WarAiLog.GameDate(),
                settlement.Name,
                oldOwner?.MapFaction?.Name?.ToString() ?? "nobody",
                newOwner?.MapFaction?.Name?.ToString() ?? "nobody",
                detail));
            WarAiLog.Flush();
        }

        private void OnWarDeclared(IFaction attacker, IFaction defender, DeclareWarAction.DeclareWarDetail detail)
        {
            WarAiLog.Write("Outcome", string.Format("{0}: WAR - {1} declares on {2}",
                WarAiLog.GameDate(), attacker?.Name, defender?.Name));
        }

        private void OnPeaceMade(IFaction side1, IFaction side2, MakePeaceAction.MakePeaceDetail detail)
        {
            WarAiLog.Write("Outcome", string.Format("{0}: PEACE - {1} and {2}",
                WarAiLog.GameDate(), side1?.Name, side2?.Name));
        }

        private void OnSiegeStarted(SiegeEvent siegeEvent)
        {
            Settlement besieged = siegeEvent?.BesiegedSettlement;
            if (besieged == null)
            {
                return;
            }

            WarAiLog.Write("Outcome", string.Format("{0}: siege of {1} ({2}) begun by {3}",
                WarAiLog.GameDate(), besieged.Name, besieged.MapFaction?.Name,
                siegeEvent.BesiegerCamp?.LeaderParty?.LeaderHero?.Name));
        }

        /// <summary>
        /// A weekly line of who holds what. Over years this is the shape of the
        /// campaign - whether the map is moving at all, and whether any one realm is
        /// running away with it.
        /// </summary>
        private void OnWeeklyTick()
        {
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableLogging)
            {
                return;
            }

            WarAiLog.Section(WarAiLog.GameDate() + " - the map");

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated)
                {
                    continue;
                }

                int fortifications = 0;
                foreach (Settlement settlement in kingdom.Settlements)
                {
                    if (settlement.IsFortification)
                    {
                        fortifications++;
                    }
                }

                WarAiLog.Write("Map", string.Format("{0,-18} {1,3} fiefs, {2,3} parties, {3,2} war(s), strength {4:F0}",
                    kingdom.Name, fortifications, kingdom.WarPartyComponents.Count,
                    kingdom.FactionsAtWarWith.Count, kingdom.CurrentTotalStrength));
            }

            WarAiLog.Flush();
        }
    }
}
