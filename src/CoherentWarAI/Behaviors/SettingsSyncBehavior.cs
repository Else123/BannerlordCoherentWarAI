using CoherentWarAI.Diagnostics;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;

namespace CoherentWarAI.Behaviors
{
    /// <summary>
    /// Mirrors the in-game settings page into the plain object the AI logic reads.
    ///
    /// Runs hourly so a change made mid-campaign takes effect without reloading -
    /// which is the point of having the page at all, since these weights are meant
    /// to be tuned against what a running campaign actually does.
    /// </summary>
    public class SettingsSyncBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Sync);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        /// <summary>Settings live in MCM's own storage; nothing to save here.</summary>
        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Sync();
        }

        private void Sync()
        {
            CoherentWarAIMcmSettings page = CoherentWarAIMcmSettings.Instance;
            if (page == null)
            {
                return;
            }

            page.Apply(CoherentWarAISettings.Current);

            // The log switches are read once at startup, so reflect changes here too.
            WarAiLog.VerboseScoring = CoherentWarAISettings.Current.VerboseScoreLogging;
        }
    }
}
