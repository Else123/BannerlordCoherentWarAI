using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoherentWarAI.Diagnostics
{
    /// <summary>
    /// Writes a readable trace of what the mod decided and why, so behaviour seen
    /// in a campaign can be traced back to the numbers that produced it. On by
    /// default - the weights ship as conservative starting points, not tuned
    /// values, and tuning them blind is guesswork.
    ///
    /// Buffered: campaign AI runs on the main thread and target scoring is called
    /// hundreds of times an hour, so nothing touches the disk until a flush.
    /// </summary>
    public static class WarAiLog
    {
        private const int FlushThreshold = 400;

        private static readonly List<string> Buffer = new List<string>();
        private static string _path;
        private static bool _failed;

        /// <summary>Turn logging off entirely; enabled by default.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Per-decision detail for target scoring. Off by default even when logging
        /// is on: that path runs hundreds of times per game hour and would bury the
        /// interesting entries.
        /// </summary>
        public static bool VerboseScoring { get; set; }

        /// <summary>Full path of the log file, once known.</summary>
        public static string FilePath => _path;

        /// <summary>
        /// Readable in-game date. <c>CampaignTime.ToDays</c> counts from the calendar
        /// epoch, not from the campaign start, so it reads as a five-digit number
        /// that means nothing to a player looking at the log.
        /// </summary>
        public static string GameDate()
        {
            TaleWorlds.CampaignSystem.CampaignTime now = TaleWorlds.CampaignSystem.CampaignTime.Now;
            return string.Format("Year {0}, {1} {2}", now.GetYear, now.GetSeasonOfYear, now.GetDayOfSeason + 1);
        }

        /// <summary>Starts a fresh log for this session.</summary>
        public static void Start()
        {
            if (!Enabled)
            {
                return;
            }

            // A failure in a previous session should not disable logging for the
            // rest of the process - whatever locked the file may well be gone.
            _failed = false;

            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord",
                    "CoherentWarAI");
                Directory.CreateDirectory(directory);

                _path = Path.Combine(directory, "CoherentWarAI.log");

                // Keep one previous run around; a fresh file per session keeps the
                // trace readable instead of growing without bound.
                string previous = Path.Combine(directory, "CoherentWarAI.previous.log");
                if (File.Exists(_path))
                {
                    File.Copy(_path, previous, overwrite: true);
                }

                File.WriteAllText(_path,
                    "CoherentWarAI log - started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // Logging must never take the game down with it.
                _failed = true;
                _path = null;
            }
        }

        /// <summary>Records one line under a short category tag.</summary>
        public static void Write(string category, string message)
        {
            if (!Enabled || _failed || _path == null)
            {
                return;
            }

            Buffer.Add("[" + category + "] " + message);

            // Growth is bounded here: the buffer is written out and cleared as soon
            // as it reaches the threshold, and a failed write disables logging
            // outright rather than letting lines accumulate.
            if (Buffer.Count >= FlushThreshold)
            {
                Flush();
            }
        }

        /// <summary>Writes a blank-line separated heading, e.g. for a new day.</summary>
        public static void Section(string heading)
        {
            if (!Enabled || _failed || _path == null)
            {
                return;
            }
            Buffer.Add(string.Empty);
            Buffer.Add("=== " + heading + " ===");
        }

        /// <summary>Pushes buffered lines to disk.</summary>
        public static void Flush()
        {
            if (_failed || _path == null || Buffer.Count == 0)
            {
                return;
            }

            try
            {
                File.AppendAllLines(_path, Buffer, Encoding.UTF8);
            }
            catch (Exception)
            {
                _failed = true;
            }
            finally
            {
                Buffer.Clear();
            }
        }
    }
}
