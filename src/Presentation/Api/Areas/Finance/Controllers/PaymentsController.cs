namespace GamaEdtech.Presentation.Api.Areas.Finance.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Payment;
    using GamaEdtech.Presentation.ViewModel.Payment;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Finance), "Finance")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Finance)])]
    public class PaymentsController(Lazy<ILogger<PaymentsController>> logger, Lazy<IPaymentService> paymentService)
        : ApiControllerBase<PaymentsController>(logger)
    {
        [HttpGet("summary"), Produces<ApiResponse<IEnumerable<PaymentsSummaryResponseViewModel>>>()]
        public async Task<IActionResult<IEnumerable<PaymentsSummaryResponseViewModel>>> GetPaymentsSummary([NotNull, FromQuery] PaymentsSummaryRequestViewModel request)
        {
            try
            {
                ISpecification<Payment>? specification = null;

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = new CreationDateBetweenSpecification(request.StartDate, request.EndDate);
                }

                if (request.UserId.HasValue)
                {
                    var spec = new UserIdEqualsSpecification<Payment, long>(request.UserId.Value);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.Gateway is not null)
                {
                    var spec = new GatewayEqualsSpecification(request.Gateway);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.Status is not null)
                {
                    var spec = new StatusEqualsSpecification(request.Status);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.Currency is not null)
                {
                    var spec = new CurrencyEqualsSpecification(request.Currency);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.Kind is not null)
                {
                    var spec = new KindEqualsSpecification(request.Kind);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var lst = await paymentService.Value.GetPaymentsSummaryAsync(specification);
                if (lst.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok<IEnumerable<PaymentsSummaryResponseViewModel>>(new() { Errors = lst.Errors });
                }

                var start = request.StartDate.HasValue ? request.StartDate.Value.ToDateTime(TimeOnly.MinValue) : lst.Data![0].Date;
                var end = request.EndDate.HasValue ? request.EndDate.Value.ToDateTime(TimeOnly.MinValue) : lst.Data![^1].Date;

                // Grouped server-side by (Date, Status, Kind) together (GetPaymentsSummaryAsync), so each date
                // can have several rows here - one per Status/Kind combination actually present, not one per
                // date. The Status and Kind pivots below are two independent sums over that same per-date slice,
                // each ignoring the other dimension - not a single Find() per bucket like before Kind existed.
                List<PaymentsSummaryResponseViewModel> result = [];
                while (start <= end)
                {
                    var dayRows = lst.Data!.Where(t => t.Date == start).ToList();

                    result.Add(new()
                    {
                        Date = DateOnly.FromDateTime(start),
                        FailedAmount = dayRows.Where(t => t.Status == PaymentStatus.Failed).Sum(t => t.Amount),
                        FailedCount = dayRows.Where(t => t.Status == PaymentStatus.Failed).Sum(t => t.Count),
                        PaidAmount = dayRows.Where(t => t.Status == PaymentStatus.Paid).Sum(t => t.Amount),
                        PaidCount = dayRows.Where(t => t.Status == PaymentStatus.Paid).Sum(t => t.Count),
                        PendingAmount = dayRows.Where(t => t.Status == PaymentStatus.Pending).Sum(t => t.Amount),
                        PendingCount = dayRows.Where(t => t.Status == PaymentStatus.Pending).Sum(t => t.Count),
                        NewSubscriptionAmount = dayRows.Where(t => t.Kind == PaymentKind.NewSubscription).Sum(t => t.Amount),
                        NewSubscriptionCount = dayRows.Where(t => t.Kind == PaymentKind.NewSubscription).Sum(t => t.Count),
                        RenewalAmount = dayRows.Where(t => t.Kind == PaymentKind.Renewal).Sum(t => t.Amount),
                        RenewalCount = dayRows.Where(t => t.Kind == PaymentKind.Renewal).Sum(t => t.Count),
                        PlanSwitchAmount = dayRows.Where(t => t.Kind == PaymentKind.PlanSwitch).Sum(t => t.Amount),
                        PlanSwitchCount = dayRows.Where(t => t.Kind == PaymentKind.PlanSwitch).Sum(t => t.Count),
                        PointsTopUpAmount = dayRows.Where(t => t.Kind == PaymentKind.PointsTopUp).Sum(t => t.Amount),
                        PointsTopUpCount = dayRows.Where(t => t.Kind == PaymentKind.PointsTopUp).Sum(t => t.Count),
                    });

                    start = start.AddDays(1);
                }

                return Ok<IEnumerable<PaymentsSummaryResponseViewModel>>(new()
                {
                    Data = result,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<PaymentsSummaryResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
