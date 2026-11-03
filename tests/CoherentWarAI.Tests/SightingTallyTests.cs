using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    // Defect (log analysis 2026-11-03): every observing lord added a fresh sighting
    // every hour and the readers summed all of them, so one army watched by three lords
    // for a day and a half was believed to be 108 armies. The tally counts each force
    // once, by the latest report about it.
    public class SightingTallyTests
    {
        [Fact]
        public void OneArmyWatchedByThreeLordsForADayAndAHalf_CountsOnce()
        {
            var tally = new SightingTally<string>();
            const float strength = 500f;
            const float confidence = 0.8f;

            for (int hour = 0; hour < 36; hour++)
            {
                for (int observer = 0; observer < 3; observer++)
                {
                    tally.Offer("army", hour, strength * confidence);
                }
            }

            Assert.Equal(strength * confidence, tally.Total, 3);
        }

        [Fact]
        public void DifferentForces_AddUp()
        {
            var tally = new SightingTally<string>();

            tally.Offer("north host", 0f, 300f);
            tally.Offer("south host", 5f, 200f);

            Assert.Equal(500f, tally.Total, 3);
        }

        [Fact]
        public void LatestReportSupersedesOlderOnes()
        {
            // Seen tied down five hours ago, seen free an hour ago: the older word is
            // no longer what the realm believes.
            var tally = new SightingTally<string>();

            tally.Offer("army", 5f, 400f);
            tally.Offer("army", 1f, 0f);

            Assert.Equal(0f, tally.Total, 3);
        }

        [Fact]
        public void LatestReportWins_WhateverOrderReportsAreOffered()
        {
            var tally = new SightingTally<string>();

            tally.Offer("army", 1f, 0f);
            tally.Offer("army", 5f, 400f);

            Assert.Equal(0f, tally.Total, 3);
        }

        [Fact]
        public void ReportsFromTheSameHour_TheMostConfidentOneIsKept()
        {
            // Several lords seeing the same army at the same moment: the best scout's
            // account stands, the others add nothing.
            var tally = new SightingTally<string>();

            tally.Offer("army", 2f, 150f);
            tally.Offer("army", 2f, 300f);
            tally.Offer("army", 2f, 100f);

            Assert.Equal(300f, tally.Total, 3);
        }

        [Fact]
        public void Clear_ForgetsEverything()
        {
            var tally = new SightingTally<string>();
            tally.Offer("army", 0f, 300f);

            tally.Clear();

            Assert.Equal(0f, tally.Total, 3);
        }

        [Fact]
        public void Empty_TotalsZero()
        {
            Assert.Equal(0f, new SightingTally<string>().Total, 3);
        }
    }
}
