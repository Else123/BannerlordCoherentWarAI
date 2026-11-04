namespace CoherentWarAI.Logic
{
    /// <summary>What the hourly dispatch does with one active naval patrol order.</summary>
    public enum NavalDispatchVerdict
    {
        /// <summary>Re-issue the patrol around the anchor port.</summary>
        Dispatch,

        /// <summary>Do nothing this hour but keep the order: the obstacle is temporary.</summary>
        Skip,

        /// <summary>
        /// The addressee leads an army. Vanilla's patrol action rewrites the army's type and
        /// behaviour object, and chain of command must never change what an army does (D1).
        /// </summary>
        DropLeadsArmy,

        /// <summary>The order names no port settlement to anchor the patrol to.</summary>
        DropNoPortAnchor,

        /// <summary>The anchor settlement id no longer resolves.</summary>
        DropAnchorNotFound,

        /// <summary>The anchor settlement has no port.</summary>
        DropAnchorHasNoPort,

        /// <summary>The anchor port now belongs to another faction (captured since issue).</summary>
        DropAnchorLost,

        /// <summary>No navigation type reaches the anchor; the party would stay bound without moving.</summary>
        DropNoRoute
    }

    /// <summary>
    /// Engine-free facts the connector reads about one naval patrol order and its party.
    /// <see cref="HasRoute"/> only needs to be meaningful when every fact checked before it
    /// in <see cref="NavalPatrolDispatch.Decide"/> passed.
    /// </summary>
    public struct NavalDispatchFacts
    {
        /// <summary>The party leads an army (Army.LeaderParty == party).</summary>
        public bool LeadsArmy;

        /// <summary><see cref="Order.HasPortAnchor"/>.</summary>
        public bool HasPortAnchor;

        public bool AnchorFound;
        public bool AnchorHasPort;
        public bool AnchorOwnedByPartyFaction;

        /// <summary>The party currently owns a ship (can lose it between issue and dispatch, SPEC SS4.1).</summary>
        public bool HasNavalCapability;

        /// <summary>The best navigation type to the anchor is not None.</summary>
        public bool HasRoute;
    }

    public static class NavalPatrolDispatch
    {
        /// <summary>
        /// Permanent obstacles drop the order before the temporary ship check can merely
        /// skip it, so an undispatchable order never keeps binding its lord until expiry.
        /// </summary>
        public static NavalDispatchVerdict Decide(NavalDispatchFacts facts)
        {
            if (facts.LeadsArmy)
            {
                return NavalDispatchVerdict.DropLeadsArmy;
            }
            if (!facts.HasPortAnchor)
            {
                return NavalDispatchVerdict.DropNoPortAnchor;
            }
            if (!facts.AnchorFound)
            {
                return NavalDispatchVerdict.DropAnchorNotFound;
            }
            if (!facts.AnchorHasPort)
            {
                return NavalDispatchVerdict.DropAnchorHasNoPort;
            }
            if (!facts.AnchorOwnedByPartyFaction)
            {
                return NavalDispatchVerdict.DropAnchorLost;
            }
            if (!facts.HasNavalCapability)
            {
                return NavalDispatchVerdict.Skip;
            }
            if (!facts.HasRoute)
            {
                return NavalDispatchVerdict.DropNoRoute;
            }
            return NavalDispatchVerdict.Dispatch;
        }

        public static bool IsDrop(NavalDispatchVerdict verdict)
        {
            return verdict != NavalDispatchVerdict.Dispatch && verdict != NavalDispatchVerdict.Skip;
        }
    }
}
