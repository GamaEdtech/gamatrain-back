namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Data.Dto.Subscription;

    using Xunit;

    /// <summary>The user's usage summary counts uses net of refunds (a refunded download logs a charge and a refund).</summary>
    public class SubscriptionUsageSummaryTests
    {
        [Theory]
        [InlineData(3, 0, 3)]
        [InlineData(3, 1, 1)]
        [InlineData(2, 1, 0)]
        [InlineData(0, 0, 0)]
        [InlineData(1, 1, 0)]
        public void NetUseCountTakesEachRefundAndItsChargeOff(int eventCount, int refundCount, int expected)
        {
            var dto = new SubscriptionUsageAggregateDto { EventCount = eventCount, RefundCount = refundCount };

            Assert.Equal(expected, dto.NetUseCount);
        }
    }
}
