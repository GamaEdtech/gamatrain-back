namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.UnitOfWork;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Reconciliation must not renew a subscription whose renewal invoice the gateway has not been paid for yet
    /// (2026-10-05: a still-pending payment was recorded as a $0 Paid renewal and granted a month).
    /// </summary>
    public class SubscriptionReconciliationTests
    {
        [Fact]
        public async Task UnpaidRenewalInvoiceSyncsNothing()
        {
            // Every dependency throws if touched: an unpaid invoice must return before any read or write.
            var service = new SubscriptionQuotaService(
                new Lazy<IUnitOfWorkProvider>(() => throw new InvalidOperationException("database touched")),
                new(() => throw new InvalidOperationException("http context touched")),
                new(() => throw new InvalidOperationException("localizer touched")),
                new(() => throw new InvalidOperationException("logger touched")),
                new(() => throw new InvalidOperationException("gateway touched")));

            var result = await service.SyncExpirationFromGatewayAsync(1, DateTimeOffset.UtcNow.AddMonths(1), "in_test", invoiceIsRenewal: true, invoiceIsPaid: false, invoiceAmountPaid: 0m);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.False(result.Data);
        }
    }
}
