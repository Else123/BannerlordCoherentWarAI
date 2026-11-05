using System;
using System.Collections.Generic;
using System.Text;
using CoherentWarAI.Diagnostics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TestRig.Harness.Logic;

namespace CoherentWarAI.Scenarios
{
    /// <summary>
    /// Observer for COMMAND-SLICE-2A AC25a (army composition). Reads vanilla data only and writes a
    /// digest to the harness log; the judgement is made offline by <c>tools/Compare-Ac25a.ps1</c>,
    /// which parses exactly this format. One line per kingdom and day, plus one line per army
    /// created (short-lived armies would be missing from the daily snapshots):
    /// <code>
    /// ac25a v1 date day=&lt;d&gt; calendar=Year &lt;y&gt;, &lt;Season&gt; &lt;n&gt;
    /// ac25a v1 day=&lt;d&gt; kingdom=&lt;id&gt;|&lt;name&gt; armies=&lt;n&gt; leaders=&lt;heroId&gt;|&lt;name&gt;|&lt;ruler01&gt;;...
    /// ac25a v1 created day=&lt;d&gt; kingdom=&lt;id&gt;|&lt;name&gt; leader=&lt;heroId&gt;|&lt;name&gt;|&lt;ruler01&gt; calendar=Year &lt;y&gt;, &lt;Season&gt; &lt;n&gt;
    /// </code>
    /// A date line is written for the start day and after every digested day: it maps the harness
    /// day to the calendar date (<see cref="WarAiLog.GameDate"/>) that heads the mod log sections,
    /// so the comparer can tell whether a lord held Offense when his army was created. A created
    /// line carries its own calendar date as the last field, because <c>ctx.Day</c> only advances
    /// when the runner ticks: an army created after the daily tick but before that tick still has
    /// the previous day. The field was added to v1 at the end of the line; the comparer falls back
    /// to the date line when it is absent.
    /// Kingdom and hero names are written as plain <c>Name.ToString()</c>, exactly as the mod log
    /// does, because the comparer joins the digest to the mod log on (kingdom, name).
    /// </summary>
    public sealed class Ac25aDigestScenario : IScenario
    {
        private const string Format = "ac25a v1";

        public string Id => "coherentwarai.ac25a-digest";

        public IEnumerable<ScenarioStep> Run(IScenarioContext ctx)
        {
            Campaign campaign = Campaign.Current;
            ctx.Check("setup.campaign", campaign != null, "Campaign.Current != null", campaign != null ? "ok" : "null");
            if (campaign == null)
            {
                yield break;
            }

            int daysRequested = ctx.DaysRequested;
            ctx.Check("setup.days-requested", daysRequested > 0, "days > 0", daysRequested.ToString());
            if (daysRequested <= 0)
            {
                yield break;
            }

            int startDay = ctx.Day;
            int alignedDays = 0;
            string firstMisaligned = null;
            int kingdomLines = 0;
            int createdLines = 0;
            int createdSkipped = 0;
            int createdErrors = 0;

            // The event holds the listener by owner; ClearListeners(this) removes all of ours.
            // An exception must not escape into the campaign's event dispatch: it is counted instead.
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, army =>
            {
                try
                {
                    string line = CreatedLine(ctx.Day, army);
                    if (line == null)
                    {
                        createdSkipped++;
                        return;
                    }

                    ctx.Log(line);
                    createdLines++;
                }
                catch (Exception)
                {
                    createdErrors++;
                }
            });

            try
            {
                ctx.Log(DateLine(startDay));
                for (int i = 0; i < daysRequested; i++)
                {
                    yield return ScenarioStep.Days(1);

                    int day = ctx.Day;
                    if (day == startDay + i + 1)
                    {
                        alignedDays++;
                    }
                    else if (firstMisaligned == null)
                    {
                        firstMisaligned = "step " + (i + 1) + " on day " + day;
                    }

                    ctx.Log(DateLine(day));
                    foreach (Kingdom kingdom in Kingdom.All)
                    {
                        if (kingdom.IsEliminated)
                        {
                            continue;
                        }

                        ctx.Log(DayLine(day, kingdom));
                        kingdomLines++;
                    }
                }
            }
            finally
            {
                CampaignEvents.ArmyCreated.ClearListeners(this);
            }

            ctx.Log("digest done: " + alignedDays + " of " + daysRequested + " days aligned, " + kingdomLines + " kingdom lines, "
                + createdLines + " created events, " + createdSkipped + " skipped (no kingdom or leader), " + createdErrors + " errors");
            ctx.Check("digest.days-aligned", alignedDays == daysRequested,
                "every step one day later: days " + (startDay + 1) + ".." + (startDay + daysRequested),
                firstMisaligned ?? (alignedDays + " days aligned"));
            ctx.Check("digest.kingdoms", kingdomLines > 0, "at least one kingdom line", kingdomLines + " kingdom lines");
            ctx.Check("digest.created-errors", createdErrors == 0, "no error in the ArmyCreated listener", createdErrors + " errors");
        }

        private static string DateLine(int day)
        {
            return Format + " date day=" + day + " calendar=" + WarAiLog.GameDate();
        }

        private static string DayLine(int day, Kingdom kingdom)
        {
            var leaders = new StringBuilder();
            int armies = 0;
            foreach (Army army in kingdom.Armies)
            {
                Hero leader = army.LeaderParty?.LeaderHero;
                if (leader == null)
                {
                    continue;
                }

                if (armies > 0)
                {
                    leaders.Append(';');
                }

                leaders.Append(LeaderEntry(kingdom, leader));
                armies++;
            }

            return Format + " day=" + day + " kingdom=" + KingdomEntry(kingdom) + " armies=" + armies + " leaders=" + leaders;
        }

        // Null when the army has no kingdom or no leader hero: the comparer needs both.
        private static string CreatedLine(int day, Army army)
        {
            Kingdom kingdom = army?.Kingdom;
            Hero leader = army?.LeaderParty?.LeaderHero;
            if (kingdom == null || leader == null)
            {
                return null;
            }

            return Format + " created day=" + day + " kingdom=" + KingdomEntry(kingdom) + " leader=" + LeaderEntry(kingdom, leader)
                + " calendar=" + WarAiLog.GameDate();
        }

        private static string KingdomEntry(Kingdom kingdom)
        {
            return kingdom.StringId + "|" + kingdom.Name;
        }

        // The ruler flag is 1 for the kingdom leader; the army model lets rulers raise the royal host.
        // The player-clan flag is 1 for a lord of the player's clan, which the army model never constrains.
        private static string LeaderEntry(Kingdom kingdom, Hero leader)
        {
            return leader.StringId + "|" + leader.Name + "|" + (leader == kingdom.Leader ? "1" : "0")
                + "|" + (leader.Clan != null && leader.Clan == Clan.PlayerClan ? "1" : "0");
        }
    }
}
