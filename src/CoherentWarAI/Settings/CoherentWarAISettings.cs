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

        /// <summary>
        /// Route analysis: find the gateways into a realm by walking the campaign
        /// map's travel graph, rather than merely counting foreign neighbours.
        /// </summary>
        public bool EnableChokepointAnalysis = true;

        /// <summary>
        /// How much land behind a gate counts as "a lot", so realms of different
        /// sizes stay comparable.
        /// </summary>
        public float GatewaySaturation = 4f;
        public int RecruitCapMax = GarrisonPlanner.DefaultRecruitCapMax;

        // Marshal doctrine - offensives are led by a few appointed lords.
        public bool EnableMarshalDoctrine = true;
        public int SlotsPerMarshal = MarshalPlanner.DefaultSlotsPerMarshal;
        public int MaxMarshals = MarshalPlanner.DefaultMaxMarshals;
        public float RulerBonus = MarshalPlanner.DefaultRulerBonus;

        // Coordination between a realm's own lords.
        public bool EnableCoordination = true;
        public float RequiredMargin = ClaimPlanner.DefaultRequiredMargin;
        public float SaturationSuppression = ClaimPlanner.DefaultSaturationSuppression;
        public float NeglectBonus = ClaimPlanner.DefaultNeglectBonus;

        /// <summary>Draw defending lords to the gateways rather than to the last alarm.</summary>
        public bool EnableGatewayDefense = true;
        public float GatewayDefenseGain = ClaimPlanner.DefaultGatewayDefenseGain;

        // Diagnostics. On by default: the weights above ship as conservative
        // starting points, and tuning them without a trace is guesswork.
        public bool EnableLogging = true;

        /// <summary>
        /// Log every target-scoring decision. Very noisy - that path runs hundreds
        /// of times per game hour - so only worth enabling to debug one situation.
        /// </summary>
        public bool VerboseScoreLogging = false;

        /// <summary>Active settings instance. Replaced by the MCM adapter later.</summary>
        public static CoherentWarAISettings Current { get; set; } = new CoherentWarAISettings();
    }
}
