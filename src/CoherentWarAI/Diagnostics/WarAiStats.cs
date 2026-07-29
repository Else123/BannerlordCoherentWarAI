using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

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
        private const float DefaultTypicalRatio = 1.5f;

        /// <summary>
        /// Learned per realm, not globally. A dominant kingdom whose fights are
        /// routinely lopsided would otherwise drag the threshold up for a small
        /// faction that never enjoys those odds, so the weak realm would stop
        /// damping attacks it should still be cautious about - and the reverse for
        /// a struggling one. Every other piece of this mod reasons per realm; this
        /// was the exception.
        /// </summary>
        private static readonly Dictionary<IFaction, RatioAccumulator> Ratios
            = new Dictionary<IFaction, RatioAccumulator>();

        private class RatioAccumulator
        {
            public float Typical = DefaultTypicalRatio;
            public float Sum;
            public int Count;
        }

        /// <summary>Typical odds this realm has been seeing lately.</summary>
        public static float TypicalRatioFor(IFaction faction)
        {
            if (faction == null)
            {
                return DefaultTypicalRatio;
            }
            return Ratios.TryGetValue(faction, out RatioAccumulator acc) ? acc.Typical : DefaultTypicalRatio;
        }

        /// <summary>
        /// Forgets what was learned from a previous campaign. The ratio feeds the
        /// overkill threshold on the scoring path, so carrying one campaign's power
        /// curve into another would change AI behaviour for reasons that have
        /// nothing to do with the game being played.
        /// </summary>
        public static void ResetForNewCampaign()
        {
            Ratios.Clear();
        }

        /// <summary>Feeds one observation into that realm's running average.</summary>
        public static void ObserveStrengthRatio(IFaction faction, float ourStrength, float defenderStrength)
        {
            if (faction == null || ourStrength <= 0f || defenderStrength <= 0f)
            {
                return;
            }

            if (!Ratios.TryGetValue(faction, out RatioAccumulator acc))
            {
                acc = new RatioAccumulator();
                Ratios[faction] = acc;
            }

            acc.Sum += ourStrength / defenderStrength;
            acc.Count++;
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
                "defenders vanilla overlooked (nearby relief, player at full weight): {0} targets ({1:P0})",
                _defenceCorrected, Share(_defenceCorrected)));

            // Per realm, since a dominant kingdom and a struggling one see quite
            // different odds and each should judge overkill by its own experience.
            foreach (KeyValuePair<IFaction, RatioAccumulator> pair in Ratios)
            {
                if (pair.Key != null && pair.Value.Typical > DefaultTypicalRatio * 1.1f)
                {
                    WarAiLog.Write("Effect", string.Format(
                        "  {0} typically fights at {1:F2}:1, so overkill is measured from there",
                        pair.Key.Name, pair.Value.Typical));
                }
            }

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

            // Carry each realm's observed odds forward as its new baseline, so the
            // threshold tracks the campaign rather than resetting each day.
            foreach (KeyValuePair<IFaction, RatioAccumulator> pair in Ratios)
            {
                RatioAccumulator acc = pair.Value;
                if (acc.Count <= 0)
                {
                    continue;
                }

                float observed = acc.Sum / acc.Count;
                // Smooth it: one unusual day should nudge the baseline, not redefine it.
                acc.Typical = acc.Typical * 0.8f + observed * 0.2f;
                acc.Sum = 0f;
                acc.Count = 0;
            }
        }
    }
}
