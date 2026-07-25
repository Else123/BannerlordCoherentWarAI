using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free garrison sizing.
    ///
    /// Vanilla sizes garrisons purely economically - prosperity, food, the owner
    /// clan's wealth - with no notion of whether a settlement is a quiet interior
    /// holding or the gate the enemy marches through. So border fiefs are no better
    /// defended than safe ones, and auto-recruitment (capped at one troop a day)
    /// cannot refill them in time.
    ///
    /// This scales the vanilla result by how exposed a settlement actually is, and
    /// by whether it is a chokepoint worth holding.
    /// </summary>
    public static class GarrisonPlanner
    {
        public const float DefaultInteriorBase = 0.8f;
        public const float DefaultBorderBase = 1.4f;
        public const float DefaultThreatGain = 0.15f;
        public const float DefaultThreatCap = 4f;
        public const float DefaultPeaceCap = 1.1f;
        public const float DefaultChokepointGain = 0.5f;
        public const float DefaultChokepointSaturation = 2f;
        public const int DefaultRecruitCapMax = 3;

        /// <summary>
        /// How much of a gateway a settlement is: high only when it both faces enemy
        /// ground AND covers friendly ground behind it. A fief with enemies on every
        /// side is an exposed outpost, not a chokepoint; one deep inside our own
        /// territory is not a gate at all.
        ///
        /// Uses the harmonic mean of the two neighbour counts (low whenever either
        /// side is low), then saturates into 0..1 so a hub with many links outranks a
        /// minor crossing without growing without bound.
        /// </summary>
        public static float ChokepointScore(int enemyNeighbors, int friendlyNeighbors, float saturation)
        {
            if (enemyNeighbors <= 0 || friendlyNeighbors <= 0)
            {
                return 0f;
            }
            if (saturation <= 0f)
            {
                return 1f;
            }

            float harmonic = 2f * enemyNeighbors * friendlyNeighbors / (enemyNeighbors + friendlyNeighbors);
            return harmonic / (harmonic + saturation);
        }

        /// <summary>
        /// Exposure factor from position and current pressure. Interior fiefs shrink
        /// (freeing troops for the field), border fiefs grow, and active threat grows
        /// them further. In peacetime the result is capped so realms do not bankrupt
        /// themselves garrisoning against nobody.
        /// </summary>
        public static float ThreatFactor(bool isBorder, float activeThreat, bool atWar,
            float interiorBase, float borderBase, float threatGain, float threatCap, float peaceCap)
        {
            float baseFactor = isBorder ? borderBase : interiorBase;
            float threat = activeThreat < 0f ? 0f : Math.Min(activeThreat, Math.Max(0f, threatCap));

            float factor = baseFactor * (1f + threatGain * threat);

            if (!atWar && factor > peaceCap)
            {
                factor = peaceCap;
            }
            return factor < 0f ? 0f : factor;
        }

        /// <summary>
        /// Final multiplier applied to vanilla's garrison numbers: exposure, further
        /// raised for chokepoints so the gates of a realm are held hardest.
        /// </summary>
        public static float GarrisonMultiplier(float threatFactor, float chokepointScore, float chokepointGain)
        {
            float score = chokepointScore < 0f ? 0f : (chokepointScore > 1f ? 1f : chokepointScore);
            float multiplier = threatFactor * (1f + chokepointGain * score);
            return multiplier < 0f ? 0f : multiplier;
        }

        /// <summary>
        /// Scales a vanilla troop count by the multiplier, keeping the result a sane
        /// non-negative integer. Used to leave more behind on an exposed border.
        /// </summary>
        public static int ScaleTroopCount(int vanillaCount, float multiplier)
        {
            if (vanillaCount <= 0)
            {
                return 0;
            }
            if (multiplier <= 0f)
            {
                return 0;
            }
            int scaled = (int)Math.Round(vanillaCount * multiplier, MidpointRounding.AwayFromZero);
            return scaled < 0 ? 0 : scaled;
        }

        /// <summary>
        /// Daily auto-recruitment allowance. Vanilla always returns 1, which cannot
        /// refill a frontier garrison between raids; threatened settlements are
        /// allowed more, quiet ones keep the vanilla rate.
        /// </summary>
        public static int RecruitmentCap(float multiplier, int vanillaCap, int maxCap)
        {
            if (vanillaCap < 0)
            {
                vanillaCap = 0;
            }
            if (maxCap < vanillaCap)
            {
                maxCap = vanillaCap;
            }
            if (multiplier <= 1f)
            {
                return vanillaCap;
            }

            // Each full multiple above parity buys one extra recruit per day.
            int extra = (int)Math.Floor(multiplier - 1f);
            int cap = vanillaCap + extra;
            return cap > maxCap ? maxCap : cap;
        }
    }
}
