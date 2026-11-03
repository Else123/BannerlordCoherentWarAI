using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CoherentWarAI.Behaviors;
using CoherentWarAI.Diagnostics;
using CoherentWarAI.Models;
using TaleWorlds.CampaignSystem;
using TestRig.Harness.Logic;

namespace CoherentWarAI.Scenarios
{
    /// <summary>
    /// Health check with time stopped: every campaign behavior and GameModel registered in
    /// <c>SubModule.InitializeGameStarter</c> is actually present in the running campaign.
    /// </summary>
    public sealed class HealthScenario : IScenario
    {
        public string Id => "coherentwarai.health";

        public IEnumerable<ScenarioStep> Run(IScenarioContext ctx)
        {
            Campaign campaign = Campaign.Current;
            ctx.Check("setup.campaign", campaign != null, "Campaign.Current != null", campaign != null ? "ok" : "null");
            if (campaign == null)
            {
                yield break;
            }

            // Typed lookups, so renaming or removing a behavior breaks this build.
            CheckBehavior(ctx, "behavior.OutcomeLog", campaign.GetCampaignBehavior<OutcomeLogBehavior>());
            CheckBehavior(ctx, "behavior.BanditHunt", campaign.GetCampaignBehavior<BanditHuntBehavior>());
            CheckBehavior(ctx, "behavior.SightingNetwork", campaign.GetCampaignBehavior<SightingNetworkBehavior>());
            CheckBehavior(ctx, "behavior.Command", campaign.GetCampaignBehavior<CommandBehavior>());
            CheckBehavior(ctx, "behavior.WarPosture", campaign.GetCampaignBehavior<WarPostureBehavior>());
            CheckBehavior(ctx, "behavior.WarCoordinator", campaign.GetCampaignBehavior<WarCoordinatorBehavior>());
            CheckBehavior(ctx, "behavior.ChokepointMap", campaign.GetCampaignBehavior<ChokepointMapBehavior>());
            CheckBehavior(ctx, "behavior.MapMarker", campaign.GetCampaignBehavior<MapMarkerBehavior>());
            CheckBehavior(ctx, "behavior.SettingsSync", campaign.GetCampaignBehavior<SettingsSyncBehavior>());

            // The mod's models extend the previously registered model (BaseModel), so another
            // mod registered after ours may legitimately wrap or subclass them. "is" accepts
            // that; an exact type match would not.
            CheckModel<CoherentTargetScoreModel>(ctx, "model.TargetScoreCalculatingModel",
                campaign.Models.TargetScoreCalculatingModel);
            CheckModel<CoherentGarrisonModel>(ctx, "model.SettlementGarrisonModel",
                campaign.Models.SettlementGarrisonModel);
            CheckModel<CoherentArmyManagementModel>(ctx, "model.ArmyManagementCalculationModel",
                campaign.Models.ArmyManagementCalculationModel);

            CheckLogForErrors(ctx);
        }

        // F-S6-6. The harness calls a health scenario twice on this same instance: before the soak
        // (baseline) and after it. The first call records the log length, the second one reads
        // only what was appended since. WarAiLog has no error level, so the markers cover the
        // category tags an error line would use plus raw exception text; today none is emitted,
        // which makes this a tripwire for future error logging.
        private static readonly string[] ErrorMarkers = { "[Error]", "[Exception]", "Exception" };

        private bool _baselineTaken;
        private string _baselinePath;
        private long _baselineLength;

        private void CheckLogForErrors(IScenarioContext ctx)
        {
            // Buffered lines only reach the file on a flush; make the file current first.
            WarAiLog.Flush();
            string path = WarAiLog.FilePath;

            if (!_baselineTaken)
            {
                _baselineTaken = true;
                _baselinePath = path;
                _baselineLength = path != null ? FileLength(path) : 0;
                ctx.Log("log baseline at day " + ctx.Day + ": " + (path ?? "(no log file)") + ", " + _baselineLength + " bytes");
                return;
            }

            if (path == null || path != _baselinePath)
            {
                ctx.Log("log growth check skipped: log path changed from " + (_baselinePath ?? "(none)") + " to " + (path ?? "(none)"));
                return;
            }

            long length = FileLength(path);
            if (length < _baselineLength)
            {
                ctx.Log("log growth check skipped: log shrank from " + _baselineLength + " to " + length + " bytes (restarted)");
                return;
            }

            string appended = ReadFrom(path, _baselineLength, length);
            string found = null;
            foreach (string marker in ErrorMarkers)
            {
                if (appended.Contains(marker))
                {
                    found = marker;
                    break;
                }
            }

            ctx.Log("log grew by " + (length - _baselineLength) + " bytes by day " + ctx.Day);
            ctx.Check("log.no-errors", found == null,
                "no " + string.Join(" / ", ErrorMarkers) + " in " + (length - _baselineLength) + " appended bytes",
                found == null ? "none found" : "found " + found);
        }

        private static long FileLength(string path)
        {
            // A missing file counts as empty.
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        private static string ReadFrom(string path, long start, long end)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[Math.Max(0, end - start)];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                return Encoding.UTF8.GetString(buffer, 0, read);
            }
        }

        private static void CheckBehavior<T>(IScenarioContext ctx, string name, T behavior) where T : class
        {
            ctx.Check(name, behavior != null, typeof(T).Name + " registered", behavior != null ? "ok" : "null");
        }

        private static void CheckModel<TMod>(IScenarioContext ctx, string name, object active)
        {
            ctx.Check(name, active is TMod, "active model is " + typeof(TMod).Name,
                active == null ? "null" : active.GetType().FullName);
        }
    }
}
