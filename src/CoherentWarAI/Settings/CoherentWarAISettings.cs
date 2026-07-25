using CoherentWarAI.Logic;

namespace CoherentWarAI.Settings
{
    /// <summary>
    /// Tunable settings for the mod. For now this is a plain POCO seeded with the
    /// neutral-by-default weights; an MCM adapter is wired in a later slice, which
    /// is why access goes through <see cref="Current"/> rather than constants.
    /// </summary>
    public class CoherentWarAISettings
    {
        // Slice A - target selection de-greeding.
        public bool EnableTargetDeGreed = true;
        public float OverkillOnset = TargetWeights.DefaultOverkillOnset;
        public float OverkillMinFactor = TargetWeights.DefaultOverkillMinFactor;
        public float OverkillSpan = TargetWeights.DefaultOverkillSpan;
        public float FrontFloor = TargetWeights.DefaultFrontFloor;
        public float FrontGain = TargetWeights.DefaultFrontGain;

        // Commitment hysteresis - stops lords dithering in front of a castle.
        public bool EnableCommitmentHysteresis = true;
        public float EngageRatio = EngagementHysteresis.DefaultEngageRatio;
        public float AbandonRatio = EngagementHysteresis.DefaultAbandonRatio;
        public float MinCommitmentHours = EngagementHysteresis.DefaultMinCommitmentHours;
        public float RetentionDecayHours = EngagementHysteresis.DefaultRetentionDecayHours;
        public float CollapseRatio = EngagementHysteresis.DefaultCollapseRatio;

        // Slice B-def - defense-first posture.
        public bool EnableDefensivePosture = true;
        public float AggressiveShare = PosturePlanner.DefaultAggressiveShare;
        public int MinimumDefenders = PosturePlanner.DefaultMinimumDefenders;
        public float ValorWeight = PosturePlanner.DefaultValorWeight;

        /// <summary>
        /// Whether the player's own clan parties (companion parties) are given
        /// objectives too. On by default: they otherwise wander off and die.
        /// </summary>
        public bool ManagePlayerClanParties = true;

        // Slice C - threat- and chokepoint-aware garrisons.
        public bool EnableGarrisonThreatAwareness = true;
        public float InteriorBase = GarrisonPlanner.DefaultInteriorBase;
        public float BorderBase = GarrisonPlanner.DefaultBorderBase;
        public float GarrisonThreatGain = GarrisonPlanner.DefaultThreatGain;
        public float GarrisonThreatCap = GarrisonPlanner.DefaultThreatCap;
        public float PeaceCap = GarrisonPlanner.DefaultPeaceCap;
        public float AllyWeight = 0.25f;
        public float ChokepointGain = GarrisonPlanner.DefaultChokepointGain;
        public float ChokepointSaturation = GarrisonPlanner.DefaultChokepointSaturation;
        public int RecruitCapMax = GarrisonPlanner.DefaultRecruitCapMax;

        /// <summary>Active settings instance. Replaced by the MCM adapter later.</summary>
        public static CoherentWarAISettings Current { get; set; } = new CoherentWarAISettings();
    }
}
