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

            // Statistics learned from a previous campaign in this process would
            // otherwise carry over and change scoring here.
            WarAiStats.ResetForNewCampaign();

            // The startup line is written before the settings page is necessarily
            // available, so it can report defaults rather than what is configured.
            // Restate it once the real values are in.
            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            WarAiLog.Write("Init", "Settings in effect. Target de-greed: " + settings.EnableTargetDeGreed
                + ", defensive posture: " + settings.EnableDefensivePosture
                + ", hysteresis: " + settings.EnableCommitmentHysteresis
                + ", garrisons: " + settings.EnableGarrisonThreatAwareness
                + ", route analysis: " + settings.EnableChokepointAnalysis
                + ", coordination: " + settings.EnableCoordination
                + ", marshal doctrine: " + settings.EnableMarshalDoctrine
                + ", enemy focus: " + settings.EnableEnemyFocus
                + ", holdability: " + settings.EnableHoldability);
            WarAiLog.Flush();
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
