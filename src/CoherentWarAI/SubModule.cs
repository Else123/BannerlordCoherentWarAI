using CoherentWarAI.Models;
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
                // Last-registered model wins; we extend the default and call base,
                // so any other TargetScoreCalculatingModel already registered keeps
                // working through the vanilla chain.
                campaignStarter.AddModel(new CoherentTargetScoreModel());
            }
        }
    }
}
