namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;
    using System.Text;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Data.Dto.ExamAnalysis;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.ExamAnalysis;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class ExamAnalysisService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamAnalysisService>> localizer, Lazy<ILogger<ExamAnalysisService>> logger,
        Lazy<IContentPurchaseService> contentPurchaseService, Lazy<ICoreProvider> coreProvider, Lazy<IAiService> aiService,
        Lazy<IApplicationSettingsService> applicationSettingsService)
        : LocalizableServiceBase<ExamAnalysisService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IExamAnalysisService
    {
        private const int MaxQuestions = 200;
        private const int MaxQuestionLength = 1_000;
        private const int MaxOptionLength = 200;
        private const int MaxResultLength = 20_000;
        private const int MaxTokens = 1000;
        private const double Temperature = 0.4;

        // The instructions the AI gets, as product wrote them: an embedded file (ExamAnalysisPrompt.md) so they read and
        // diff as plain text, like the support handbook. The exam data goes in the user prompt (BuildUserPrompt).
        private const string SystemPromptResourceName = "ExamAnalysisPrompt.md";
        private static readonly Lazy<string> SystemPrompt = new(ReadSystemPrompt);

        public async Task<ResultData<PrepareExamAnalysisResponseDto>> PrepareAsync([NotNull] PrepareExamAnalysisRequestDto requestDto)
        {
            ContentPurchaseRequestDto? purchase = null;
            ContentChargeResponseDto? charge = null;
            var purchaseCompleted = false;

            try
            {
                var existing = await GetAsync(requestDto.UserId, requestDto.ExamId);
                if (existing is not null)
                {
                    return Prepared(existing);
                }

                // Everything in the prompt (questions, options, correct and chosen answers, score, time, rank) comes from
                // gama-api's own result for this caller, never from the client.
                var examResult = await coreProvider.Value.GetExamResultDetailAsync(new ExamResultRequestDto
                {
                    ExamId = requestDto.ExamId,
                    SecretKey = requestDto.SecretKey,
                });
                if (examResult.OperationResult is not OperationResult.Succeeded || examResult.Data is null)
                {
                    return new(examResult.OperationResult is OperationResult.Succeeded ? OperationResult.Failed : examResult.OperationResult)
                    {
                        Errors = examResult.Errors,
                    };
                }

                if (examResult.Data.Questions.Count is 0 or > MaxQuestions)
                {
                    return new(OperationResult.NotValid)
                    {
                        Errors = [new() { Message = Localizer.Value["ExamAnalysisResultUnavailable"] },],
                    };
                }

                var settingsResult = await applicationSettingsService.Value.GetApplicationSettingsAsync();
                if (settingsResult.OperationResult is not OperationResult.Succeeded || settingsResult.Data is null)
                {
                    return new(settingsResult.OperationResult is OperationResult.Succeeded ? OperationResult.Failed : settingsResult.OperationResult)
                    {
                        Errors = settingsResult.Errors,
                    };
                }

                var points = Price(settingsResult.Data);
                purchase = CreatePurchase(requestDto, points);
                var chargeResult = await contentPurchaseService.Value.ChargeAsync(purchase);
                if (chargeResult.OperationResult is not OperationResult.Succeeded || chargeResult.Data is null)
                {
                    return new(chargeResult.OperationResult)
                    {
                        Errors = chargeResult.Errors,
                        Data = new() { Charge = chargeResult.Data?.Refused },
                    };
                }

                charge = chargeResult.Data;
                var completion = await aiService.Value.CompleteAsync(new ChatCompletionRequestDto
                {
                    SystemPrompt = SystemPrompt.Value,
                    UserPrompt = BuildUserPrompt(examResult.Data),
                    MaxTokens = MaxTokens,
                    Temperature = Temperature,
                });
                var generatedResult = completion.Data?.Content.SanitizePlainText()?.Trim();
                if (completion.OperationResult is not OperationResult.Succeeded || string.IsNullOrWhiteSpace(generatedResult))
                {
                    await RefundAsync(purchase, charge);
                    return new(OperationResult.Failed) { Errors = completion.Errors };
                }

                if (generatedResult.Length > MaxResultLength)
                {
                    generatedResult = generatedResult[..MaxResultLength];
                }

                await contentPurchaseService.Value.CompletePurchaseAsync(purchase, charge);
                purchaseCompleted = true;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var analysis = new ExamAnalysis
                {
                    UserId = requestDto.UserId,
                    ExamId = requestDto.ExamId,
                    Result = generatedResult,
                    Points = points,
                    CreationDate = DateTimeOffset.UtcNow,
                };
                uow.GetRepository<ExamAnalysis>().Add(analysis);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Analysis = ToDto(analysis),
                        Charge = charge.Spend,
                    },
                };
            }
            catch (UniqueConstraintException)
            {
                if (!purchaseCompleted)
                {
                    await RefundAsync(purchase, charge);
                }

                var existing = await GetAsync(requestDto.UserId, requestDto.ExamId);
                return existing is null
                    ? Failed<PrepareExamAnalysisResponseDto>()
                    : Prepared(existing);
            }
            catch (Exception exc)
            {
                if (!purchaseCompleted)
                {
                    await RefundAsync(purchase, charge);
                }

                Logger.Value.LogException(exc);
                return Failed<PrepareExamAnalysisResponseDto>();
            }
        }

        public async Task<ResultData<ExamAnalysisStatusDto>> GetStatusAsync(long userId, long examId)
        {
            try
            {
                var existing = await GetAsync(userId, examId);
                if (existing is not null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { Analysis = ToDto(existing), Purchased = true } };
                }

                var purchased = await contentPurchaseService.Value.GetPurchasedVariantsAsync(userId, PurchasableContentType.ExamAnalysis, examId);
                if (purchased.OperationResult is not OperationResult.Succeeded)
                {
                    return new(purchased.OperationResult) { Errors = purchased.Errors };
                }

                if (purchased.Data is { Count: > 0 })
                {
                    return new(OperationResult.Succeeded) { Data = new() { Purchased = true } };
                }

                var settings = await applicationSettingsService.Value.GetApplicationSettingsAsync();
                var failure = settings.OperationResult is OperationResult.Succeeded ? OperationResult.Failed : settings.OperationResult;
                return settings.OperationResult is not OperationResult.Succeeded || settings.Data is null
                    ? new(failure) { Errors = settings.Errors }
                    : new(OperationResult.Succeeded) { Data = new() { Points = Price(settings.Data) } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Failed<ExamAnalysisStatusDto>();
            }
        }

        public async Task<ResultData<ExamResultPageDto>> GetResultPageAsync(long userId, long examId, string secretKey)
        {
            try
            {
                // Both at once: the result is gama-api's, the analysis status only ours.
                var resultTask = coreProvider.Value.GetExamResultPageAsync(new() { ExamId = examId, SecretKey = secretKey });
                var statusTask = GetStatusAsync(userId, examId);
                var result = await resultTask;
                if (result.Data is null)
                {
                    return new(result.OperationResult is OperationResult.Succeeded ? OperationResult.Failed : result.OperationResult) { Errors = result.Errors };
                }

                // A failed status read (logged in GetStatusAsync) only hides the analysis part; the result still shows.
                var status = await statusTask;
                return new(OperationResult.Succeeded) { Data = new() { Result = result.Data, Analysis = status.Data } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Failed<ExamResultPageDto>();
            }
        }

        internal static string BuildUserPrompt(ExamResultDetailResponseDto result)
        {
            StringBuilder prompt = new();
            AppendLineIfAny(prompt, "Student's first name: ", PlainText(result.StudentFirstName, MaxOptionLength));
            _ = prompt.AppendLine("Exam (from Gamatrain):");
            AppendLineIfAny(prompt, "Title: ", PlainText(result.ExamTitle, MaxQuestionLength));
            AppendLineIfAny(prompt, "Time allowed (minutes): ", result.ExamTime);
            if (result.TimeTakenSeconds is > 0 and var seconds)
            {
                _ = prompt.Append("Time the student took: ").Append((seconds / 60).ToString(CultureInfo.InvariantCulture)).Append(" min ")
                    .Append((seconds % 60).ToString(CultureInfo.InvariantCulture)).AppendLine(" s");
            }

            _ = prompt.AppendLine()
                .AppendLine("Overall exam result (verified by the backend):")
                .Append("Total questions: ").AppendLine(result.Stats.Total.ToString(CultureInfo.InvariantCulture))
                .Append("Correct: ").AppendLine(result.Stats.Valid.ToString(CultureInfo.InvariantCulture))
                .Append("Incorrect: ").AppendLine(result.Stats.Invalid.ToString(CultureInfo.InvariantCulture))
                .Append("Unanswered: ").AppendLine(result.Stats.NoAnswer.ToString(CultureInfo.InvariantCulture))
                .Append("Score percentage: ").Append(result.Stats.Percent.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine("%");

            foreach (var rank in result.Ranks)
            {
                _ = prompt.Append("Rank among ").Append(rank.Scope == "total" ? "all" : $"the {rank.Scope}'s").Append(" participants: ")
                    .Append(rank.Position.ToString(CultureInfo.InvariantCulture));
                _ = rank.Participants is int participants
                    ? prompt.Append(" of ").AppendLine(participants.ToString(CultureInfo.InvariantCulture))
                    : prompt.AppendLine();
            }

            _ = prompt.AppendLine()
                .AppendLine("Questions and answers:");

            for (var i = 0; i < result.Questions.Count; i++)
            {
                var question = result.Questions[i];
                _ = prompt.AppendLine()
                    .Append("Question ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).AppendLine(":");
                AppendLineIfAny(prompt, "Lesson: ", PlainText(question.Lesson, MaxOptionLength));
                AppendLineIfAny(prompt, "Topics: ", PlainText(question.Topics, MaxOptionLength));
                _ = prompt.AppendLine(ContentText(question.Question, question.QuestionFile, MaxQuestionLength));

                if (IsDescriptive(question))
                {
                    _ = prompt.AppendLine("(Descriptive question, not auto-graded)");
                    continue;
                }

                _ = prompt.AppendLine("Choices:");
                for (var option = 0; option < question.Options.Count; option++)
                {
                    _ = prompt.Append((char)('A' + option)).Append(") ")
                        .AppendLine(ContentText(question.Options[option].Text, question.Options[option].File, MaxOptionLength));
                }

                _ = prompt.Append("Student answer: ").AppendLine(question.StudentOption?.ToString() ?? "Unanswered")
                    .Append("Correct answer: ").AppendLine(question.CorrectOption?.ToString() ?? "Not given")
                    .Append("Result: ").AppendLine(question switch
                    {
                        { StudentOption: null } => "No answer",
                        { CorrectOption: null } => "Not graded",
                        _ when question.StudentOption == question.CorrectOption => "Correct",
                        _ => "Wrong",
                    });
            }

            return prompt.ToString();
        }

        private async Task<ExamAnalysis?> GetAsync(long userId, long examId)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            return await uow.GetRepository<ExamAnalysis>().GetAsync(new UserIdAndExamIdEqualsSpecification(userId, examId), false);
        }

        private async Task RefundAsync(ContentPurchaseRequestDto? purchase, ContentChargeResponseDto? charge)
        {
            if (purchase is not null && charge is not null)
            {
                await contentPurchaseService.Value.RefundAsync(purchase, charge);
            }
        }

        private ResultData<T> Failed<T>() => new(OperationResult.Failed)
        {
            Errors = [new() { Message = Localizer.Value["OperationFailed"] },],
        };

        // gama-api's type when it sends one; otherwise a question with no option text or image at all is descriptive.
        private static bool IsDescriptive(ExamResultDetailResponseDto.QuestionDto question) => question.Type switch
        {
            "descriptive" => true,
            "fourchoice" => false,
            _ => question.Options.All(t => string.IsNullOrWhiteSpace(t.Text) && string.IsNullOrWhiteSpace(t.File)),
        };

        private static void AppendLineIfAny(StringBuilder prompt, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _ = prompt.Append(label).AppendLine(value);
            }
        }

        private static string ContentText(string? html, string? file, int maxLength)
        {
            var text = PlainText(html, maxLength);
            return string.IsNullOrEmpty(file) ? text ?? "(empty)" : $"{text} (image)".TrimStart();
        }

        // gama-api question text is HTML (may hold $...$ formulas): the prompt gets it as plain, single-spaced text.
        private static string? PlainText(string? html, int maxLength)
        {
            // A paragraph or line break becomes a space, or "rain.</p><p>Which" would read "rain.Which".
            var spaced = html?.Replace("</p>", " </p>", StringComparison.OrdinalIgnoreCase)
                .Replace("<br", " <br", StringComparison.OrdinalIgnoreCase)
                .Replace("</div>", " </div>", StringComparison.OrdinalIgnoreCase)
                .Replace("</li>", " </li>", StringComparison.OrdinalIgnoreCase);
            var text = WebUtility.HtmlDecode(spaced.SanitizePlainText());
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length > maxLength ? text[..maxLength] : text;
        }

        private static string ReadSystemPrompt()
        {
            using var stream = typeof(ExamAnalysisService).Assembly.GetManifestResourceStream(SystemPromptResourceName)
                ?? throw new InvalidOperationException($"Missing embedded resource {SystemPromptResourceName}.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        private static long Price(ApplicationSettingsDto settings) => Math.Max(settings.ExamAnalysisPoints ?? ApplicationSettingsDto.DefaultExamAnalysisPoints, 0);

        private static ContentPurchaseRequestDto CreatePurchase(PrepareExamAnalysisRequestDto requestDto, long points) => new()
        {
            UserId = requestDto.UserId,
            ContentType = PurchasableContentType.ExamAnalysis,
            ContentId = requestDto.ExamId,
            Points = points,
            SpendContentType = ContentType.ExamAnalysis,
        };

        private static ResultData<PrepareExamAnalysisResponseDto> Prepared(ExamAnalysis analysis) => new(OperationResult.Succeeded)
        {
            Data = new() { Analysis = ToDto(analysis) },
        };

        private static ExamAnalysisDto ToDto(ExamAnalysis analysis) => new()
        {
            Id = analysis.Id,
            ExamId = analysis.ExamId,
            Result = analysis.Result,
            Points = analysis.Points,
        };

    }
}
