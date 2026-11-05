using System.Collections.Generic;

namespace CoherentWarAI.Logic
{
    /// <summary>One party channel that two subsystems held at the same time.</summary>
    public sealed class WriteContention<TParty, TChannel>
    {
        public WriteContention(TParty party, TChannel channel, IList<string> writers)
        {
            Party = party;
            Channel = channel;
            Writers = writers;
        }

        public TParty Party { get; private set; }

        public TChannel Channel { get; private set; }

        /// <summary>The subsystems that overlapped on this channel, sorted ordinally.</summary>
        public IList<string> Writers { get; private set; }
    }

    /// <summary>
    /// Ledger behind the daily contention counter (AC13 / SPEC SS11.4). A subsystem
    /// that writes a party's channel *holds* it until it releases it; contention is two
    /// different subsystems holding the same channel of the same party at the same time.
    /// A handoff - A writes, A releases (order dropped or expired), B writes - is
    /// sequential and never counts, whereas A writing and B writing before A let go does,
    /// and keeps counting even if A releases afterwards (they did overlap).
    ///
    /// Generic over the party and channel keys so it carries no engine types.
    /// </summary>
    public sealed class WriteContentionLedger<TParty, TChannel>
    {
        private sealed class ChannelState
        {
            public readonly HashSet<string> Holders = new HashSet<string>();
            public readonly List<string> Overlapping = new List<string>();
        }

        private readonly Dictionary<TParty, Dictionary<TChannel, ChannelState>> _parties
            = new Dictionary<TParty, Dictionary<TChannel, ChannelState>>();

        /// <summary>How many distinct parties were written today, released or not.</summary>
        public int TrackedPartyCount
        {
            get { return _parties.Count; }
        }

        /// <summary>Records that <paramref name="subsystem"/> now holds this party's channel.</summary>
        public void Write(TParty party, TChannel channel, string subsystem)
        {
            if (party == null || string.IsNullOrEmpty(subsystem))
            {
                return;
            }
            if (!_parties.TryGetValue(party, out Dictionary<TChannel, ChannelState> byChannel))
            {
                byChannel = new Dictionary<TChannel, ChannelState>();
                _parties[party] = byChannel;
            }
            if (!byChannel.TryGetValue(channel, out ChannelState state))
            {
                state = new ChannelState();
                byChannel[channel] = state;
            }

            if (state.Holders.Contains(subsystem))
            {
                return;
            }
            if (state.Holders.Count > 0)
            {
                // Someone else still holds it: every current holder is part of the clash.
                foreach (string holder in state.Holders)
                {
                    AddOverlapping(state, holder);
                }
                AddOverlapping(state, subsystem);
            }
            state.Holders.Add(subsystem);
        }

        /// <summary>
        /// Ends <paramref name="subsystem"/>'s hold on this party's channel. A no-op when it
        /// never wrote it or does not currently hold it. An overlap already seen stays counted.
        /// </summary>
        public void Release(TParty party, TChannel channel, string subsystem)
        {
            if (party == null || string.IsNullOrEmpty(subsystem))
            {
                return;
            }
            if (_parties.TryGetValue(party, out Dictionary<TChannel, ChannelState> byChannel)
                && byChannel.TryGetValue(channel, out ChannelState state))
            {
                state.Holders.Remove(subsystem);
            }
        }

        /// <summary>The channels that were held by more than one subsystem at once.</summary>
        public List<WriteContention<TParty, TChannel>> Contentions()
        {
            List<WriteContention<TParty, TChannel>> result = new List<WriteContention<TParty, TChannel>>();
            foreach (KeyValuePair<TParty, Dictionary<TChannel, ChannelState>> partyPair in _parties)
            {
                foreach (KeyValuePair<TChannel, ChannelState> channelPair in partyPair.Value)
                {
                    if (channelPair.Value.Overlapping.Count > 0)
                    {
                        // Ordinal-sorted so the log line does not depend on join order.
                        string[] writers = channelPair.Value.Overlapping.ToArray();
                        System.Array.Sort(writers, System.StringComparer.Ordinal);
                        result.Add(new WriteContention<TParty, TChannel>(partyPair.Key, channelPair.Key, writers));
                    }
                }
            }
            return result;
        }

        /// <summary>Forgets every write and hold - the daily reset.</summary>
        public void Clear()
        {
            _parties.Clear();
        }

        private static void AddOverlapping(ChannelState state, string subsystem)
        {
            if (!state.Overlapping.Contains(subsystem))
            {
                state.Overlapping.Add(subsystem);
            }
        }
    }
}
