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

        /// <summary>Per force, the age of the newest report that saw it inside another force.</summary>
        private readonly Dictionary<TSource, float> _absorbedHoursAgo = new Dictionary<TSource, float>();

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

        /// <summary>
        /// Notes that <paramref name="member"/> was seen <paramref name="hoursAgo"/> as
        /// part of another force (a party marching in an army), whose own report already
        /// carries its strength. Any report about <paramref name="member"/> that is not
        /// newer than that sighting is superseded and no longer counted; a later report
        /// of it on its own - it has left the army - counts again. Order-independent:
        /// may be called before or after the member's own reports are offered.
        /// </summary>
        public void Absorb(TSource member, float hoursAgo)
        {
            if (_absorbedHoursAgo.TryGetValue(member, out float held) && held <= hoursAgo)
            {
                return;
            }
            _absorbedHoursAgo[member] = hoursAgo;
        }

        /// <summary>Sum of the one report kept per force, leaving out forces last seen inside another.</summary>
        public float Total
        {
            get
            {
                float total = 0f;
                foreach (KeyValuePair<TSource, Report> entry in _latest)
                {
                    if (_absorbedHoursAgo.TryGetValue(entry.Key, out float absorbedHoursAgo)
                        && entry.Value.HoursAgo >= absorbedHoursAgo)
                    {
                        continue;
                    }
                    total += entry.Value.Value;
                }
                return total;
            }
        }

        public void Clear()
        {
            _latest.Clear();
            _absorbedHoursAgo.Clear();
        }
    }
}
