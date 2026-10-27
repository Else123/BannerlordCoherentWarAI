using System;
using System.Collections.Generic;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Which command domain an order or a delegation belongs to. A realm delegates
    /// each domain to at most one holder; a holder may hold several at once.
    /// </summary>
    [Flags]
    public enum Domain
    {
        None = 0,

        /// <summary>Holding our own ground: posture, gateway duty, relief, interior bandit policing.</summary>
        Defense = 1,

        /// <summary>Conquest: which enemy, which fief, who leads and who joins the host.</summary>
        Offense = 2,

        /// <summary>Sea lanes, ports, coastal raids, fleet movement - gated on ship ownership.</summary>
        Naval = 4,

        All = Defense | Offense | Naval
    }

    /// <summary>How precisely an <see cref="Order"/> is addressed.</summary>
    public enum OrderScope
    {
        Realm,
        Domain,
        Lord
    }

    /// <summary>
    /// What an order asks for. Naval and land tasks share the same verbs - vanilla
    /// itself reuses the same <c>AiBehavior</c> values for a coastal raid and a land
    /// raid, distinguishing them only by navigation type - so <see cref="Order.IsNaval"/>
    /// carries that distinction instead of a parallel naval verb list.
    /// </summary>
    public enum OrderVerb
    {
        Defend,
        Patrol,
        Besiege,
        Raid,
        JoinArmy,
        HuntBandits
    }

    /// <summary>Whether a lord must obey an order or is merely being advised.</summary>
    public enum Bindingness
    {
        Recommendation,
        Directive
    }

    /// <summary>Who caused an order to exist, and therefore how much it outranks another one.</summary>
    public enum Provenance
    {
        AiMarshal,
        AiKing,
        Player
    }

    /// <summary>
    /// One instruction from a king or marshal to a lord, a domain, or the whole
    /// realm. Targets are kept as <c>StringId</c>s, never engine object references,
    /// so the type stays engine-free and save-stable - the connector
    /// (<c>Behaviors/CommandBehavior.cs</c>) is the only place that resolves an id
    /// back to a <c>Hero</c> / <c>Settlement</c> / <c>MobileParty</c>.
    /// </summary>
    public struct Order
    {
        /// <summary>Hero.StringId of the king or marshal who issued this; "" for a doctrine-level default.</summary>
        public string IssuerId;

        public OrderScope Scope;

        /// <summary>Hero.StringId when Scope == Lord; else "".</summary>
        public string AddresseeId;

        /// <summary>Which domain this order belongs to - a single flag, never <see cref="Domain.All"/>; part of the order store's key from Slice 2a on.</summary>
        public Domain DomainScope;

        public OrderVerb Verb;

        /// <summary>"" when the target is a point rather than a settlement.</summary>
        public string TargetSettlementId;

        /// <summary>Point target, e.g. a naval patrol lane; also the resolved port/gate point.</summary>
        public float TargetX, TargetY;

        /// <summary>Sea task: drives the NavigationType at the engine boundary.</summary>
        public bool IsNaval;

        /// <summary>Maps to the engine's isTargetingPort parameter.</summary>
        public bool TargetIsPort;

        public Bindingness Bindingness;

        /// <summary>1 routine .. 3 urgent; scales the score bias, not the bindingness.</summary>
        public int Priority;

        public float IssuedAtHours;
        public float ExpiresAtHours;
        public Provenance Provenance;

        /// <summary>Whether this order has not yet expired.</summary>
        public bool IsActive(float nowHours)
        {
            return nowHours < ExpiresAtHours;
        }

        /// <summary>
        /// Whether a lord must obey this order right now. A <see cref="Bindingness.Recommendation"/>
        /// is never binding, whatever else is set; an expired <see cref="Bindingness.Directive"/> stops
        /// binding without needing to be deleted first.
        /// </summary>
        public bool IsBinding(float nowHours)
        {
            return Bindingness == Bindingness.Directive && IsActive(nowHours);
        }

        /// <summary>
        /// Total, deterministic conflict resolution via a strict weak ordering: at most one
        /// of <c>a.Supersedes(b)</c> / <c>b.Supersedes(a)</c> is ever true, so two orders can
        /// never both win and no <see cref="Random"/> is ever needed - but neither call may
        /// return true either, since two distinct orders tied on every tier below (same
        /// provenance, scope, priority and issue time) both return false, i.e. "returns false
        /// for ties" rather than "exactly one is true". Evaluated as four
        /// tiers, each only consulted when the previous one is tied:
        /// 1. Provenance rank - <see cref="Provenance.Player"/> beats <see cref="Provenance.AiKing"/>
        ///    beats <see cref="Provenance.AiMarshal"/>. Ranking provenance directly (rather than
        ///    checking "is player" and "is king" as two separate steps) is exactly equivalent to the
        ///    spec's two rules ("player beats any AI", "king beats marshal") because the three
        ///    provenances form a strict order along that same axis.
        /// 2. Scope precision - Lord beats Domain beats Realm.
        /// 3. Priority, descending.
        /// 4. IssuedAtHours, descending (newer wins).
        ///
        /// Usage contract: only meaningful between two orders already known to share
        /// one (addressee, domain) bucket. It has no domain dimension - two orders
        /// differing only in <see cref="DomainScope"/> tie in both directions - so a
        /// caller must bucket by domain before comparing, never compare across domains
        /// (correction to SPEC SS3.5, see COMMAND-SLICE-2A.md).
        /// </summary>
        public bool Supersedes(Order other)
        {
            int thisRank = ProvenanceRank(Provenance);
            int otherRank = ProvenanceRank(other.Provenance);
            if (thisRank != otherRank)
            {
                return thisRank > otherRank;
            }

            int thisScope = ScopeRank(Scope);
            int otherScope = ScopeRank(other.Scope);
            if (thisScope != otherScope)
            {
                return thisScope > otherScope;
            }

            if (Priority != other.Priority)
            {
                return Priority > other.Priority;
            }

            return IssuedAtHours > other.IssuedAtHours;
        }

        private static int ProvenanceRank(Provenance provenance)
        {
            switch (provenance)
            {
                case Provenance.Player:
                    return 2;
                case Provenance.AiKing:
                    return 1;
                default:
                    return 0;
            }
        }

        private static int ScopeRank(OrderScope scope)
        {
            switch (scope)
            {
                case OrderScope.Lord:
                    return 2;
                case OrderScope.Domain:
                    return 1;
                default:
                    return 0;
            }
        }
    }

    /// <summary>
    /// Composite key bucketing the order stores by (owner, domain) rather than by owner
    /// alone - the Slice 2a rekey that lets one lord hold orders in more than one domain
    /// at once without one silently overwriting another (COMMAND-SLICE-2A.md). Owner
    /// equality is reference equality, matching how the pre-rekey dictionaries already
    /// keyed <c>Hero</c>/<c>MobileParty</c>. Engine-free (M2, post-review: moved here
    /// from a private struct inside <c>Behaviors/CommandBehavior.cs</c> so it gets
    /// offline test coverage) - the connector is the only place that instantiates it
    /// with an actual engine type.
    /// </summary>
    internal struct DomainKey<T> : IEquatable<DomainKey<T>> where T : class
    {
        public readonly T Owner;
        public readonly Domain Domain;

        public DomainKey(T owner, Domain domain)
        {
            Owner = owner;
            Domain = domain;
        }

        public bool Equals(DomainKey<T> other)
        {
            return ReferenceEquals(Owner, other.Owner) && Domain == other.Domain;
        }

        public override bool Equals(object obj)
        {
            return obj is DomainKey<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((Owner != null ? Owner.GetHashCode() : 0) * 397) ^ (int)Domain;
        }
    }

    /// <summary>One domain held by one hero, since a given <see cref="DelegationTable.WithDelegation"/> call.</summary>
    public struct DelegationEntry
    {
        public Domain Domain;
        public string HolderId;
        public float GrantedAtHours;
    }

    /// <summary>
    /// Who commands whom in one realm: an immutable map from <see cref="Domain"/> to
    /// its current holder. A domain always has exactly one holder, either a marshal
    /// or the king himself - <see cref="HolderFor"/> returning "" means the king holds
    /// it personally, there is no third state. Every mutator returns a new table
    /// rather than changing this one in place, so a caller can hold onto an old
    /// table (e.g. for logging what changed) without it shifting under them.
    /// </summary>
    public sealed class DelegationTable
    {
        public string KingId { get; }
        public bool NavalDelegatedSeparately { get; }

        /// <summary>
        /// Inspectable entry list. Not part of the authority model itself (that is
        /// <see cref="HolderFor"/> / <see cref="CanIssue"/>) - exposed so the
        /// one-holder-per-domain invariant is testable without assuming internal
        /// storage.
        /// </summary>
        public IReadOnlyList<DelegationEntry> Entries { get { return _entries; } }

        private readonly List<DelegationEntry> _entries;

        public DelegationTable(string kingId, bool navalDelegatedSeparately)
            : this(kingId, navalDelegatedSeparately, new List<DelegationEntry>())
        {
        }

        private DelegationTable(string kingId, bool navalDelegatedSeparately, List<DelegationEntry> entries)
        {
            KingId = kingId;
            NavalDelegatedSeparately = navalDelegatedSeparately;
            _entries = entries;
        }

        /// <summary>
        /// The hero holding this single domain flag, or "" if the king holds it (reclaimed).
        /// <paramref name="domain"/> must be exactly one of <see cref="Domain.Defense"/>,
        /// <see cref="Domain.Offense"/>, <see cref="Domain.Naval"/> - never
        /// <see cref="Domain.None"/> nor a multi-flag combination such as
        /// <see cref="Domain.All"/>, which throws <see cref="ArgumentException"/>. A caller
        /// wanting several domains' holders calls this once per flag.
        /// </summary>
        public string HolderFor(Domain domain)
        {
            RequireSingleFlag(domain);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Domain == domain)
                {
                    return _entries[i].HolderId;
                }
            }
            return "";
        }

        private static void RequireSingleFlag(Domain domain)
        {
            if (domain != Domain.Defense && domain != Domain.Offense && domain != Domain.Naval)
            {
                throw new ArgumentException(
                    "HolderFor/CanIssue require a single Domain flag (Defense, Offense or Naval), not " + domain,
                    nameof(domain));
            }
        }

        /// <summary>Expands a (possibly multi-flag) domain into its set individual flags, ignoring <see cref="Domain.None"/>.</summary>
        private static List<Domain> ExpandFlags(Domain domain)
        {
            List<Domain> flags = new List<Domain>();
            if ((domain & Domain.Defense) != 0) { flags.Add(Domain.Defense); }
            if ((domain & Domain.Offense) != 0) { flags.Add(Domain.Offense); }
            if ((domain & Domain.Naval) != 0) { flags.Add(Domain.Naval); }
            return flags;
        }

        /// <summary>Whether this hero holds at least one domain and is not the king himself.</summary>
        public bool IsMarshal(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || heroId == KingId)
            {
                return false;
            }
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].HolderId == heroId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The entire authority model, with no further permissions: an issuer may
        /// act in a domain iff he is the king, or he is that domain's current
        /// holder. A marshal is bound by the domains he actually holds, not by
        /// rank - one holding only <see cref="Domain.Naval"/> cannot issue a
        /// <see cref="Domain.Defense"/> order even to a lord he commands navally.
        /// <paramref name="targetLordId"/> is accepted per the SPEC signature but not
        /// otherwise consulted: authority here is judged purely by domain holdership. An
        /// empty or null <paramref name="issuerId"/> never has authority - a reclaimed
        /// domain's <see cref="HolderFor"/> is "" and a throneless kingdom's
        /// <see cref="KingId"/> is "" too, and neither must let an absent issuer compare
        /// equal and pass as the king or the holder.
        /// </summary>
        public bool CanIssue(string issuerId, string targetLordId, Domain domain)
        {
            if (string.IsNullOrEmpty(issuerId))
            {
                return false;
            }
            return issuerId == KingId || issuerId == HolderFor(domain);
        }

        /// <summary>
        /// Grants <paramref name="domain"/> to <paramref name="holderId"/>. A multi-flag
        /// <paramref name="domain"/> (e.g. <see cref="Domain.All"/>) expands into one entry
        /// per set flag, each independently replacing that flag's existing holder - the
        /// one-holder-per-domain invariant still holds per flag, never per combination.
        /// </summary>
        public DelegationTable WithDelegation(Domain domain, string holderId, float nowHours)
        {
            List<Domain> flags = ExpandFlags(domain);
            List<DelegationEntry> next = new List<DelegationEntry>(_entries.Count + flags.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!flags.Contains(_entries[i].Domain))
                {
                    next.Add(_entries[i]);
                }
            }
            for (int f = 0; f < flags.Count; f++)
            {
                next.Add(new DelegationEntry { Domain = flags[f], HolderId = holderId, GrantedAtHours = nowHours });
            }
            return new DelegationTable(KingId, NavalDelegatedSeparately, next);
        }

        /// <summary>
        /// Takes a domain back for the king. <see cref="HolderFor"/> returns "" for each of
        /// <paramref name="domain"/>'s set flags afterwards; a multi-flag domain only clears
        /// those flags' entries and leaves any other held domain untouched.
        /// </summary>
        public DelegationTable WithReclaim(Domain domain, float nowHours)
        {
            List<Domain> flags = ExpandFlags(domain);
            List<DelegationEntry> next = new List<DelegationEntry>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!flags.Contains(_entries[i].Domain))
                {
                    next.Add(_entries[i]);
                }
            }
            return new DelegationTable(KingId, NavalDelegatedSeparately, next);
        }

        /// <summary>
        /// Drops every entry whose holder no longer resolves (<paramref name="resolves"/>
        /// returns false), so a hero who died, was captured or left the kingdom does not
        /// linger as a ghost holder. The connector supplies "does this StringId still
        /// resolve to a live Hero in this kingdom" as <paramref name="resolves"/>.
        /// </summary>
        public DelegationTable Prune(Func<string, bool> resolves)
        {
            List<DelegationEntry> next = new List<DelegationEntry>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (resolves(_entries[i].HolderId))
                {
                    next.Add(_entries[i]);
                }
            }
            return new DelegationTable(KingId, NavalDelegatedSeparately, next);
        }
    }

    /// <summary>
    /// Free functions over the command model that do not belong to a single type -
    /// one static planner class per Logic file, as elsewhere in this codebase.
    /// </summary>
    public static class ChainOfCommandFunctions
    {
        /// <summary>
        /// Which domain a given order verb falls under. Naval is only its own domain
        /// for <see cref="OrderVerb.Patrol"/>/<see cref="OrderVerb.Defend"/> (folds into
        /// <see cref="Domain.Defense"/> when not separately delegated) and
        /// <see cref="OrderVerb.Raid"/> (folds into <see cref="Domain.Offense"/>) - a
        /// small realm with one port never sees a Naval domain at all. Every other verb
        /// keeps its ordinary land mapping regardless of <paramref name="isNaval"/>.
        /// </summary>
        public static Domain DomainOf(OrderVerb verb, bool isNaval, bool navalDelegatedSeparately)
        {
            if (isNaval)
            {
                switch (verb)
                {
                    case OrderVerb.Patrol:
                    case OrderVerb.Defend:
                        return navalDelegatedSeparately ? Domain.Naval : Domain.Defense;
                    case OrderVerb.Raid:
                        return navalDelegatedSeparately ? Domain.Naval : Domain.Offense;
                }
            }

            switch (verb)
            {
                case OrderVerb.Defend:
                case OrderVerb.Patrol:
                case OrderVerb.HuntBandits:
                    return Domain.Defense;
                default:
                    return Domain.Offense;
            }
        }
    }
}
