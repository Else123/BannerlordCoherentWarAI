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

        public static void RecordGatewayDefence()
        {
            _gatewayDefence++;
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
                "commitments held against vanilla: {0}; gateway posting applied: {1}",
                _hysteresisHeld, _gatewayDefence));

            WarAiLog.Write("Effect", string.Format(
                "idle defenders sent after bandits: {0}", _banditHunts));

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
        }
    }
}
