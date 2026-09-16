namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using ClosedXML.Excel;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Payment;
    using GamaEdtech.Presentation.ViewModel.Payment;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class PaymentsController(Lazy<ILogger<PaymentsController>> logger, Lazy<IPaymentService> paymentService)
        : ApiControllerBase<PaymentsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<PaymentsListResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<PaymentsListResponseViewModel>>> GetPayments([NotNull, FromQuery] PaymentsListRequestViewModel request)
        {
            try
            {
                ISpecification<Payment>? specification = null;

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = new CreationDateBetweenSpecification<Payment>(request.StartDate, request.EndDate);
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

                if (request.Kind is not null)
                {
                    var spec = new KindEqualsSpecification(request.Kind);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var result = await paymentService.Value.GetPaymentsAsync(new ListRequestDto<Payment>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<PaymentsListResponseViewModel>>(new()
                {
                    Errors = result.Errors,
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new PaymentsListResponseViewModel
                        {
                            Id = t.Id,
                            CreationDate = t.CreationDate,
                            UserId = t.UserId,
                            Amount = t.Amount,
                            Comment = t.Comment,
                            Currency = t.Currency,
                            FirstName = t.FirstName,
                            LastName = t.LastName,
                            SourceWallet = t.SourceWallet,
                            Status = t.Status,
                            TransactionId = t.TransactionId,
                            VerifyDate = t.VerifyDate,
                            Gateway = t.Gateway,
                            Kind = t.Kind,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<PaymentsListResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("export"), Produces<ApiResponse<string>>()]
        public async Task<IActionResult> Export([NotNull, FromQuery] ExportPaymentsListRequestViewModel request)
        {
            try
            {
                ISpecification<Payment>? specification = null;

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = new CreationDateBetweenSpecification<Payment>(request.StartDate, request.EndDate);
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

                if (request.Kind is not null)
                {
                    var spec = new KindEqualsSpecification(request.Kind);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var result = await paymentService.Value.GetPaymentsAsync(new ListRequestDto<Payment>
                {
                    Specification = specification,
                });

                var data = result.Data.List?.Select(t => new
                {
                    t.Id,
                    t.UserId,
                    t.FirstName,
                    t.LastName,
                    t.City,
                    t.State,
                    t.Country,
                    t.Amount,
                    Currency = t.Currency.Name,
                    Status = t.Status.Name,
                    t.CreationDate,
                    t.VerifyDate,
                    t.SourceWallet,
                    t.Comment,
                    t.TransactionId,
                    Gateway = t.Gateway.Name,
                    Kind = t.Kind?.Name,
                });

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("DataSheet");

                // Insert the list into the worksheet starting at cell A1
                // It automatically handles headers based on object properties
                _ = worksheet.Cell(1, 1).InsertTable(data);

                // Adjust column widths automatically
                _ = worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                return new FileContentResult(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                {
                    FileDownloadName = "Payments.xlsx",
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<string>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
