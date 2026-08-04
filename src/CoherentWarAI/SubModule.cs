using CoherentWarAI.Behaviors;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Models;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoherentWarAI
{
    public class SubModule : MBSubModuleBase
    {
        /// <summary>
        /// Writes out whatever is still buffered. Log lines are flushed on daily and
        /// weekly ticks, so quitting mid-day - the normal case - would otherwise
        /// discard everything since the last one, including the very lines a player
        /// was collecting when they decided to stop and look at them.
        /// </summary>
        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            WarAiLog.Flush();
        }

        protected override void InitializeGameStarter(Game game, IGameStarter gameStarterObject)
        {
            base.InitializeGameStarter(game, gameStarterObject);

            if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter campaignStarter)
            {
                // Take the configured values before anything reads them.
                CoherentWarAIMcmSettings.Instance?.Apply(CoherentWarAISettings.Current);

                CoherentWarAISettings settings = CoherentWarAISettings.Current;
                WarAiLog.Enabled = settings.EnableLogging;
                WarAiLog.VerboseScoring = settings.VerboseScoreLogging;
                WarAiLog.Start();
                WarAiLog.Write("Init", "CoherentWarAI active. Target de-greed: " + settings.EnableTargetDeGreed
                    + ", defensive posture: " + settings.EnableDefensivePosture
                    + ", hysteresis: " + settings.EnableCommitmentHysteresis
                    + ", garrisons: " + settings.EnableGarrisonThreatAwareness
                    + ", route analysis: " + settings.EnableChokepointAnalysis
                    + ", coordination: " + settings.EnableCoordination
                    + ", marshal doctrine: " + settings.EnableMarshalDoctrine);

                // Last-registered model wins; we extend the default and call base,
                // so any other TargetScoreCalculatingModel already registered keeps
                // working through the vanilla chain.
                campaignStarter.AddModel(new CoherentTargetScoreModel());
                campaignStarter.AddModel(new CoherentGarrisonModel());
                campaignStarter.AddModel(new CoherentArmyManagementModel());

                // Registered in reverse of the order they must run in: the engine
                // prepends event listeners, so the last one registered fires first.
                // Settings must be applied before anything reads them, so settings
                // sync is registered last.
                campaignStarter.AddBehavior(new OutcomeLogBehavior());
                campaignStarter.AddBehavior(new BanditHuntBehavior());
                campaignStarter.AddBehavior(new SightingNetworkBehavior());
                campaignStarter.AddBehavior(new WarPostureBehavior());
                campaignStarter.AddBehavior(new WarCoordinatorBehavior());
                campaignStarter.AddBehavior(new ChokepointMapBehavior());
                campaignStarter.AddBehavior(new MapMarkerBehavior());
                campaignStarter.AddBehavior(new SettingsSyncBehavior());
            }
        }
    }
}

