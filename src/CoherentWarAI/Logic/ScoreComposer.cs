namespace CoherentWarAI.Logic
{
    /// <summary>Which weights are switched on for a given scoring call.</summary>
    public struct WeightToggles
    {
        public bool CountNearbyDefenders;
        public bool DeGreedTargets;
        public bool Coordination;
        public bool EnemyFocus;
        public bool Holdability;
        public bool CommitmentStickiness;
        public bool ExploitDistraction;
        public bool RequireKnowledge;

        /// <summary>
        /// Whether anything enabled needs the defender estimate.
        ///
        /// This exists as one derived property rather than a condition written at
        /// the call site because getting it wrong is the single most repeated bug in
        /// this mod: three separate times a weight was computed from an input that
        /// only another, unrelated feature caused to be gathered, so switching that
        /// other feature off silently broke this one - once badly enough to score
        /// worse than vanilla. A new weight that needs this input adds itself here,
        /// where it cannot be missed.
        /// </summary>
        public bool NeedsDefenderStrength
        {
            get { return DeGreedTargets || Coordination || CountNearbyDefenders; }
        }

        /// <summary>Whether the front neighbour counts need gathering.</summary>
        public bool NeedsFrontNeighbours
        {
            get { return DeGreedTargets; }
        }

        /// <summary>Whether the post-capture neighbour counts need gathering.</summary>
        public bool NeedsHoldabilityNeighbours
        {
            get { return Holdability; }
        }

        /// <summary>Whether it matters that this lord already pursues the target.</summary>
        public bool NeedsPursuitState
        {
            get { return CommitmentStickiness || Coordination; }
        }
    }

    /// <summary>
    /// Everything the weights need, already extracted from the game. Plain numbers
    /// only, so the whole composition can be exercised without a running campaign -
    /// which is the point: the bugs this structure prevents were all invisible to a
    /// test suite that could not reach into the engine-facing model.
    /// </summary>
    public struct ScoreInputs
    {
        public float AttackerStrength;

        /// <summary>Defending strength as vanilla counts it: who is inside.</summary>
        public float DefenderStrength;

        /// <summary>Everyone who could fight for the place, including relief nearby.</summary>
        public float AvailableDefence;

        public int FrontOwnedByUs;
        public int FrontNotOwnedByTarget;

        /// <summary>Strength of the same realm already heading for this target.</summary>
        public float CommittedStrength;

        public bool IsPursuingTarget;
        public bool IsPrimaryEnemy;

        /// <summary>Vanilla's per-war stance priority: 1 de-prioritised, 2 prioritised, 0 unset.</summary>
        public int StancePriority;

        public int HoldabilityFriendlyNeighbours;
        public int HoldabilityHostileNeighbours;

        /// <summary>Share of the target owner's strength tied up elsewhere, 0..1.</summary>
        public float EnemyDistraction;

        /// <summary>Whether the target adjoins land of ours, and so is watched anyway.</summary>
        public bool TargetBordersOurLand;

        /// <summary>Time since we had eyes on the target; negative if we never have.</summary>
        public float HoursSinceObserved;
    }

    /// <summary>The tuning values, lifted out of settings.</summary>
    public struct ScoreTuning
    {
        public float OverkillOnset;
        public float OverkillMinFactor;
        public float OverkillSpan;
        public float FrontFloor;
        public float FrontGain;
        public float RequiredMargin;
        public float SaturationSuppression;
        public float NeglectBonus;
        public float PrimaryEnemyBoost;
        public float SecondaryEnemyDamp;
        public float ConsolidationBonus;
        public float SalientPenalty;
        public float PursuitStickiness;
        public float MinimumWeightFloor;
        public float DistractionOnset;
        public float DistractionExposureBonus;
        public float KnowledgeLifetimeHours;
        public float UnknownPenalty;
    }

    /// <summary>Each weight separately, plus the product actually applied.</summary>
    public struct ScoreWeights
    {
        public float Visibility;
        public float Overkill;
        public float Front;
        public float Coordination;
        public float Strategy;
        public float Commitment;

        /// <summary>How much the owner being busy elsewhere raises this target's appeal.</summary>
        public float Exposure;

        /// <summary>How confidently this target can be acted on at all.</summary>
        public float Knowledge;

        /// <summary>The holdability part of Strategy, kept for diagnostics.</summary>
        public float HoldabilityBias;

        /// <summary>The product before the floor.</summary>
        public float Raw;

        /// <summary>The product after the floor - what a score should be multiplied by.</summary>
        public float Combined;

        /// <summary>Whether the floor had to intervene.</summary>
        public bool WasFloored;
    }

    /// <summary>
    /// Turns the state of a scoring decision into the multiplier applied to
    /// vanilla's target score.
    ///
    /// The arithmetic itself lives in the individual weight classes; this only
    /// decides which of them apply and combines the results. Keeping that in one
    /// engine-free place means every weight is reachable on its own - a feature can
    /// no longer be disabled as a side effect of switching off an unrelated one,
    /// and that property is now something tests can assert rather than something
    /// reviews have to notice.
    /// </summary>
    public static class ScoreComposer
    {
        public static ScoreWeights Compose(ScoreInputs inputs, WeightToggles toggles, ScoreTuning tuning)
        {
            ScoreWeights weights = new ScoreWeights
            {
                Visibility = 1f,
                Overkill = 1f,
                Front = 1f,
                Coordination = 1f,
                Strategy = 1f,
                Commitment = 1f,
                Exposure = 1f,
                Knowledge = 1f,
                HoldabilityBias = 1f
            };

            // Act on what is known. The AI cannot be stopped from seeing the whole
            // map - vanilla's own loops read it directly - but it can be stopped
            // from marching confidently on a castle nobody has laid eyes on.
            if (toggles.RequireKnowledge)
            {
                weights.Knowledge = SightingNetwork.KnowledgeWeight(
                    inputs.TargetBordersOurLand, inputs.HoursSinceObserved,
                    tuning.KnowledgeLifetimeHours, tuning.UnknownPenalty);
            }

            // Strike where the enemy cannot answer. A realm that has thrown its host
            // at a siege of its own cannot also hold its border, and that opening is
            // the difference between a deliberate campaign and an opportunistic one.
            if (toggles.ExploitDistraction)
            {
                weights.Exposure = ForceCommitment.ExposureBonus(
                    inputs.EnemyDistraction, tuning.DistractionOnset, tuning.DistractionExposureBonus);
            }

            // What vanilla could not see: relief close enough to intervene, and the
            // player at full weight rather than discounted.
            if (toggles.CountNearbyDefenders)
            {
                weights.Visibility = TargetWeights.DefenderVisibilityCorrection(
                    inputs.DefenderStrength, inputs.AvailableDefence);
            }

            if (toggles.DeGreedTargets)
            {
                weights.Overkill = TargetWeights.Overkill(
                    inputs.AttackerStrength, inputs.DefenderStrength,
                    tuning.OverkillOnset, tuning.OverkillMinFactor, tuning.OverkillSpan);

                weights.Front = TargetWeights.FrontCoherence(
                    inputs.FrontOwnedByUs, inputs.FrontNotOwnedByTarget,
                    tuning.FrontFloor, tuning.FrontGain);
            }

            // Only lords who would be arriving are steered away; someone already
            // committed here is not joining a pile.
            if (toggles.Coordination && !inputs.IsPursuingTarget)
            {
                float required = ClaimPlanner.RequiredStrength(inputs.DefenderStrength, tuning.RequiredMargin);
                weights.Coordination = ClaimPlanner.SaturationBias(
                    inputs.CommittedStrength, required,
                    tuning.SaturationSuppression, tuning.NeglectBonus);
            }

            if (toggles.EnemyFocus)
            {
                weights.Strategy *= StrategicPriority.CombinedWarFocus(
                    inputs.StancePriority, inputs.IsPrimaryEnemy,
                    tuning.PrimaryEnemyBoost, tuning.SecondaryEnemyDamp);
            }

            if (toggles.Holdability)
            {
                weights.HoldabilityBias = StrategicPriority.HoldabilityBias(
                    inputs.HoldabilityFriendlyNeighbours, inputs.HoldabilityHostileNeighbours,
                    tuning.ConsolidationBonus, tuning.SalientPenalty);
                weights.Strategy *= weights.HoldabilityBias;
            }

            if (toggles.CommitmentStickiness && inputs.IsPursuingTarget)
            {
                weights.Commitment = tuning.PursuitStickiness;
            }

            weights.Raw = weights.Visibility * weights.Overkill * weights.Front
                * weights.Coordination * weights.Strategy * weights.Commitment
                * weights.Exposure * weights.Knowledge;

            // Floored here rather than by the caller, so no future caller can forget
            // to - the guarantee that an offensive never disappears entirely only
            // holds if it is applied every time.
            weights.Combined = StrategicPriority.ApplyWeightFloor(weights.Raw, tuning.MinimumWeightFloor);
            weights.WasFloored = weights.Combined > weights.Raw;

            return weights;
        }
    }
}
