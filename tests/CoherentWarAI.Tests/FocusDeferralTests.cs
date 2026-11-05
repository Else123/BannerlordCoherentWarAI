using CoherentWarAI.Logic;
using Xunit;

namespace CoherentWarAI.Tests
{
    public class FocusDeferralTests
    {
        [Fact]
        public void HasMeasurableEnemy_AllZero_IsFalse()
        {
            Assert.False(FocusDeferral.HasMeasurableEnemy(new[] { 0f, 0f }));
        }

        [Fact]
        public void HasMeasurableEnemy_OneAboveZero_IsTrue()
        {
            Assert.True(FocusDeferral.HasMeasurableEnemy(new[] { 0f, 120f }));
        }

        [Fact]
        public void HasMeasurableEnemy_NullOrEmpty_IsFalse()
        {
            Assert.False(FocusDeferral.HasMeasurableEnemy(null));
            Assert.False(FocusDeferral.HasMeasurableEnemy(new float[0]));
        }

        [Fact]
        public void Update_ReportsOnlyTransitions_SoAStrengthlessWarLogsOnce()
        {
            var deferral = new FocusDeferral<string>();

            Assert.True(deferral.Update("A", deferred: true));
            Assert.False(deferral.Update("A", deferred: true));
            Assert.False(deferral.Update("A", deferred: true));
            Assert.True(deferral.Update("A", deferred: false));
            Assert.False(deferral.Update("A", deferred: false));
        }

        [Fact]
        public void Realms_AreDeferredIndependently()
        {
            var deferral = new FocusDeferral<string>();
            deferral.Update("A", true);
            deferral.Update("B", true);

            deferral.Update("A", false);

            Assert.False(deferral.IsDeferred("A"));
            Assert.True(deferral.IsDeferred("B"));
            Assert.Equal(new[] { "B" }, deferral.DeferredRealms());
        }

        [Fact]
        public void Clear_ForgetsEveryRealm()
        {
            var deferral = new FocusDeferral<string>();
            deferral.Update("A", true);

            deferral.Clear();

            Assert.False(deferral.IsDeferred("A"));
            Assert.Empty(deferral.DeferredRealms());
        }
    }
}
