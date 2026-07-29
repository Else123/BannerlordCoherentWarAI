using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free pure scoring weights for Slice A (target selection de-greeding).
    /// Uses only <c>System</c> so it compiles into the net10 test project and runs
    /// with <c>dotnet test</c> on the host SDK - no TaleWorlds/game dependency.
    ///
    /// The Slice A model override multiplies the vanilla target score by these
    /// factors. Every factor defaults to 1.0 for neutral inputs, so a mis-tuned or
    /// disabled weight is a no-op, never a regression.
    /// </summary>
    public static class TargetWeights
    {
        // Suggested starting-point defaults. Tuned in-game later.
        public const float DefaultOverkillOnset = 1.5f;
        public const float DefaultOverkillMinFactor = 0.6f;
        public const float DefaultOverkillSpan = 1.5f;
        public const float DefaultFrontFloor = 0.6f;
        public const float DefaultFrontGain = 0.9f;

        /// <summary>The vanilla defender-strength estimate is floored at this value.</summary>
        public const float MinDefenderStrength = 100f;

        /// <summary>
        /// Corrects for who vanilla leaves out when rating a settlement's defenders.
        ///
        /// Two omissions matter. Vanilla counts the garrison and whoever is standing
        /// *inside* the walls, but a relieving force a few hours away is just as real
        /// - it will be there before the siege is decided. And the player is
        /// deliberately discounted: their own party counts half, less again while
        /// they are inside.
        ///
        /// Together those produce the behaviour players notice: an enemy lord's
        /// arithmetic lurches every time the player rides through a gate, though
        /// nothing about the defence actually changed. Standing outside a castle
        /// invites an attack that standing inside would have deterred - the same
        /// force, counted differently. Judging by who could actually fight makes the
        /// decision stable for an honest reason rather than a hysteresis papering
        /// over an unstable one.
        ///
        /// A target's score moves inversely with how strong its defenders look, so
        /// the correction is what vanilla saw over what is really available. Capped
        /// at 1: this only ever makes a defended target less inviting.
        /// </summary>
        /// <param name="asVanillaCountedThem">Defending strength as vanilla scored it.</param>
        /// <param name="allWhoCouldDefend">Everyone able to fight for the settlement, at full weight.</param>
        public static float DefenderVisibilityCorrection(float asVanillaCountedThem, float allWhoCouldDefend)
        {
            float seen = Math.Max(MinDefenderStrength, asVanillaCountedThem);
            float real = Math.Max(MinDefenderStrength, allWhoCouldDefend);

            if (real <= seen)
            {
                return 1f;
            }
            return seen / real;
        }

        /// <summary>
        /// W_overkill - flattens the vanilla "ourStrength / defenderStrength" reward
        /// once the attacker is already strong enough, so overkill stops raising the
        /// score and value/coherence decide the target instead of "who is weakest".
        ///
        /// r = ourStrength / max(defenderStrength, <see cref="MinDefenderStrength"/>).
        /// r &lt;= onset            -> 1.0
        /// r &gt;= onset + span     -> minFactor
        /// in between             -> linear interpolation 1.0 -> minFactor.
        /// </summary>
        public static float Overkill(float ourStrength, float defenderStrength, float onset, float minFactor, float span)
        {
            float r = ourStrength / Math.Max(defenderStrength, MinDefenderStrength);
            if (r <= onset || span <= 0f)
            {
                return 1f;
            }
            float t = (r - onset) / span;
            if (t >= 1f)
            {
                return minFactor;
            }
            return 1f + (minFactor - 1f) * t;
        }

        /// <summary>
        /// Where "overwhelming" starts, given how lopsided fights typically are at
        /// this point in the campaign.
        ///
        /// A fixed threshold ages badly. Field armies grow far faster than garrisons
        /// over a long campaign, so a ratio that marked real overkill in the first
        /// year becomes ordinary later: measured after twenty-two years, the damping
        /// fired on 80% of all targets, at which point it is a constant rather than
        /// a signal. Tracking what the odds actually look like keeps the threshold
        /// meaning the same thing throughout.
        ///
        /// Never drops below the configured value, so early-campaign behaviour is
        /// unchanged and this can only ever make the weight more selective.
        /// </summary>
        /// <param name="configuredOnset">The onset from settings.</param>
        /// <param name="typicalRatio">Recently observed typical attacker/defender ratio.</param>
        public static float AdaptiveOnset(float configuredOnset, float typicalRatio)
        {
            return typicalRatio > configuredOnset ? typicalRatio : configuredOnset;
        }

        /// <summary>
        /// W_front - front-coherence. Rewards attacking fiefs on our own front so
        /// distant soft targets stop out-scoring reachable border objectives.
        ///
        /// ownShare = clamp(ownedByUs / max(notOwnedByTarget, 1), 0, 1)
        /// returns frontFloor + frontGain * ownShare.
        /// notOwnedByTarget &lt;= 0 (isolated target) -> frontFloor.
        /// </summary>
        public static float FrontCoherence(int ownedByUs, int notOwnedByTarget, float frontFloor, float frontGain)
        {
            if (notOwnedByTarget <= 0)
            {
                return frontFloor;
            }
            float ownShare = (float)ownedByUs / notOwnedByTarget;
            if (ownShare < 0f)
            {
                ownShare = 0f;
            }
            else if (ownShare > 1f)
            {
                ownShare = 1f;
            }
            return frontFloor + frontGain * ownShare;
        }
    }
}
