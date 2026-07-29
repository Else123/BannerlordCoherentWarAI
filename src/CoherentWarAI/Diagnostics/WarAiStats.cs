namespace CoherentWarAI.Diagnostics
{
    /// <summary>
    /// Counts how often each weight actually changed a decision, and by how much.
    ///
    /// Target scoring runs hundreds of times per game hour, so logging every
    /// decision buries the file. Counting them instead costs nothing and answers
    /// the question that matters when tuning: is this weight doing anything, and is
    /// it doing too much? A feature that never fires is dead weight; one that fires
    /// on every single target is probably mistuned.
    ///
    /// Reset and reported once a day.
    /// </summary>
    public static class WarAiStats
    {
        private static int _scored;

        private static int _overkillDamped;
        private static float _overkillSum;

        private static int _frontBoosted;
        private static int _frontDamped;

        private static int _coordDiverted;
        private static int _coordEncouraged;

        private static int _focusPreferred;
        private static int _focusDamped;

        private static int _salientPenalised;
        private static int _consolidationRewarded;

        private static int _floorHit;
        private static int _hysteresisHeld;
        private static int _gatewayDefence;
        private static int _banditHunts;

        private static int _rejected;
        private static int _rejectedWhilePursuing;
        private static int _rejectedWithMemory;
        private static int _rejectedMemoryStale;
        private static int _rejectedOddsTooBad;
        private static int _pursuitHeld;
        private static int _defenceCorrected;

        /// <summary>Records one offensive target evaluation and what each weight did to it.</summary>
        public static void RecordScore(float overkill, float front, float coord, float strategy, bool floored)
        {
            _scored++;

            if (overkill < 0.999f)
            {
                _overkillDamped++;
                _overkillSum += overkill;
            }

            if (front > 1.001f)
            {
                _frontBoosted++;
            }
            else if (front < 0.999f)
            {
                _frontDamped++;
            }

            if (coord < 0.999f)
            {
                _coordDiverted++;
            }
            else if (coord > 1.001f)
            {
                _coordEncouraged++;
            }

            if (strategy > 1.001f)
            {
                _focusPreferred++;
            }
            else if (strategy < 0.999f)
            {
                _focusDamped++;
            }

            if (floored)
            {
                _floorHit++;
            }
        }

        /// <summary>Records what the holdability judgement said about a conquest.</summary>
        public static void RecordHoldability(float bias)
        {
            if (bias < 0.999f)
            {
                _salientPenalised++;
            }
            else if (bias > 1.001f)
            {
                _consolidationRewarded++;
            }
        }

        public static void RecordHysteresisHold()
        {
            _hysteresisHeld++;
        }

        /// <summary>
        /// Traces why the hysteresis does or does not fire. It reported zero holds
        /// across two full playtests, and guessing at the cause has already failed
        /// once - these count each step of the path so the break is visible.
        /// </summary>
        public static void RecordRejectedTarget(bool wasPursuing, bool hadMemory, bool memoryStale, bool oddsTooBad)
        {
            _rejected++;
            if (wasPursuing)
            {
                _rejectedWhilePursuing++;
            }
            if (hadMemory)
            {
                _rejectedWithMemory++;
            }
            if (memoryStale)
            {
                _rejectedMemoryStale++;
            }
            if (oddsTooBad)
            {
                _rejectedOddsTooBad++;
            }
        }

        public static void RecordGatewayDefence()
        {
            _gatewayDefence++;
        }

        /// <summary>
        /// Typical attacker-to-defender ratio seen lately, used to keep "overwhelming"
        /// meaning the same thing as a campaign ages. Field armies outgrow garrisons
        /// over decades, so a fixed threshold slowly turns into a constant.
        /// </summary>
        public static float TypicalStrengthRatio { get; private set; } = DefaultTypicalRatio;

        private const float DefaultTypicalRatio = 1.5f;

        /// <summary>
        /// Forgets what was learned from a previous campaign. The ratio feeds the
        /// overkill threshold on the scoring path, so carrying one campaign's power
        /// curve into another would change AI behaviour for reasons that have
        /// nothing to do with the game being played.
        /// </summary>
        public static void ResetForNewCampaign()
        {
            TypicalStrengthRatio = DefaultTypicalRatio;
            _ratioSum = 0f;
            _ratioCount = 0;
        }

        private static float _ratioSum;
        private static int _ratioCount;

        /// <summary>Feeds one observation into the running average.</summary>
        public static void ObserveStrengthRatio(float ourStrength, float defenderStrength)
        {
            if (ourStrength <= 0f || defenderStrength <= 0f)
            {
                return;
            }
            _ratioSum += ourStrength / defenderStrength;
            _ratioCount++;
        }

        public static void RecordDefenceCorrection()
        {
            _defenceCorrected++;
        }

        public static void RecordPursuitHeld()
        {
            _pursuitHeld++;
        }

        public static void RecordBanditHunts(int count)
        {
            _banditHunts += count;
        }

        /// <summary>Writes the day's tally and starts a fresh one.</summary>
        public static void FlushDaily()
        {
            if (!WarAiLog.Enabled)
            {
                Reset();
                return;
            }

            if (_scored == 0)
            {
                Reset();
                return;
            }

            WarAiLog.Section(WarAiLog.GameDate() + " - what the weights did");
            WarAiLog.Write("Effect", string.Format("{0} offensive targets scored", _scored));

            float averageOverkill = _overkillDamped > 0 ? _overkillSum / _overkillDamped : 1f;
            WarAiLog.Write("Effect", string.Format(
                "overkill damping: {0} targets ({1:P0} of them), average factor {2:F2}",
                _overkillDamped, Share(_overkillDamped), averageOverkill));

            WarAiLog.Write("Effect", string.Format(
                "front coherence: {0} on our front, {1} away from it", _frontBoosted, _frontDamped));

            WarAiLog.Write("Effect", string.Format(
                "coordination: {0} lords steered off crowded targets, {1} nudged to neglected ones",
                _coordDiverted, _coordEncouraged));

            WarAiLog.Write("Effect", string.Format(
                "war focus: {0} preferred (priority war), {1} damped (other fronts)",
                _focusPreferred, _focusDamped));

            WarAiLog.Write("Effect", string.Format(
                "border shape: {0} salients discouraged, {1} consolidating conquests favoured",
                _salientPenalised, _consolidationRewarded));

            WarAiLog.Write("Effect", string.Format(
                "score floor reached {0} times ({1:P0}) - high values mean the weights are damping too hard",
                _floorHit, Share(_floorHit)));

            WarAiLog.Write("Effect", string.Format(
                "pursuit kept its target: {0} ({1:P0}); commitments rescued outright: {2}; gateway posting: {3}",
                _pursuitHeld, Share(_pursuitHeld), _hysteresisHeld, _gatewayDefence));

            WarAiLog.Write("Effect", string.Format(
                "idle defenders sent after bandits: {0}", _banditHunts));

            WarAiLog.Write("Effect", string.Format(
                "defenders vanilla overlooked (nearby relief, player at full weight): {0} targets ({1:P0}); "
                + "typical odds {2:F2}, so overkill now measured from {3:F2}",
                _defenceCorrected, Share(_defenceCorrected), TypicalStrengthRatio, TypicalStrengthRatio));

            // Where the hysteresis path breaks: each number is a stage, so the drop
            // between two of them is the answer.
            WarAiLog.Write("Effect", string.Format(
                "targets vanilla rejected: {0} ({1} of them by a lord already pursuing it); "
                + "of those, {2} had a remembered rating, {3} were too stale, {4} had odds too poor",
                _rejected, _rejectedWhilePursuing, _rejectedWithMemory, _rejectedMemoryStale, _rejectedOddsTooBad));

            Reset();
        }

        private static float Share(int count)
        {
            return _scored > 0 ? (float)count / _scored : 0f;
        }

        private static void Reset()
        {
            _scored = 0;
            _overkillDamped = 0;
            _overkillSum = 0f;
            _frontBoosted = 0;
            _frontDamped = 0;
            _coordDiverted = 0;
            _coordEncouraged = 0;
            _focusPreferred = 0;
            _focusDamped = 0;
            _salientPenalised = 0;
            _consolidationRewarded = 0;
            _floorHit = 0;
            _hysteresisHeld = 0;
            _gatewayDefence = 0;
            _banditHunts = 0;
            _rejected = 0;
            _rejectedWhilePursuing = 0;
            _rejectedWithMemory = 0;
            _rejectedMemoryStale = 0;
            _rejectedOddsTooBad = 0;
            _pursuitHeld = 0;
            _defenceCorrected = 0;

            // Carry the observed odds forward as the new baseline, so the threshold
            // tracks the campaign rather than resetting each day.
            if (_ratioCount > 0)
            {
                float observed = _ratioSum / _ratioCount;
                // Smooth it: one unusual day should nudge the baseline, not redefine it.
                TypicalStrengthRatio = TypicalStrengthRatio * 0.8f + observed * 0.2f;
                _ratioSum = 0f;
                _ratioCount = 0;
            }
        }
    }
}
