namespace CoherentWarAI.Logic
{
    /// <summary>Which side a force near a settlement is on, from the owner's point of view.</summary>
    public enum ForceStance
    {
        Neutral,
        Hostile,
        Friendly,
    }

    /// <summary>
    /// One party (or army) found near a settlement, reduced to the plain facts the
    /// garrison threat measure needs. The connector fills these from a vanilla
    /// locator search; <see cref="GarrisonPlanner.ActiveThreat"/> decides what counts.
    /// </summary>
    public struct NearbyForce
    {
        /// <param name="strength">Estimated strength; for an army leader, the whole army's.</param>
        /// <param name="distance">Distance from the point the search was centred on.</param>
        /// <param name="searchRadius">Radius of the search that found it (land and sea differ).</param>
        /// <param name="isLordForce">A lord's party or the player - not bandits, caravans or villagers.</param>
        /// <param name="isAttachedToArmy">Already contained in its army leader's strength.</param>
        /// <param name="stance">Hostile, friendly or neutral to the settlement's owner.</param>
        /// <param name="belongsToRequester">
        /// The party asking about this garrison, or its army. It must not count as a
        /// defender of the garrison it is about to strip or fill.
        /// </param>
        public NearbyForce(float strength, float distance, float searchRadius,
            bool isLordForce, bool isAttachedToArmy, ForceStance stance, bool belongsToRequester = false)
        {
            Strength = strength;
            Distance = distance;
            SearchRadius = searchRadius;
            IsLordForce = isLordForce;
            IsAttachedToArmy = isAttachedToArmy;
            Stance = stance;
            BelongsToRequester = belongsToRequester;
        }

        public float Strength { get; }
        public float Distance { get; }
        public float SearchRadius { get; }
        public bool IsLordForce { get; }
        public bool IsAttachedToArmy { get; }
        public ForceStance Stance { get; }
        public bool BelongsToRequester { get; }
    }
}
