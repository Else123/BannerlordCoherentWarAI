using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free scoring for which war to press and which conquests are worth
    /// holding.
    ///
    /// Two vanilla shortcomings are addressed here. A kingdom at war with three
    /// neighbours treats all of them alike, dribbling a party at each front instead
    /// of finishing one war. And nothing in the target scoring cares what the border
    /// looks like *after* a conquest, so realms grow long indefensible salients that
    /// promptly get carved off again.
    /// </summary>
    public static class StrategicPriority
    {
        /// <summary>Preference for fiefs of the enemy we have chosen to finish first.</summary>
        public const float DefaultPrimaryEnemyBoost = 1.25f;

        /// <summary>
        /// How far other wars are set aside - damped, never abandoned. Kept mild:
        /// most enemies are secondary at any moment, so this applies to the majority
        /// of targets and compounds with every other weight. What separates the
        /// priority war is the ratio to the boost, not the absolute damping.
        /// </summary>
        public const float DefaultSecondaryEnemyDamp = 0.9f;

        /// <summary>Reward for a conquest that rounds off the border.</summary>
        public const float DefaultConsolidationBonus = 0.35f;

        /// <summary>Penalty for a conquest that juts out into enemy ground.</summary>
        public const float DefaultSalientPenalty = 0.4f;

        /// <summary>How much a shared border adds on top of an enemy's weight.</summary>
        public const float DefaultBorderWeight = 0.15f;

        /// <summary>
        /// Ranks an enemy for the realm's attention. The war that matters is the one
        /// against someone who could actually hurt us, so strength decides.
        ///
        /// Measured in a long campaign, realms sit at nine to fourteen simultaneous
        /// wars, most of them against minor factions with no territory. Ranking by
        /// shared border alone treated those as comparable to a neighbouring kingdom
        /// fielding twenty parties. An enemy we share no border with scores nothing:
        /// however dangerous, we cannot march on them.
        /// </summary>
        /// <param name="enemyStrength">The enemy's total strength.</param>
        /// <param name="sharedBorders">Our fortifications adjoining theirs.</param>
        public static float PrimaryEnemyScore(float enemyStrength, int sharedBorders, float borderWeight)
        {
            if (sharedBorders <= 0 || enemyStrength <= 0f)
            {
                return 0f;
            }
            return enemyStrength * (1f + Math.Max(0f, borderWeight) * sharedBorders);
        }

        /// <summary>
        /// Concentrates a realm on one enemy at a time. Secondary enemies are damped
        /// rather than ignored: a kingdom must still be able to answer a second
        /// front, just not go looking for one.
        /// </summary>
        public static float EnemyFocusBias(bool isPrimaryEnemy, float primaryBoost, float secondaryDamp)
        {
            return isPrimaryEnemy ? Math.Max(0f, primaryBoost) : Math.Max(0f, secondaryDamp);
        }

        /// <summary>
        /// How defensible a settlement would be once taken, judged from who would
        /// surround it.
        ///
        /// A fief whose neighbours are mostly ours rounds the border off and is
        /// rewarded. One ringed by enemy holdings becomes a salient - expensive to
        /// garrison, easy to cut off - and is discouraged even when it looks like
        /// easy prey. Neither effect is absolute: this nudges target choice, it does
        /// not veto conquests.
        /// </summary>
        /// <param name="friendlyNeighbors">Neighbouring fortifications already ours.</param>
        /// <param name="hostileNeighbors">Neighbouring fortifications that would remain foreign.</param>
        public static float HoldabilityBias(int friendlyNeighbors, int hostileNeighbors, float consolidationBonus, float salientPenalty)
        {
            int total = friendlyNeighbors + hostileNeighbors;
            if (total <= 0)
            {
                return 1f;
            }

            // -1 (fully surrounded by enemies) .. +1 (fully enclosed by our own)
            float balance = (float)(friendlyNeighbors - hostileNeighbors) / total;

            // A conquest target is enemy ground, so of course it has enemy
            // neighbours - judging that alone marked nearly every target a salient,
            // which drags all scores down without telling them apart. Only the
            // genuinely lopsided cases count: a fief with some friendly ground near
            // it is an ordinary border objective, not a salient.
            if (balance > -NeutralBand && balance < NeutralBand)
            {
                return 1f;
            }

            // Rescale so the effect ramps from the edge of the band, not from zero.
            float excess = (Math.Abs(balance) - NeutralBand) / (1f - NeutralBand);

            float factor = balance >= 0f
                ? 1f + Math.Max(0f, consolidationBonus) * excess
                : 1f - Math.Max(0f, salientPenalty) * excess;

            return factor < 0f ? 0f : factor;
        }

        /// <summary>
        /// How lopsided a settlement's surroundings must be before it counts as
        /// either a salient or a consolidation rather than an ordinary objective.
        /// </summary>
        private const float NeutralBand = 0.5f;

        /// <summary>
        /// Translates the vanilla per-war priority (a stance field the engine stores
        /// and reads but only ever applies to the player's own kingdom) into a score
        /// multiplier, so AI realms get the same notion of a prioritised war.
        /// </summary>
        /// <param name="behaviorPriority">Vanilla stance priority: 1 = de-prioritised, 2 = prioritised.</param>
        public static float WarPriorityBias(int behaviorPriority, float primaryBoost, float secondaryDamp)
        {
            switch (behaviorPriority)
            {
                case 2:
                    return Math.Max(0f, primaryBoost);
                case 1:
                    return Math.Max(0f, secondaryDamp);
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Settles which war matters, from the two sources that have an opinion:
        /// the stance priority the engine records, and our own reading of where the
        /// fronts actually are.
        ///
        /// They answer the same question, so they are chosen between rather than
        /// multiplied - stacking two "this war is secondary" verdicts would damp a
        /// front twice for one reason. An explicit stance priority wins; our
        /// heuristic only speaks when the engine has said nothing.
        /// </summary>
        public static float CombinedWarFocus(int behaviorPriority, bool isPrimaryEnemy, float primaryBoost, float secondaryDamp)
        {
            if (behaviorPriority == 1 || behaviorPriority == 2)
            {
                return WarPriorityBias(behaviorPriority, primaryBoost, secondaryDamp);
            }
            return EnemyFocusBias(isPrimaryEnemy, primaryBoost, secondaryDamp);
        }

        /// <summary>
        /// Floor for everything this mod multiplies onto a vanilla target score.
        ///
        /// Each individual weight is a mild nudge, but four of them compound: an
        /// unlucky target could fall to a few percent of its vanilla score, and
        /// since defending and patrolling are scored by paths we do not touch, that
        /// would not just re-rank attacks - it would stop a lord attacking at all.
        /// Whatever the weights say, an offensive stays on the table.
        /// </summary>
        public static float ApplyWeightFloor(float combinedWeight, float floor)
        {
            float limit = floor > 0f ? floor : 0f;

            // Anything not a usable number falls back to the floor rather than
            // propagating. NaN compares false against everything, so a plain
            // less-than test would pass it straight through - and a NaN score does
            // not merely rank badly, it silently corrupts every comparison the AI
            // makes with it. Strength values can reach infinity in a long campaign,
            // and infinity over infinity is NaN.
            if (float.IsNaN(combinedWeight) || float.IsInfinity(combinedWeight))
            {
                return limit;
            }

            // Negative input is nonsensical but must still honour the floor: this
            // function exists so a score never disappears entirely.
            return combinedWeight < limit ? limit : combinedWeight;
        }
    }
}
