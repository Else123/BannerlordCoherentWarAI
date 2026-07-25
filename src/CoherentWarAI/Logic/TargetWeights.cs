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
