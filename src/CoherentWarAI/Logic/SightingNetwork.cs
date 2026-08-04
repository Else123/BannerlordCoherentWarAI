using System;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Engine-free rules for word of an enemy force travelling between lords.
    ///
    /// Vanilla AI is omniscient: every lord scores every target from perfect
    /// knowledge of the whole map, which is why a realm can react to a threat
    /// nobody has seen and why its responses feel oddly synchronised. Here a
    /// sighting starts where someone actually saw it and spreads outward at the
    /// speed a rider could carry it, so a realm learns of an army the way a
    /// medieval one would - late, and nearest first.
    /// </summary>
    public static class SightingNetwork
    {
        /// <summary>How far word travels in an hour, as a fraction of the usual gap between towns.</summary>
        public const float DefaultRelaySpeed = 0.5f;

        /// <summary>How long a sighting is worth acting on before it is simply old news.</summary>
        public const float DefaultSightingLifetimeHours = 36f;

        /// <summary>Distance within which a party notices an enemy force itself.</summary>
        public const float DefaultSpotRadiusFactor = 0.75f;

        /// <summary>
        /// How far word of a sighting has spread by now. Word does not appear
        /// everywhere at once; it moves outward from where it started.
        /// </summary>
        public static float SpreadRadius(float hoursSinceSighted, float relaySpeed, float distanceUnit)
        {
            if (hoursSinceSighted <= 0f || relaySpeed <= 0f || distanceUnit <= 0f)
            {
                return 0f;
            }
            return hoursSinceSighted * relaySpeed * distanceUnit;
        }

        /// <summary>
        /// Whether a lord this far from where the enemy was seen has heard about it
        /// yet. Distance is measured from the sighting, so news reaches neighbours
        /// before it reaches the far side of the realm.
        /// </summary>
        public static bool HasReached(float distanceFromSighting, float hoursSinceSighted,
            float relaySpeed, float distanceUnit)
        {
            if (distanceFromSighting <= 0f)
            {
                return true;
            }
            return distanceFromSighting <= SpreadRadius(hoursSinceSighted, relaySpeed, distanceUnit);
        }

        /// <summary>
        /// What a sighting is still worth acting on, 0..1. Fades with age: an army
        /// reported a day and a half ago could be anywhere, so acting on it is
        /// guesswork rather than intelligence.
        /// </summary>
        public static float Freshness(float hoursSinceSighted, float lifetimeHours)
        {
            if (lifetimeHours <= 0f)
            {
                return 0f;
            }
            if (hoursSinceSighted <= 0f)
            {
                return 1f;
            }
            if (hoursSinceSighted >= lifetimeHours)
            {
                return 0f;
            }
            return 1f - hoursSinceSighted / lifetimeHours;
        }

        /// <summary>
        /// How alarming a sighting is for a particular place: how big the force was,
        /// weighed against how far it would have to come, and faded by age.
        ///
        /// Returns 0 for anything too old or too distant to be worth moving for -
        /// the point is to concentrate defenders where a threat actually is, not to
        /// have every lord chase every rumour.
        /// </summary>
        /// <param name="enemyStrength">Strength of the force that was seen.</param>
        /// <param name="distanceToThreatened">Distance from the sighting to the place at risk.</param>
        /// <param name="reachDistance">How far that force could plausibly travel while the news is current.</param>
        public static float ThreatToPlace(float enemyStrength, float distanceToThreatened,
            float reachDistance, float hoursSinceSighted, float lifetimeHours)
        {
            if (enemyStrength <= 0f || reachDistance <= 0f)
            {
                return 0f;
            }

            float freshness = Freshness(hoursSinceSighted, lifetimeHours);
            if (freshness <= 0f)
            {
                return 0f;
            }

            if (distanceToThreatened >= reachDistance)
            {
                return 0f;
            }

            // Nearer means more urgent, linearly - a force half the reach away is
            // half as pressing as one at the gates.
            float proximity = 1f - Math.Max(0f, distanceToThreatened) / reachDistance;
            return enemyStrength * proximity * freshness;
        }

        /// <summary>How far a target's score falls when nothing is known about it.</summary>
        public const float DefaultUnknownPenalty = 0.55f;

        /// <summary>
        /// How confidently a realm can act against a place, given when it last had
        /// eyes on it.
        ///
        /// This is what stops the AI from behaving omniscient in its target choice.
        /// It cannot be stopped from *seeing* everything - vanilla's own loops read
        /// the world directly and are not ours to change - but it can be stopped
        /// from acting decisively on knowledge it never gathered. A lord marches on
        /// what he has been told about, and a castle nobody has looked at in a month
        /// is a rumour rather than a plan.
        ///
        /// Bordering our own land counts as known: a frontier is watched
        /// continuously by the people living along it, without anyone being sent.
        /// </summary>
        /// <param name="bordersOurLand">Whether the place adjoins territory of ours.</param>
        /// <param name="hoursSinceObserved">Time since a party of ours was last in sight of it; negative if never.</param>
        public static float KnowledgeWeight(bool bordersOurLand, float hoursSinceObserved,
            float lifetimeHours, float unknownPenalty)
        {
            if (bordersOurLand)
            {
                return 1f;
            }

            float penalty = unknownPenalty < 0f ? 0f : (unknownPenalty > 1f ? 1f : unknownPenalty);
            float floor = 1f - penalty;

            if (hoursSinceObserved < 0f)
            {
                return floor;
            }

            // Knowledge decays the same way a report does: what was seen a while ago
            // is somewhere between certainty and hearsay.
            float freshness = Freshness(hoursSinceObserved, lifetimeHours);
            return floor + penalty * freshness;
        }

        /// <summary>
        /// Turns accumulated reported threat into a defensive weighting for a
        /// settlement, relative to what a realm normally faces.
        ///
        /// Deliberately bounded: reported threat should shift where defenders go,
        /// not overwhelm every other reason they might be somewhere.
        /// </summary>
        public static float DefensiveUrgency(float reportedThreat, float typicalThreat, float maxBoost)
        {
            if (reportedThreat <= 0f || typicalThreat <= 0f)
            {
                return 1f;
            }

            float ratio = reportedThreat / typicalThreat;
            if (ratio <= 1f)
            {
                return 1f;
            }

            float boost = Math.Max(0f, maxBoost);
            // Saturating, so one enormous host does not make everywhere else
            // irrelevant.
            return 1f + boost * (1f - 1f / ratio);
        }
    }
}
