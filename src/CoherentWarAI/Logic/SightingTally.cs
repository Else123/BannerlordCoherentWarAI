using System.Collections.Generic;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Adds up reports so that each enemy force counts once, by the latest word about
    /// it - not once for every lord who saw it and every hour he kept seeing it.
    ///
    /// A watched army is reported again each hour by everyone near it. Summing those
    /// reports turns one army besieged for a day and a half under three lords' eyes
    /// into a hundred armies. Newer word supersedes older word, because it says where
    /// the force is now; of reports from the same moment the most confident stands,
    /// because the best scout's account is the one a realm would act on.
    /// </summary>
    /// <typeparam name="TSource">Identifies the force a report is about.</typeparam>
    public sealed class SightingTally<TSource>
    {
        private struct Report
        {
            public float HoursAgo;
            public float Value;
        }

        private readonly Dictionary<TSource, Report> _latest = new Dictionary<TSource, Report>();

        /// <summary>
        /// Considers a report about <paramref name="source"/>. Kept only if it is newer
        /// than what is held, or from the same moment and worth more.
        /// </summary>
        /// <param name="hoursAgo">Age of the report.</param>
        /// <param name="value">What the report contributes, already weighted by the reader.</param>
        public void Offer(TSource source, float hoursAgo, float value)
        {
            if (_latest.TryGetValue(source, out Report held))
            {
                bool newer = hoursAgo < held.HoursAgo;
                bool sameMomentButWorthMore = hoursAgo == held.HoursAgo && value > held.Value;
                if (!newer && !sameMomentButWorthMore)
                {
                    return;
                }
            }
            _latest[source] = new Report { HoursAgo = hoursAgo, Value = value };
        }

        /// <summary>Sum of the one report kept per force.</summary>
        public float Total
        {
            get
            {
                float total = 0f;
                foreach (Report report in _latest.Values)
                {
                    total += report.Value;
                }
                return total;
            }
        }

        public void Clear()
        {
            _latest.Clear();
        }
    }
}
