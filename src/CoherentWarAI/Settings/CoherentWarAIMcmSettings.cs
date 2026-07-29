using CoherentWarAI.Logic;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace CoherentWarAI.Settings
{
    /// <summary>
    /// In-game settings page (Options -> Mod Options). Every weight this mod applies
    /// is exposed here, because the shipped values are conservative starting points
    /// rather than tuned ones - and tuning them should not require a rebuild.
    ///
    /// This is the user-facing model; <see cref="CoherentWarAISettings"/> remains the
    /// plain object the AI logic reads, and <see cref="Apply"/> translates between
    /// them. Each feature has a master toggle so it can be isolated when judging
    /// what a campaign is actually doing.
    /// </summary>
    public sealed class CoherentWarAIMcmSettings : AttributeGlobalSettings<CoherentWarAIMcmSettings>
    {
        public override string Id => "CoherentWarAI_v1";
        public override string DisplayName => "Coherent War AI";
        public override string FolderName => "CoherentWarAI";
        public override string FormatType => "json2";

        private const string DefenceGroup = "1. Defence first";
        private const string TargetGroup = "2. Target selection";
        private const string CommitmentGroup = "3. Commitment";
        private const string CoordinationGroup = "4. Coordination";
        private const string MarshalGroup = "5. Marshal doctrine";
        private const string StrategyGroup = "6. Strategy";
        private const string GarrisonGroup = "7. Garrisons and gateways";
        private const string DiagnosticsGroup = "8. Diagnostics";

        // --- 1. Defence first -------------------------------------------------

        [SettingPropertyBool("Lords defend by default", Order = 0, RequireRestart = false,
            HintText = "Vanilla never assigns AI lords an objective, so nobody guards their realm until a settlement is already under attack. With this on, defending is the default and only a limited number are released to attack.")]
        [SettingPropertyGroup(DefenceGroup, GroupOrder = 0)]
        public bool EnableDefensivePosture { get; set; } = true;

        [SettingPropertyFloatingInteger("Share allowed to attack", 0.05f, 1f, "0%", Order = 1, RequireRestart = false,
            HintText = "Fraction of a realm's lords released for offence when nothing is threatened. Shrinks automatically as more of the realm comes under threat. Cannot be set to zero: that would freeze every AI offensive in the game permanently.")]
        [SettingPropertyGroup(DefenceGroup, GroupOrder = 0)]
        public float AggressiveShare { get; set; } = PosturePlanner.DefaultAggressiveShare;

        [SettingPropertyInteger("Defenders always held back", 0, 10, "0", Order = 2, RequireRestart = false,
            HintText = "Lords never released for offence. Capped so a small realm is never left unable to attack at all.")]
        [SettingPropertyGroup(DefenceGroup, GroupOrder = 0)]
        public int MinimumDefenders { get; set; } = PosturePlanner.DefaultMinimumDefenders;

        [SettingPropertyFloatingInteger("Weight of a lord's Valor", 0f, 0.45f, "0.00", Order = 3, RequireRestart = false,
            HintText = "How strongly the Valor trait decides who leads attacks. 0 means only strength counts. Capped below 0.5, above which the most cautious lords would score zero regardless of their troops and could no longer be ranked at all.")]
        [SettingPropertyGroup(DefenceGroup, GroupOrder = 0)]
        public float ValorWeight { get; set; } = PosturePlanner.DefaultValorWeight;

        [SettingPropertyBool("Manage your own clan parties", Order = 4, RequireRestart = false,
            HintText = "Give your companions' parties objectives too, so they stop wandering off and losing their troops. Your own party is never touched.")]
        [SettingPropertyGroup(DefenceGroup, GroupOrder = 0)]
        public bool ManagePlayerClanParties { get; set; } = true;

        // --- 2. Target selection ---------------------------------------------

        [SettingPropertyBool("De-greed target selection", Order = 0, RequireRestart = false,
            HintText = "Vanilla scoring is dominated by attacker-to-defender strength, so every lord converges on whichever fief is momentarily weakest.")]
        [SettingPropertyGroup(TargetGroup, GroupOrder = 1)]
        public bool EnableTargetDeGreed { get; set; } = true;

        [SettingPropertyFloatingInteger("Overkill onset", 1f, 4f, "0.0", Order = 1, RequireRestart = false,
            HintText = "Strength ratio beyond which extra superiority stops making a target more attractive.")]
        [SettingPropertyGroup(TargetGroup, GroupOrder = 1)]
        public float OverkillOnset { get; set; } = TargetWeights.DefaultOverkillOnset;

        [SettingPropertyFloatingInteger("Overkill damping", 0.1f, 1f, "0.00", Order = 2, RequireRestart = false,
            HintText = "How far a wildly over-matched target is pushed down. 1 disables the damping.")]
        [SettingPropertyGroup(TargetGroup, GroupOrder = 1)]
        public float OverkillMinFactor { get; set; } = TargetWeights.DefaultOverkillMinFactor;

        [SettingPropertyFloatingInteger("Front coherence floor", 0.1f, 1f, "0.00", Order = 3, RequireRestart = false,
            HintText = "Score multiplier for a target with no friendly ground near it. Lower means deep strikes are discouraged harder.")]
        [SettingPropertyGroup(TargetGroup, GroupOrder = 1)]
        public float FrontFloor { get; set; } = TargetWeights.DefaultFrontFloor;

        [SettingPropertyFloatingInteger("Front coherence gain", 0f, 2f, "0.00", Order = 4, RequireRestart = false,
            HintText = "Extra weight for targets on our own front.")]
        [SettingPropertyGroup(TargetGroup, GroupOrder = 1)]
        public float FrontGain { get; set; } = TargetWeights.DefaultFrontGain;

        [SettingPropertyFloatingInteger("Minimum score floor", 0.05f, 0.9f, "0.00", Order = 5, RequireRestart = false,
            HintText = "Least this mod may reduce a vanilla score to, however many weights stack. Attacks compete with defending and patrolling, so an over-damped attack would lose to standing around. Zero is unreachable because that guarantee is the point; 1 is unreachable because it would clamp away every damping weight and disable half the mod.")]
        [SettingPropertyGroup(TargetGroup, GroupOrder = 1)]
        public float MinimumWeightFloor { get; set; } = 0.25f;

        // --- 3. Commitment ----------------------------------------------------

        [SettingPropertyBool("Stop dithering at the gates", Order = 0, RequireRestart = false,
            HintText = "Vanilla re-decides every target each tick against one hard threshold, so a lord flips between attacking and aborting whenever defenders change - which is also exploitable by stepping in and out of a settlement.")]
        [SettingPropertyGroup(CommitmentGroup, GroupOrder = 2)]
        public bool EnableCommitmentHysteresis { get; set; } = true;

        [SettingPropertyFloatingInteger("Ratio to begin an attack", 1f, 4f, "0.0", Order = 1, RequireRestart = false,
            HintText = "Strength ratio required to commit to a new target.")]
        [SettingPropertyGroup(CommitmentGroup, GroupOrder = 2)]
        public float EngageRatio { get; set; } = EngagementHysteresis.DefaultEngageRatio;

        [SettingPropertyFloatingInteger("Ratio to abandon one", 0.5f, 3f, "0.0", Order = 2, RequireRestart = false,
            HintText = "Ratio below which a committed lord finally gives up. The gap to the value above is the hysteresis band.")]
        [SettingPropertyGroup(CommitmentGroup, GroupOrder = 2)]
        public float AbandonRatio { get; set; } = EngagementHysteresis.DefaultAbandonRatio;

        [SettingPropertyFloatingInteger("Protected commitment (hours)", 0f, 48f, "0", Order = 3, RequireRestart = false,
            HintText = "How long a fresh decision is left alone. An outright collapse of the lord's own force still ends it.")]
        [SettingPropertyGroup(CommitmentGroup, GroupOrder = 2)]
        public float MinCommitmentHours { get; set; } = EngagementHysteresis.DefaultMinCommitmentHours;

        // --- 4. Coordination --------------------------------------------------

        [SettingPropertyBool("Stop lords dogpiling one fief", Order = 0, RequireRestart = false,
            HintText = "Vanilla has no term anywhere for how much of our army is already heading somewhere, so whichever fief looks best to one lord looks best to all.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public bool EnableCoordination { get; set; } = true;

        [SettingPropertyFloatingInteger("Force worth sending", 1f, 5f, "0.0", Order = 1, RequireRestart = false,
            HintText = "Strength considered sufficient for a target, as a multiple of its defenders. Arrivals beyond this are pushed elsewhere.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public float RequiredMargin { get; set; } = ClaimPlanner.DefaultRequiredMargin;

        [SettingPropertyFloatingInteger("Crowding penalty", 0f, 1f, "0.00", Order = 2, RequireRestart = false,
            HintText = "Score multiplier for joining an effort that is already more than sufficient.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public float SaturationSuppression { get; set; } = ClaimPlanner.DefaultSaturationSuppression;

        [SettingPropertyBool("Defenders hold the gateways", Order = 3, RequireRestart = false,
            HintText = "Post defending lords on the routes an invader must pass through, rather than wherever the last alarm came from. Suspended for anywhere already under attack.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public bool EnableGatewayDefense { get; set; } = true;

        [SettingPropertyFloatingInteger("Pull toward gateways", 0f, 2f, "0.00", Order = 4, RequireRestart = false,
            HintText = "How strongly gateways attract defenders. Kept modest on purpose: watching a quiet gate must never outrank relieving a burning town.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public float GatewayDefenseGain { get; set; } = ClaimPlanner.DefaultGatewayDefenseGain;

        // --- 5. Marshal doctrine ---------------------------------------------

        [SettingPropertyBool("Offensives are led", Order = 0, RequireRestart = false,
            HintText = "Only appointed marshals raise armies, so a realm sends a few real hosts instead of a scatter of small parties beaten one at a time. Your own clan and rulers are never restricted.")]
        [SettingPropertyGroup(MarshalGroup, GroupOrder = 4)]
        public bool EnableMarshalDoctrine { get; set; } = true;

        [SettingPropertyInteger("Lords per marshal", 1, 20, "0", Order = 1, RequireRestart = false,
            HintText = "How many offensive lords one marshal is expected to absorb. Higher means fewer, larger hosts.")]
        [SettingPropertyGroup(MarshalGroup, GroupOrder = 4)]
        public int SlotsPerMarshal { get; set; } = MarshalPlanner.DefaultSlotsPerMarshal;

        [SettingPropertyInteger("Simultaneous offensives", 1, 6, "0", Order = 2, RequireRestart = false,
            HintText = "Upper bound on marshals per realm. Several converging campaigns is the incoherence problem, not the fix.")]
        [SettingPropertyGroup(MarshalGroup, GroupOrder = 4)]
        public int MaxMarshals { get; set; } = MarshalPlanner.DefaultMaxMarshals;

        // --- 6. Strategy ------------------------------------------------------

        [SettingPropertyBool("Press one war at a time", Order = 0, RequireRestart = false,
            HintText = "A kingdom at war with three neighbours otherwise treats them alike and concludes none of them. Secondary wars are damped, never abandoned.")]
        [SettingPropertyGroup(StrategyGroup, GroupOrder = 5)]
        public bool EnableEnemyFocus { get; set; } = true;

        [SettingPropertyFloatingInteger("Priority war weight", 1f, 2f, "0.00", Order = 1, RequireRestart = false,
            HintText = "Preference for fiefs of the enemy chosen to be finished first.")]
        [SettingPropertyGroup(StrategyGroup, GroupOrder = 5)]
        public float PrimaryEnemyBoost { get; set; } = StrategicPriority.DefaultPrimaryEnemyBoost;

        [SettingPropertyFloatingInteger("Other wars weight", 0.1f, 1f, "0.00", Order = 2, RequireRestart = false,
            HintText = "How far other fronts are set aside. Too low and a second front stops being answered at all.")]
        [SettingPropertyGroup(StrategyGroup, GroupOrder = 5)]
        public float SecondaryEnemyDamp { get; set; } = StrategicPriority.DefaultSecondaryEnemyDamp;

        [SettingPropertyBool("Want conquests worth holding", Order = 3, RequireRestart = false,
            HintText = "Prefer fiefs that round off the border over ones jutting into enemy ground. OFF by default: measured in play it penalised 91% of all targets and rewarded none, because a conquest target is enemy ground by definition. Front coherence already judges nearness to our own land, and does it better.")]
        [SettingPropertyGroup(StrategyGroup, GroupOrder = 5)]
        public bool EnableHoldability { get; set; } = false;

        [SettingPropertyFloatingInteger("Salient penalty", 0f, 0.9f, "0.00", Order = 4, RequireRestart = false,
            HintText = "How far a conquest ringed by enemy holdings is discouraged. Only genuinely lopsided cases count - an ordinary border objective is left alone. A nudge, never a veto.")]
        [SettingPropertyGroup(StrategyGroup, GroupOrder = 5)]
        public float SalientPenalty { get; set; } = StrategicPriority.DefaultSalientPenalty;

        [SettingPropertyFloatingInteger("Consolidation bonus", 0f, 1f, "0.00", Order = 5, RequireRestart = false,
            HintText = "Extra weight for taking a fief whose neighbours are already ours, rounding off the border.")]
        [SettingPropertyGroup(StrategyGroup, GroupOrder = 5)]
        public float ConsolidationBonus { get; set; } = StrategicPriority.DefaultConsolidationBonus;

        // --- 7. Garrisons and gateways ---------------------------------------

        [SettingPropertyBool("Garrisons reflect the map", Order = 0, RequireRestart = false,
            HintText = "Vanilla sizes garrisons from economics alone, so the fief the enemy marches through is defended no better than one deep inside the realm.")]
        [SettingPropertyGroup(GarrisonGroup, GroupOrder = 6)]
        public bool EnableGarrisonThreatAwareness { get; set; } = true;

        [SettingPropertyFloatingInteger("Interior garrison size", 0.3f, 1.5f, "0.00", Order = 1, RequireRestart = false,
            HintText = "Multiplier for quiet interior holdings. Below 1 frees those troops for the field army.")]
        [SettingPropertyGroup(GarrisonGroup, GroupOrder = 6)]
        public float InteriorBase { get; set; } = GarrisonPlanner.DefaultInteriorBase;

        [SettingPropertyFloatingInteger("Border garrison size", 1f, 3f, "0.00", Order = 2, RequireRestart = false,
            HintText = "Multiplier for fiefs on a hostile border. Raise with care: garrisons cost wages and food.")]
        [SettingPropertyGroup(GarrisonGroup, GroupOrder = 6)]
        public float BorderBase { get; set; } = GarrisonPlanner.DefaultBorderBase;

        [SettingPropertyInteger("Daily recruits when threatened", 1, 6, "0", Order = 3, RequireRestart = false,
            HintText = "Vanilla allows one a day everywhere, which cannot refill a frontier garrison between raids.")]
        [SettingPropertyGroup(GarrisonGroup, GroupOrder = 6)]
        public int RecruitCapMax { get; set; } = GarrisonPlanner.DefaultRecruitCapMax;

        [SettingPropertyBool("Find gateways by route", Order = 4, RequireRestart = false,
            HintText = "Walk the campaign map's travel graph to find the settlements an invader must pass through, instead of merely counting foreign neighbours. Alternative routes dissolve a gate's importance.")]
        [SettingPropertyGroup(GarrisonGroup, GroupOrder = 6)]
        public bool EnableChokepointAnalysis { get; set; } = true;

        [SettingPropertyFloatingInteger("Gateway garrison bonus", 0f, 2f, "0.00", Order = 5, RequireRestart = false,
            HintText = "Extra garrison for the gates of a realm, on top of the border multiplier.")]
        [SettingPropertyGroup(GarrisonGroup, GroupOrder = 6)]
        public float ChokepointGain { get; set; } = GarrisonPlanner.DefaultChokepointGain;

        [SettingPropertyBool("Idle defenders hunt bandits", Order = 6, RequireRestart = false,
            HintText = "Vanilla lords never seek bandits out at all - they only fight them by walking into them. Lords held back for defence with nothing to do will now clear nearby bands, which protects villages and is how troops gain experience.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public bool EnableBanditHunting { get; set; } = true;

        [SettingPropertyFloatingInteger("Superiority needed to spare lords", 1f, 3f, "0.00", Order = 7, RequireRestart = false,
            HintText = "How much stronger than its main enemy a realm must be before it sends anyone after bandits. At 1.0 even an evenly matched war allows it; higher values keep every lord at the front.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public float BanditRequiredSuperiority { get; set; } = BanditHuntPlanner.DefaultRequiredSuperiority;

        [SettingPropertyFloatingInteger("Advantage needed over a band", 1f, 4f, "0.00", Order = 8, RequireRestart = false,
            HintText = "Strength a hunter needs over its quarry. A lord lost to bandits is worse than bandits left alone.")]
        [SettingPropertyGroup(CoordinationGroup, GroupOrder = 3)]
        public float BanditRequiredAdvantage { get; set; } = BanditHuntPlanner.DefaultRequiredHunterAdvantage;

        // --- 8. Diagnostics ---------------------------------------------------

        [SettingPropertyBool("Write a decision log", Order = 0, RequireRestart = true,
            HintText = "Records what the AI decided and why, in Documents/Mount and Blade II Bannerlord/CoherentWarAI. On by default: the weights above ship untuned, and tuning them without a trace is guesswork.")]
        [SettingPropertyGroup(DiagnosticsGroup, GroupOrder = 7)]
        public bool EnableLogging { get; set; } = true;

        [SettingPropertyBool("Log every target score", Order = 1, RequireRestart = true,
            HintText = "Very noisy - target scoring runs hundreds of times per game hour. Only worth enabling to dissect one specific situation.")]
        [SettingPropertyGroup(DiagnosticsGroup, GroupOrder = 7)]
        public bool VerboseScoreLogging { get; set; } = false;

        /// <summary>
        /// Copies the page into the plain settings object the AI logic reads.
        /// Values not exposed here keep their defaults - they are derived or too
        /// fine-grained to be worth a slider.
        /// </summary>
        public void Apply(CoherentWarAISettings target)
        {
            if (target == null)
            {
                return;
            }

            target.EnableDefensivePosture = EnableDefensivePosture;
            target.AggressiveShare = AggressiveShare;
            target.MinimumDefenders = MinimumDefenders;
            target.ValorWeight = ValorWeight;
            target.ManagePlayerClanParties = ManagePlayerClanParties;

            target.EnableTargetDeGreed = EnableTargetDeGreed;
            target.OverkillOnset = OverkillOnset;
            target.OverkillMinFactor = OverkillMinFactor;
            target.FrontFloor = FrontFloor;
            target.FrontGain = FrontGain;
            target.MinimumWeightFloor = MinimumWeightFloor;

            target.EnableCommitmentHysteresis = EnableCommitmentHysteresis;
            target.EngageRatio = EngageRatio;
            target.AbandonRatio = AbandonRatio;
            target.MinCommitmentHours = MinCommitmentHours;

            target.EnableCoordination = EnableCoordination;
            target.RequiredMargin = RequiredMargin;
            target.SaturationSuppression = SaturationSuppression;
            target.EnableGatewayDefense = EnableGatewayDefense;
            target.GatewayDefenseGain = GatewayDefenseGain;

            target.EnableBanditHunting = EnableBanditHunting;
            target.BanditRequiredSuperiority = BanditRequiredSuperiority;
            target.BanditRequiredAdvantage = BanditRequiredAdvantage;

            target.EnableMarshalDoctrine = EnableMarshalDoctrine;
            target.SlotsPerMarshal = SlotsPerMarshal;
            target.MaxMarshals = MaxMarshals;

            target.EnableEnemyFocus = EnableEnemyFocus;
            target.PrimaryEnemyBoost = PrimaryEnemyBoost;
            target.SecondaryEnemyDamp = SecondaryEnemyDamp;
            target.EnableHoldability = EnableHoldability;
            target.SalientPenalty = SalientPenalty;
            target.ConsolidationBonus = ConsolidationBonus;

            target.EnableGarrisonThreatAwareness = EnableGarrisonThreatAwareness;
            target.InteriorBase = InteriorBase;
            target.BorderBase = BorderBase;
            target.RecruitCapMax = RecruitCapMax;
            target.EnableChokepointAnalysis = EnableChokepointAnalysis;
            target.ChokepointGain = ChokepointGain;

            target.EnableLogging = EnableLogging;
            target.VerboseScoreLogging = VerboseScoreLogging;
        }
    }
}

