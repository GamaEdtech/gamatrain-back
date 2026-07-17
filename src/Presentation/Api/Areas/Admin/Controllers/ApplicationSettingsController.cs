namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.ApplicationSettings;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [Display(Name = "Application Settings")]
    public class ApplicationSettingsController(Lazy<ILogger<ApplicationSettingsController>> logger, Lazy<IApplicationSettingsService> applicationSettingsService)
        : ApiControllerBase<ApplicationSettingsController>(logger)
    {
        [HttpGet, Produces(typeof(ApiResponse<ApplicationSettingsViewModel>))]
        [Display(Name = "Application Settings List")]
        public async Task<IActionResult<ApplicationSettingsViewModel>> GetSettings()
        {
            try
            {
                var result = await applicationSettingsService.Value.GetApplicationSettingsAsync();
                return Ok<ApplicationSettingsViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        GridPageSize = result.Data.GridPageSize,
                        DefaultTimeZoneId = result.Data.DefaultTimeZoneId,
                        SchoolContributionPoints = result.Data.SchoolContributionPoints,
                        SchoolImageContributionPoints = result.Data.SchoolImageContributionPoints,
                        SchoolCommentContributionPoints = result.Data.SchoolCommentContributionPoints,
                        PostContributionPoints = result.Data.PostContributionPoints,
                        SchoolIssuesContributionPoints = result.Data.SchoolIssuesContributionPoints,
                        RemoveSchoolImageContributionPoints = result.Data.RemoveSchoolImageContributionPoints,
                        EasterEggBronzePoints = result.Data.EasterEggBronzePoints,
                        EasterEggSilverPoints = result.Data.EasterEggSilverPoints,
                        EasterEggGoldPoints = result.Data.EasterEggGoldPoints,
                        TestTimeCorrectSubmissionPoints = result.Data.TestTimeCorrectSubmissionPoints,
                        TestTimeIncorrectSubmissionPoints = result.Data.TestTimeIncorrectSubmissionPoints,
                        ExamCorrectTestSubmissionPoints = result.Data.ExamCorrectTestSubmissionPoints,
                        ExamIncorrectTestSubmissionPoints = result.Data.ExamIncorrectTestSubmissionPoints,
                        TicketConfirmationEmailTemplate = result.Data.TicketConfirmationEmailTemplate,
                        SchoolCommentContributionConfirmationEmailTemplate = result.Data.SchoolCommentContributionConfirmationEmailTemplate,
                        PostCommentContributionConfirmationEmailTemplate = result.Data.PostCommentContributionConfirmationEmailTemplate,
                        SchoolImageContributionConfirmationEmailTemplate = result.Data.SchoolImageContributionConfirmationEmailTemplate,
                        RemoveSchoolImageContributionConfirmationEmailTemplate = result.Data.RemoveSchoolImageContributionConfirmationEmailTemplate,
                        SchoolContributionConfirmationEmailTemplate = result.Data.SchoolContributionConfirmationEmailTemplate,
                        SchoolIssuesContributionConfirmationEmailTemplate = result.Data.SchoolIssuesContributionConfirmationEmailTemplate,
                        PostContributionConfirmationEmailTemplate = result.Data.PostContributionConfirmationEmailTemplate,
                        RegistrationEmailTemplate = result.Data.RegistrationEmailTemplate,
                        SchoolContributionRejectionEmailTemplate = result.Data.SchoolContributionRejectionEmailTemplate,
                        SchoolImageContributionRejectionEmailTemplate = result.Data.SchoolImageContributionRejectionEmailTemplate,
                        InitializeDeletingAccountEmailTemplate = result.Data.InitializeDeletingAccountEmailTemplate,
                        StartDeletingAccountEmailTemplate = result.Data.StartDeletingAccountEmailTemplate,
                        FinishedDeletingAccountEmailTemplate = result.Data.FinishedDeletingAccountEmailTemplate,
                        AdminTransactionCreationEmailTemplate = result.Data.AdminTransactionCreationEmailTemplate,
                        ContentOwnerCommissionPercent = result.Data.ContentOwnerCommissionPercent,
                        ContentOwnerCommissionPayoutThresholdUsd = result.Data.ContentOwnerCommissionPayoutThresholdUsd,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<ApplicationSettingsViewModel> { Errors = new[] { new Error { Message = exc.Message } } });
            }
        }

        [HttpPut, Produces(typeof(ApiResponse<bool>))]
        [Display(Name = "Edit Application Settings")]
        public async Task<IActionResult<bool>> UpdateSettings([NotNull] ApplicationSettingsViewModel request)
        {
            try
            {
                var result = await applicationSettingsService.Value.ModifyApplicationSettingsAsync(new()
                {
                    GridPageSize = request.GridPageSize.GetValueOrDefault(),
                    DefaultTimeZoneId = request.DefaultTimeZoneId,
                    SchoolContributionPoints = request.SchoolContributionPoints.GetValueOrDefault(),
                    SchoolImageContributionPoints = request.SchoolImageContributionPoints.GetValueOrDefault(),
                    SchoolCommentContributionPoints = request.SchoolCommentContributionPoints.GetValueOrDefault(),
                    PostContributionPoints = request.PostContributionPoints.GetValueOrDefault(),
                    SchoolIssuesContributionPoints = request.SchoolIssuesContributionPoints.GetValueOrDefault(),
                    RemoveSchoolImageContributionPoints = request.RemoveSchoolImageContributionPoints.GetValueOrDefault(),
                    EasterEggBronzePoints = request.EasterEggBronzePoints.GetValueOrDefault(),
                    EasterEggSilverPoints = request.EasterEggSilverPoints.GetValueOrDefault(),
                    EasterEggGoldPoints = request.EasterEggGoldPoints.GetValueOrDefault(),
                    TestTimeCorrectSubmissionPoints = request.TestTimeCorrectSubmissionPoints.GetValueOrDefault(),
                    TestTimeIncorrectSubmissionPoints = request.TestTimeIncorrectSubmissionPoints.GetValueOrDefault(),
                    ExamCorrectTestSubmissionPoints = request.ExamCorrectTestSubmissionPoints.GetValueOrDefault(),
                    ExamIncorrectTestSubmissionPoints = request.ExamIncorrectTestSubmissionPoints.GetValueOrDefault(),
                    TicketConfirmationEmailTemplate = request.TicketConfirmationEmailTemplate,
                    SchoolCommentContributionConfirmationEmailTemplate = request.SchoolCommentContributionConfirmationEmailTemplate,
                    PostCommentContributionConfirmationEmailTemplate = request.PostCommentContributionConfirmationEmailTemplate,
                    SchoolImageContributionConfirmationEmailTemplate = request.SchoolImageContributionConfirmationEmailTemplate,
                    RemoveSchoolImageContributionConfirmationEmailTemplate = request.RemoveSchoolImageContributionConfirmationEmailTemplate,
                    SchoolContributionConfirmationEmailTemplate = request.SchoolContributionConfirmationEmailTemplate,
                    SchoolIssuesContributionConfirmationEmailTemplate = request.SchoolIssuesContributionConfirmationEmailTemplate,
                    PostContributionConfirmationEmailTemplate = request.PostContributionConfirmationEmailTemplate,
                    RegistrationEmailTemplate = request.RegistrationEmailTemplate,
                    SchoolContributionRejectionEmailTemplate = request.SchoolContributionRejectionEmailTemplate,
                    SchoolImageContributionRejectionEmailTemplate = request.SchoolImageContributionRejectionEmailTemplate,
                    InitializeDeletingAccountEmailTemplate = request.InitializeDeletingAccountEmailTemplate,
                    StartDeletingAccountEmailTemplate = request.StartDeletingAccountEmailTemplate,
                    FinishedDeletingAccountEmailTemplate = request.FinishedDeletingAccountEmailTemplate,
                    AdminTransactionCreationEmailTemplate = request.AdminTransactionCreationEmailTemplate,
                    ContentOwnerCommissionPercent = request.ContentOwnerCommissionPercent.GetValueOrDefault(),
                    ContentOwnerCommissionPayoutThresholdUsd = request.ContentOwnerCommissionPayoutThresholdUsd.GetValueOrDefault(),
                });
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
