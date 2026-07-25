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
        protected override void InitializeGameStarter(Game game, IGameStarter gameStarterObject)
        {
            base.InitializeGameStarter(game, gameStarterObject);

            if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter campaignStarter)
            {
                CoherentWarAISettings settings = CoherentWarAISettings.Current;
                WarAiLog.Enabled = settings.EnableLogging;
                WarAiLog.VerboseScoring = settings.VerboseScoreLogging;
                WarAiLog.Start();
                WarAiLog.Write("Init", "CoherentWarAI active. Target de-greed: " + settings.EnableTargetDeGreed
                    + ", defensive posture: " + settings.EnableDefensivePosture
                    + ", hysteresis: " + settings.EnableCommitmentHysteresis
                    + ", garrisons: " + settings.EnableGarrisonThreatAwareness
                    + ", route analysis: " + settings.EnableChokepointAnalysis);

                // Last-registered model wins; we extend the default and call base,
                // so any other TargetScoreCalculatingModel already registered keeps
                // working through the vanilla chain.
                campaignStarter.AddModel(new CoherentTargetScoreModel());
                campaignStarter.AddModel(new CoherentGarrisonModel());

                campaignStarter.AddBehavior(new ChokepointMapBehavior());
                campaignStarter.AddBehavior(new WarPostureBehavior());
            }
        }
    }
}
