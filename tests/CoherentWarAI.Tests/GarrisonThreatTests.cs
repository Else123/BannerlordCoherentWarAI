using System.Collections.Generic;
using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    /// <summary>
    /// The garrison threat measure: hostile lord strength near a settlement,
    /// relative to what defends it. Replaces vanilla's NearbyLandThreatIntensity,
    /// which has no fixed scale and kept the gateway multiplier pinned at its cap.
    /// </summary>
    public class GarrisonThreatTests
    {
        private const float Radius = 30f;
        private const float Defenders = 300f;
        private const float AllyWeight = 0.25f;
        private const float Cap = GarrisonPlanner.DefaultThreatCap;

        private static NearbyForce Lord(float strength, float distance, ForceStance stance) =>
            new NearbyForce(strength, distance, Radius, true, false, stance);

        private static NearbyForce NonLord(float strength, float distance, ForceStance stance) =>
            new NearbyForce(strength, distance, Radius, false, false, stance);

        private static float Threat(params NearbyForce[] forces) =>
            GarrisonPlanner.ActiveThreat(forces, Defenders, AllyWeight, Cap);

        // A contested border gateway with the gate score from the CW run (Qasira 0.43).
        private static float GatewayMultiplier(float activeThreat) =>
            GarrisonPlanner.GarrisonMultiplier(
                GarrisonPlanner.ThreatFactor(true, activeThreat, true,
                    GarrisonPlanner.DefaultInteriorBase, GarrisonPlanner.DefaultBorderBase,
                    GarrisonPlanner.DefaultThreatGain, Cap, GarrisonPlanner.DefaultPeaceCap),
                0.43f, GarrisonPlanner.DefaultChokepointGain);

        [Fact]
        public void BanditsAndAlarmedCaravansNearby_DoNotPinTheGatewayMultiplierAtItsMaximum()
        {
            // The CW finding: banditry and the owner's own alarmed caravans and
            // villagers were all vanilla had to add up, yet Qasira sat at x2.72.
            float threat = Threat(
                NonLord(250f, 4f, ForceStance.Hostile),   // looter band at the gate
                NonLord(120f, 12f, ForceStance.Hostile),  // second bandit party
                NonLord(60f, 3f, ForceStance.Friendly),   // alarmed caravan
                NonLord(60f, 8f, ForceStance.Friendly),   // alarmed caravan
                NonLord(20f, 5f, ForceStance.Friendly));  // villagers

            Assert.Equal(0f, threat);
            Assert.True(GatewayMultiplier(threat) < GatewayMultiplier(Cap) - 0.5f,
                $"x{GatewayMultiplier(threat):F2} should stay well below the cap x{GatewayMultiplier(Cap):F2}");
        }

        [Fact]
        public void AnApproachingEnemyArmy_RaisesTheThreatAsItClosesIn()
        {
            // An army counts once, as a whole: its leader carries the army strength,
            // the attached members must not be added again.
            NearbyForce[] Army(float distance) => new[]
            {
                Lord(900f, distance, ForceStance.Hostile),
                new NearbyForce(150f, distance, Radius, true, true, ForceStance.Hostile),
                new NearbyForce(150f, distance, Radius, true, true, ForceStance.Hostile),
            };

            float far = GarrisonPlanner.ActiveThreat(Army(24f), Defenders, AllyWeight, Cap);
            float near = GarrisonPlanner.ActiveThreat(Army(6f), Defenders, AllyWeight, Cap);

            Assert.True(far > 0f, "an army inside the radius must register");
            Assert.True(near > far, $"closing in ({near}) must outweigh the distant army ({far})");
            Assert.Equal(900f * 0.2f / Defenders, far, 4);
            Assert.Equal(900f * 0.8f / Defenders, near, 4);
        }

        [Theory]
        [InlineData(100f, 0.3333f)]  // a lone lord party at the gate -> modest
        [InlineData(300f, 1f)]       // parity
        [InlineData(1200f, 4f)]      // fourfold overmatch reaches the cap
        [InlineData(3000f, 4f)]      // and never exceeds it
        public void ThreatIsEnemyStrengthRelativeToDefenders_CappedOnlyByRealOvermatch(float enemy, float expected)
        {
            Assert.Equal(expected, Threat(Lord(enemy, 0f, ForceStance.Hostile)), 4);
        }

        [Fact]
        public void AnUndefendedSettlement_IsMeasuredAgainstAMinimumDefence_WithoutAStepAtTheEdge()
        {
            float minDefence = GarrisonPlanner.MinDefenceStrength;

            // An enemy just inside the radius barely registers, even with no defenders.
            NearbyForce[] edge = { Lord(150f, Radius * 0.99f, ForceStance.Hostile) };
            float atEdge = GarrisonPlanner.ActiveThreat(edge, 0f, AllyWeight, Cap);
            Assert.True(atEdge < 0.1f, $"an enemy at the radius edge must not pin the threat ({atEdge})");

            // A strong enemy at the gate of an empty fief is still the full overmatch.
            NearbyForce[] gate = { Lord(minDefence * Cap, 0f, ForceStance.Hostile) };
            Assert.Equal(Cap, GarrisonPlanner.ActiveThreat(gate, 0f, AllyWeight, Cap), 4);
            Assert.Equal(Cap, GarrisonPlanner.ActiveThreat(gate, -5f, AllyWeight, Cap), 4);

            // A small party at the gate of an empty fief is measured, not capped.
            NearbyForce[] small = { Lord(minDefence, 0f, ForceStance.Hostile) };
            Assert.Equal(1f, GarrisonPlanner.ActiveThreat(small, 0f, AllyWeight, Cap), 4);

            Assert.Equal(0f, GarrisonPlanner.ActiveThreat(new NearbyForce[0], 0f, AllyWeight, Cap));
        }

        [Fact]
        public void TheRequestingLordAndHisArmy_DoNotCountAsDefendersOfTheGarrisonTheyAskAbout()
        {
            // The lord deciding how many troops to take sits at the gate; he must
            // not talk the threat down with his own presence.
            float threat = Threat(
                Lord(600f, 0f, ForceStance.Hostile),
                new NearbyForce(400f, 0f, Radius, true, false, ForceStance.Friendly, belongsToRequester: true));

            Assert.Equal(2f, threat, 4);
        }

        [Fact]
        public void AFriendlyArmyMember_IsCountedOnlyThroughItsLeader()
        {
            float threat = Threat(
                Lord(600f, 0f, ForceStance.Hostile),
                Lord(400f, 0f, ForceStance.Friendly),
                new NearbyForce(400f, 0f, Radius, true, true, ForceStance.Friendly));

            Assert.Equal(600f / (Defenders + AllyWeight * 400f), threat, 4);
        }

        [Fact]
        public void ForcesWithoutStrength_AreIgnored()
        {
            Assert.Equal(0f, Threat(Lord(0f, 0f, ForceStance.Hostile), Lord(-50f, 0f, ForceStance.Hostile)));
            Assert.Equal(2f, Threat(Lord(600f, 0f, ForceStance.Hostile), Lord(-400f, 0f, ForceStance.Friendly)), 4);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void ANonPositiveCap_DisablesTheThreat(float cap)
        {
            NearbyForce[] enemy = { Lord(1200f, 0f, ForceStance.Hostile) };
            Assert.Equal(0f, GarrisonPlanner.ActiveThreat(enemy, Defenders, AllyWeight, cap));
            Assert.Equal(0f, GarrisonPlanner.ActiveThreat(enemy, 0f, AllyWeight, cap));
        }

        [Fact]
        public void FriendlyLordsNearby_ShoulderPartOfTheDefence()
        {
            float alone = Threat(Lord(600f, 0f, ForceStance.Hostile));
            float covered = Threat(Lord(600f, 0f, ForceStance.Hostile), Lord(400f, 0f, ForceStance.Friendly));

            Assert.Equal(2f, alone, 4);
            Assert.Equal(600f / (Defenders + AllyWeight * 400f), covered, 4);
        }

        [Fact]
        public void NeutralLordsAndForcesOutsideTheRadius_AreIgnored()
        {
            Assert.Equal(0f, Threat(
                Lord(900f, 0f, ForceStance.Neutral),
                Lord(900f, Radius + 1f, ForceStance.Hostile)));
        }

        [Fact]
        public void LandAndSeaForces_EachFadeOverTheirOwnSearchRadius()
        {
            var forces = new List<NearbyForce>
            {
                new NearbyForce(300f, 15f, 30f, true, false, ForceStance.Hostile), // land, half way
                new NearbyForce(300f, 15f, 60f, true, false, ForceStance.Hostile), // fleet, a quarter in
            };

            Assert.Equal((150f + 225f) / Defenders, GarrisonPlanner.ActiveThreat(forces, Defenders, AllyWeight, Cap), 4);
        }

        [Theory]
        [InlineData(0f, 30f, 1f)]
        [InlineData(-1f, 30f, 1f)]
        [InlineData(15f, 30f, 0.5f)]
        [InlineData(30f, 30f, 0f)]
        [InlineData(45f, 30f, 0f)]
        [InlineData(5f, 0f, 0f)]     // degenerate search radius -> nothing counts
        public void ProximityWeight_FallsOffLinearlyToTheEdgeOfTheSearch(float distance, float radius, float expected)
        {
            Assert.Equal(expected, GarrisonPlanner.ProximityWeight(distance, radius), 4);
        }
    }
}
