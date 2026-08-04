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

        /// <summary>
        /// Count everyone who could defend a settlement, not only who is inside it,
        /// and count the player at full weight rather than vanilla's discount. This
        /// is what stops an attacker's judgement lurching when the player rides in
        /// or out of a castle.
        /// </summary>
        public bool CountNearbyDefenders = true;

        // Commitment hysteresis - stops lords dithering in front of a castle.
        public bool EnableCommitmentHysteresis = true;
        public float EngageRatio = EngagementHysteresis.DefaultEngageRatio;
        public float AbandonRatio = EngagementHysteresis.DefaultAbandonRatio;
        public float MinCommitmentHours = EngagementHysteresis.DefaultMinCommitmentHours;
        public float RetentionDecayHours = EngagementHysteresis.DefaultRetentionDecayHours;
        public float CollapseRatio = EngagementHysteresis.DefaultCollapseRatio;

        /// <summary>
        /// Extra weight for the target a lord is already heading for. This is what
        /// actually counters the dithering: measured in play, vanilla never rejects
        /// a target outright once a lord is committed to it - lords are lured away
        /// by something else scoring briefly higher instead.
        /// </summary>
        public float PursuitStickiness = EngagementHysteresis.DefaultPursuitStickiness;

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

        // Strategic priority - which war to press, and which conquests to want.
        public bool EnableEnemyFocus = true;
        public float PrimaryEnemyBoost = StrategicPriority.DefaultPrimaryEnemyBoost;
        public float SecondaryEnemyDamp = StrategicPriority.DefaultSecondaryEnemyDamp;

        /// <summary>
        /// How much a shared border counts when ranking enemies, on top of their
        /// strength. Strength leads: a realm juggling a dozen wars should press the
        /// one against someone who could actually hurt it.
        /// </summary>
        public float BorderWeight = StrategicPriority.DefaultBorderWeight;
        /// <summary>
        /// Off by default. Measured over two playtests it penalised 91% of all
        /// targets and rewarded none: a conquest target is enemy ground by
        /// definition, so counting its enemy neighbours says almost nothing.
        /// Judging how near a target is to our own ground is what front coherence
        /// already does, and it does it relatively rather than absolutely - so this
        /// mostly duplicated that while dragging every score down.
        /// </summary>
        public bool EnableHoldability = false;
        public float ConsolidationBonus = StrategicPriority.DefaultConsolidationBonus;
        public float SalientPenalty = StrategicPriority.DefaultSalientPenalty;

        /// <summary>
        /// Least this mod may reduce a vanilla target score to, however many weights
        /// stack against it. Attacks compete with defending and patrolling, which are
        /// scored by paths we do not touch, so an over-damped attack would lose to
        /// standing around rather than merely rank lower.
        /// </summary>
        public float MinimumWeightFloor = 0.25f;

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

        // Strike where the enemy cannot answer.
        //
        // Requires the sighting network, and genuinely so rather than by accident:
        // this acts on what a realm's scouts have reported about enemy forces being
        // committed elsewhere. Without reports there is nothing to act on, and
        // reading the enemy's true commitment instead would hand back the very
        // omniscience the sighting network exists to remove.
        public bool EnableDistractionExploit = true;
        public float DistractionOnset = ForceCommitment.DefaultDistractionOnset;
        public float DistractionExposureBonus = ForceCommitment.DefaultExposureBonus;

        // Sighting network - word of an enemy force travels between lords.
        public bool EnableSightingNetwork = true;
        public float SightingRelaySpeed = SightingNetwork.DefaultRelaySpeed;
        public float SightingLifetimeHours = SightingNetwork.DefaultSightingLifetimeHours;
        public float SightingSpotRadiusFactor = SightingNetwork.DefaultSpotRadiusFactor;

        /// <summary>How far a reported force is treated as able to strike.</summary>
        public float SightingReachFactor = 2f;

        /// <summary>Below this, a force is a passing lord rather than an invasion.</summary>
        public float SightingMinimumStrength = 150f;

        /// <summary>Reported threat considered ordinary, against which urgency is judged.</summary>
        public float SightingTypicalThreat = 400f;

        /// <summary>Most that reports may raise a settlement's defensive pull.</summary>
        public float SightingMaxUrgency = 1.5f;

        /// <summary>
        /// Let a party's scout decide how far it sees and how much its reports are
        /// worth. Off means every party observes equally well, which is what vanilla
        /// effectively does for the AI.
        /// </summary>
        public bool EnableScoutSkill = true;
        public float ScoutingReachBonus = ScoutingQuality.DefaultReachBonus;
        public float ScoutingMinimumConfidence = ScoutingQuality.DefaultMinimumConfidence;

        /// <summary>
        /// Act on what is known. The AI cannot be stopped from seeing the map -
        /// vanilla's own loops read it directly - but this stops it marching
        /// confidently on a castle nobody has laid eyes on. Requires the sighting
        /// network for the same genuine reason as the distraction weight.
        /// </summary>
        public bool EnableKnowledgeWeight = true;

        /// <summary>
        /// How long a look at a settlement stays worth acting on. Ten days: long
        /// enough that a realm does not forget its neighbours between campaigns,
        /// short enough that a year-old glance is not treated as intelligence.
        /// </summary>
        public float KnowledgeLifetimeHours = 240f;

        /// <summary>Most that never having seen a place may reduce its appeal.</summary>
        public float UnknownTargetPenalty = SightingNetwork.DefaultUnknownPenalty;

        // Bandit hunting - idle defenders police the realm.
        public bool EnableBanditHunting = true;
        public float BanditMaxThreatRatio = BanditHuntPlanner.DefaultMaxThreatRatio;
        public float BanditRequiredSuperiority = BanditHuntPlanner.DefaultRequiredSuperiority;
        public float BanditRequiredAdvantage = BanditHuntPlanner.DefaultRequiredHunterAdvantage;
        public float BanditHuntCommitmentHours = BanditHuntPlanner.DefaultHuntCommitmentHours;

        /// <summary>How far afield a hunter looks, as a multiple of the map's settlement spacing.</summary>
        public float BanditSearchRadiusFactor = 1f;

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
