namespace GamaEdtech.Infrastructure.Provider.Core
{
    using System;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using SkiaSharp;
    using SkiaSharp.QrCode;
    using SkiaSharp.QrCode.Image;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    public sealed partial class CoreProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<CoreProvider>> localizer
        , Lazy<ILogger<CoreProvider>> logger, Lazy<IHttpContextAccessor> httpContextAccessor)
        : InfrastructureBase<CoreProvider>(httpProvider, localizer, logger), ICoreProvider
    {
        /// <summary>The option kinds of the exam import, as gama-api's <c>types/list</c> type and parent filter.</summary>
        private static readonly Dictionary<string, (string Type, string? ParentFilter)> ExamOptionTypes = new(StringComparer.Ordinal)
        {
            ["board"] = ("section", null),
            ["grade"] = ("base", "section_id"),
            ["course"] = ("course", "section_id"),
            ["subject"] = ("lesson", "base_id"),
            ["topic"] = ("topic", "lesson_id"),
            ["paper"] = ("exam_type", null),
            ["classification"] = ("test_type", "section_id"),
        };

        private const string OptionLetters = "abcd";

        public async Task<ResultData<bool>> ValidateTestAsync([NotNull] TestTimeRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<CoreTestInformationResponse>, IHttpRequest>(new()
                {
                    Uri = string.Format(configuration.Value.GetValue<string>("Core:Test")!, requestDto.TestId),
                    Request = null,
                    HeaderParameters = GetHeaders(null),
                });
                if (response is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                if (response.Status != 1 || response.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["TestNotFound"], }] };
                }

                var valid = response.Data.AnswerId == requestDto.SubmissionId;
                return new(OperationResult.Succeeded)
                {
                    Data = valid,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<ExamResultResponseDto>> GetExamResultAsync([NotNull] ExamResultRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<CoreExamResultResponse>, IHttpRequest>(new()
                {
                    Uri = string.Format(configuration.Value.GetValue<string>("Core:ExamResult")!, requestDto.ExamId),
                    Request = null,
                    HeaderParameters = GetHeaders(requestDto.SecretKey),
                });
                if (response is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                if (response.Status != 1 || response.Data?.AnswerStats?.Total is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["ExamNotFound"], }] };
                }

                ExamResultResponseDto result = new()
                {
                    Invalid = response.Data.AnswerStats.Total.False,
                    Valid = response.Data.AnswerStats.Total.True,
                    NoAnswer = response.Data.AnswerStats.Total.NoAnswer,
                    Total = response.Data.AnswerStats.Total.Num,
                    Percent = response.Data.AnswerStats.Total.Percent,
                };
                return new(OperationResult.Succeeded)
                {
                    Data = result,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        /// <summary>
        /// Attempts per gama-api exam GET (see <see cref="GetWithRetryAsync{T}"/>): its endpoints intermittently
        /// answer "Target resource is no longer available at the origin server" and then succeed moments later.
        /// </summary>
        private const int ExamRequestAttempts = 3;

        /// <summary>gama-api sends <c>"0"</c> (not null) in its <c>q_file</c>/<c>a_file</c>... fields for "no
        /// image" -- normalized to <see langword="null"/> here so every export treats it as absent, instead of
        /// as an image to show (the Word export used to add an empty full-width image row for it).</summary>
        private static string? FileUrlOrNull(string? value) => string.IsNullOrWhiteSpace(value) || value == "0" ? null : value;

        /// <summary>
        /// Loads everything an exam export needs from two read-only gama-api calls: the exam's details and ordered
        /// question ids from <c>Core:Exam</c> (<c>exams/{id}</c>), then all its questions in full -- including the
        /// correct option, for the exports' Answer Key -- in one call to <c>Core:ExamTest</c>
        /// (<c>examTests?exam_id={id}</c>, since 2026-10-01; it used to be one <c>examTests?id=</c> call per
        /// question). The questions are put in <c>exams/{id}</c>'s order, and any id missing from that list fails
        /// the whole call: an export silently missing questions would be worse than none.
        /// </summary>
        public async Task<ResultData<ExamInformationResponseDto>> GetExamInformationAsync([NotNull] ExamInformationRequestDto requestDto)
        {
            try
            {
                var headers = GetHeaders(requestDto.SecretKey);
                var examResponse = await GetWithRetryAsync<CoreExamResponse>(
                    string.Format(configuration.Value.GetValue<string>("Core:Exam")!, requestDto.ExamId), headers);
                var exam = examResponse?.Data;
                if (exam is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = examResponse?.Message ?? Localizer.Value["GeneralError"], }] };
                }

                var testIds = exam.Tests ?? [];
                var testsResponse = await GetWithRetryAsync<CoreExamTestListResponse>(
                    string.Format(configuration.Value.GetValue<string>("Core:ExamTest")!, requestDto.ExamId), headers);
                var testsById = (testsResponse?.Data?.List ?? [])
                    .Where(t => t.Id is not null)
                    .DistinctBy(t => t.Id)
                    .ToDictionary(t => t.Id!);
                var testResponses = testIds.Select(id => testsById.GetValueOrDefault(id)).ToList();

                var failedCount = testResponses.Count(t => t is null);
                if (failedCount > 0)
                {
                    Logger.Value.LogError("Exam {ExamId}: {FailedCount} of {TestCount} questions could not be loaded", requestDto.ExamId, failedCount, testIds.Count);
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                var examDetailsUrl = string.Format(configuration.Value.GetValue<string>("Core:ExamDetailsUrl")!, exam.Code);

                // Background matches the Word/PowerPoint export's own header background (Shape 3/Gray
                // Diagonal Panel, #F2F4F7, see ExamWordDocumentBuilder) instead of the library's plain-white
                // default, so the QR code sits flush against that panel with no white square around it --
                // keep this in sync if that shape's fill color ever changes.
                var qr = new QRCodeImageBuilder(examDetailsUrl)
                    .WithErrorCorrection(ECCLevel.M)
                    .WithColors(SKColors.Black, SKColor.Parse("F2F4F7"))
                    .WithSize(512, 512)
                    .ToByteArray();

                ExamInformationResponseDto result = new()
                {
                    Exam = new()
                    {
                        Title = exam.Title,
                        TestsCount = exam.TestsCount.ValueOf<int>(),
                        StartDate = exam.StartDate,
                        EndDate = exam.EndDate,
                        ExamTime = exam.ExamTime,
                        ExamType = exam.ExamType,
                        Type = exam.Type,
                        Level = exam.Level switch
                        {
                            "1" => "Easy",
                            "2" => "Medium",
                            "3" => "Hard",
                            // 0 (or nothing) is "no level set", e.g. an exam made by the MCP import: no label, not "0".
                            null or "" or "0" => null,
                            _ => exam.Level,
                        },
                        Author = string.Join(' ', new[] { exam.FirstName, exam.LastName }.Where(t => !string.IsNullOrWhiteSpace(t))),
                        AuthorCoreId = long.TryParse(exam.UserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authorCoreId) ? authorCoreId : null,
                        Topics = [.. (exam.Topics ?? [])
                            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                            .OrderBy(t => int.TryParse(t.Order, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ? order : int.MaxValue)
                            .Select(t => t.Title!.Trim())],
                        QrCode = $"data:image/png;base64,{Convert.ToBase64String(qr)}",
                    },
                    Tests = [.. testResponses.Select(t => t!).Select(t => new ExamInformationResponseDto.TestDto
                    {
                        Question = t.Question,
                        QuestionFile = FileUrlOrNull(t.QuestionFile),
                        OptionA = t.OptionA,
                        OptionAFile = FileUrlOrNull(t.OptionAFile),
                        OptionB = t.OptionB,
                        OptionBFile = FileUrlOrNull(t.OptionBFile),
                        OptionC = t.OptionC,
                        OptionCFile = FileUrlOrNull(t.OptionCFile),
                        OptionD = t.OptionD,
                        OptionDFile = FileUrlOrNull(t.OptionDFile),
                        CorrectOption = t.TrueAnswer switch
                        {
                            "1" => 'A',
                            "2" => 'B',
                            "3" => 'C',
                            "4" => 'D',
                            _ => null,
                        },
                        AnswerHtml = string.IsNullOrWhiteSpace(t.AnswerFull) ? null : t.AnswerFull,
                        AnswerFile = FileUrlOrNull(t.AnswerFullFile),
                        QuestionType = t.Type,
                        AnswerViewType = t.AnswerViewType,
                        TestImageAnswers = t.TestImageAnswers,
                    })],
                };
                return new(OperationResult.Succeeded)
                {
                    Data = result,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<int>> GetExamQuestionCountAsync([NotNull] ExamInformationRequestDto requestDto)
        {
            try
            {
                var examResponse = await GetWithRetryAsync<CoreExamResponse>(
                    string.Format(configuration.Value.GetValue<string>("Core:Exam")!, requestDto.ExamId), GetHeaders(requestDto.SecretKey));
                return examResponse?.Data is { } exam
                    ? new(OperationResult.Succeeded) { Data = exam.Tests?.Count ?? 0 }
                    : new(OperationResult.Failed) { Errors = [new() { Message = examResponse?.Message ?? Localizer.Value["GeneralError"], }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        /// <summary>
        /// One gama-api GET, retried up to <see cref="ExamRequestAttempts"/> times. Returns the last response
        /// (possibly failed/null) once retries run out.
        /// </summary>
        private async Task<CoreResponse<T>?> GetWithRetryAsync<T>(string uri, List<(string Key, string Value)>? headers)
            where T : class
        {
            CoreResponse<T>? response = null;
            for (var attempt = 1; attempt <= ExamRequestAttempts; attempt++)
            {
                try
                {
                    response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<T>, IHttpRequest>(new()
                    {
                        Uri = uri,
                        Request = null,
                        HeaderParameters = headers,
                    });
                    if (response?.Status == 1 && response.Data is not null)
                    {
                        return response;
                    }
                }
                catch (HttpRequestException exc) when (attempt < ExamRequestAttempts)
                {
                    Logger.Value.LogException(exc);
                }

                if (attempt < ExamRequestAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt));
                }
            }

            return response;
        }

        public async Task<ResultData<IEnumerable<KeyValuePair<int, string?>>>> GetBoardsAsync()
        {
            try
            {
                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<List<CoreBoardResponse>>, IHttpRequest>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:Boards"),
                    Request = null,
                    HeaderParameters = GetHeaders(null),
                });

                if (response?.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                var data = response.Data.Select(t => new KeyValuePair<int, string?>(t.Id.ValueOf<int>(), t.Title));
                return new(OperationResult.Succeeded)
                {
                    Data = data,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<LegacyAuthResponseDto>> LegacyLoginAsync([NotNull] LegacyLoginRequestDto requestDto)
        {
            try
            {
                List<KeyValuePair<string, string?>> body = [new("identity", requestDto.Identity), new("pass", requestDto.Password)];
                if (!string.IsNullOrEmpty(requestDto.Type))
                {
                    body.Add(new("type", requestDto.Type));
                }
                if (requestDto.Code is not null)
                {
                    body.Add(new("code", requestDto.Code.Value.ToString(CultureInfo.InvariantCulture)));
                }

                var response = await HttpProvider.Value.PostAsync<IHttpRequest, CoreResponse<CoreLoginResponse>, List<KeyValuePair<string, string?>>>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:Login"),
                    Request = null,
                    Body = body,
                    HeaderParameters = GetHeaders(null),
                });
                return response switch
                {
                    null => new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] },
                    { Status: 1, Data.JwtToken: not null } => new(OperationResult.Succeeded) { Data = await MapAuthResultAsync(response.Data.Info, response.Data.JwtToken) },
                    // gama-api requires an OTP step-up (weak/easy-to-guess password) instead of failing outright -
                    // resubmit with type=confirm + code once the user has it. Not an error.
                    { Status: 1, Data.Type: not null and not "" } => new(OperationResult.Succeeded) { Data = new() { Type = response.Data.Type } },
                    _ => new(OperationResult.NotValid) { Errors = [new() { Message = response.Message ?? Localizer.Value["InvalidCredentials"], }] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<LegacyAuthResponseDto>> LegacyGoogleAuthAsync([NotNull] LegacyGoogleAuthRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.PostAsync<IHttpRequest, CoreResponse<CoreGoogleAuthResponse>, List<KeyValuePair<string, string?>>>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:GoogleAuth"),
                    Request = null,
                    Body = [new("id_token", requestDto.IdToken)],
                    HeaderParameters = GetHeaders(null),
                });
                return response switch
                {
                    null => new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] },
                    { Status: 1, Data.JwtToken: not null } => new(OperationResult.Succeeded) { Data = await MapAuthResultAsync(response.Data.Info, response.Data.JwtToken) },
                    _ => new(OperationResult.NotValid) { Errors = [new() { Message = response.Message ?? Localizer.Value["InvalidCredentials"], }] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<LegacyMessageResponseDto>> LegacyRegisterAsync([NotNull] LegacyOtpFlowRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.PostAsync<IHttpRequest, CoreResponse<CoreMessageResponse>, List<KeyValuePair<string, string?>>>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:Register"),
                    Request = null,
                    Body = [new("type", requestDto.Type), new("identity", requestDto.Identity), new("code", requestDto.Code?.ToString()), new("pass", requestDto.Password)],
                    HeaderParameters = GetHeaders(null),
                });
                return response switch
                {
                    null => new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] },
                    { Status: 1 } => new(OperationResult.Succeeded) { Data = new() { Message = response.Data?.Message, }, },
                    _ => new(OperationResult.NotValid) { Errors = [new() { Message = response.Message ?? Localizer.Value["GeneralError"], }] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<LegacyMessageResponseDto>> LegacyRecoveryAsync([NotNull] LegacyOtpFlowRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.PostAsync<IHttpRequest, CoreResponse<CoreMessageResponse>, List<KeyValuePair<string, string?>>>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:Recovery"),
                    Request = null,
                    Body = [new("type", requestDto.Type), new("identity", requestDto.Identity), new("code", requestDto.Code?.ToString()), new("pass", requestDto.Password)],
                    HeaderParameters = GetHeaders(null),
                });
                return response switch
                {
                    null => new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] },
                    { Status: 1 } => new(OperationResult.Succeeded) { Data = new() { Message = response.Data?.Message, }, },
                    _ => new(OperationResult.NotValid) { Errors = [new() { Message = response.Message ?? Localizer.Value["GeneralError"], }] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<Void>> LegacyLogoutAsync([NotNull] LegacyLogoutRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<object?>, IHttpRequest>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:Logout"),
                    Request = null,
                    HeaderParameters = GetHeaders(requestDto.Token),
                });
                return response switch
                {
                    null => new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] },
                    { Status: 1 } => new(OperationResult.Succeeded) { Data = new() },
                    _ => new(OperationResult.NotValid) { Errors = [new() { Message = response.Message ?? Localizer.Value["GeneralError"], }] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<Void>> LegacyUpdateGroupAsync([NotNull] LegacyUpdateGroupRequestDto requestDto)
        {
            try
            {
                // Deliberately no "uid" field - see this method's doc comment on ICoreProvider. gama-api infers
                // the target user from the forwarded token alone.
                var response = await HttpProvider.Value.PostAsync<IHttpRequest, CoreResponse<object?>, List<KeyValuePair<string, string?>>>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:UpdateGroup"),
                    Request = null,
                    Body = [new("group", requestDto.Group.ToString(CultureInfo.InvariantCulture))],
                    HeaderParameters = GetHeaders(requestDto.Token),
                });
                return response switch
                {
                    null => new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] },
                    { Status: 1 } => new(OperationResult.Succeeded) { Data = new() },
                    _ => new(OperationResult.NotValid) { Errors = [new() { Message = response.Message ?? Localizer.Value["GeneralError"], }] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<LegacyDashboardDataDto>> GetDashboardAsync([NotNull] DashboardRequestDto requestDto)
        {
            // Captured by postCallHandler below before the body is read, so it's available even if reading/
            // decoding the body then throws (e.g. gama-api's 401/403 body isn't valid JSON) - checked in both
            // the normal-return path and the catch block below.
            HttpStatusCode? legacyStatusCode = null;

            try
            {
                if (string.IsNullOrEmpty(requestDto.Token))
                {
                    // No forwardable legacy token (e.g. a native/local-token account) - nothing gama-api could
                    // authenticate. Not an error: the caller (IdentityService.GetDashboardAsync) treats this as
                    // "legacy data unavailable" and still returns a Succeeded overall response.
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["MissingLegacyToken"], }] };
                }

                // Mirrors gamatrain-front's own `userType === 5 ? teachers : students` ternary exactly - see
                // DashboardRequestDto.Group's doc comment.
                var uri = requestDto.Group == 5
                    ? configuration.Value.GetValue<string>("Core:TeacherDashboard")
                    : configuration.Value.GetValue<string>("Core:StudentDashboard");

                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<CoreDashboardResponse>, IHttpRequest>(new()
                {
                    Uri = uri,
                    Request = null,
                    HeaderParameters = GetHeaders(requestDto.Token),
                }, postCallHandler: r =>
                {
                    legacyStatusCode = r.StatusCode;
                    return Task.CompletedTask;
                });

                return (IsAuthRejection(legacyStatusCode), response) switch
                {
                    (true, _) => new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true } },
                    (false, { Status: 1, Data: not null } ok) => new(OperationResult.Succeeded) { Data = MapDashboard(ok.Data) },
                    _ => new(OperationResult.Failed) { Errors = [new() { Message = response?.Message ?? Localizer.Value["GeneralError"], }] },
                };
            }
            catch (Exception exc)
            {
                if (IsAuthRejection(legacyStatusCode))
                {
                    // gama-api's 401/403 body wasn't valid JSON, so reading/decoding it above threw - the
                    // status code was still captured before that happened. Still not an infrastructure
                    // failure: gama-api answered, it just rejected the token.
                    return new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true } };
                }

                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }

            static bool IsAuthRejection(HttpStatusCode? statusCode) => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

            static LegacyDashboardDataDto MapDashboard(CoreDashboardResponse source) => new()
            {
                LegacyDataAvailable = true,
                ScoreCheckInfo = source.User?.ScoreCheckInfo,
                Stats = MapStats(source.Stats),
                ExamSuggestions = MapExamSuggestions(source.ExamSuggestions),
            };

            static LegacyDashboardDataDto.StatsDto? MapStats(CoreDashboardResponse.StatsDto? source) => source is null ? null : new()
            {
                Test = MapStatItem(source.Test),
                File = MapStatItem(source.File),
                Question = MapStatItem(source.Question),
            };

            static LegacyDashboardDataDto.StatItemDto? MapStatItem(CoreDashboardResponse.StatItemDto? source) => source is null ? null : new() { Total = source.Total };

            static LegacyDashboardDataDto.ExamSuggestionsDto? MapExamSuggestions(CoreDashboardResponse.ExamSuggestionsDto? source) => source is null ? null : new()
            {
                Total = source.Total,
                Participated = source.Participated,
                Lessons = MapLessons(source.Lessons),
            };

            static Collection<LegacyDashboardDataDto.LessonDto>? MapLessons(Collection<CoreDashboardResponse.LessonDto>? source) => source is null ? null : new(source.Select(t => new LegacyDashboardDataDto.LessonDto
            {
                Id = t.Id,
                Title = t.Title,
                Participated = t.Participated,
                Total = t.Total,
            }).ToList());
        }

        public async Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetExamOptionsAsync([NotNull] ExamImportOptionsRequestDto requestDto)
        {
            if (!ExamOptionTypes.TryGetValue(requestDto.Kind, out var option))
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = $"Unknown kind {requestDto.Kind}." }] };
            }

            List<KeyValuePair<string, string?>> query = [new("type", option.Type)];
            AddIfSet(query, option.ParentFilter ?? string.Empty, option.ParentFilter is null ? null : requestDto.ParentId);
            AddIfSet(query, "course_id", requestDto.Kind == "subject" ? requestDto.CourseId : null);
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, WithQuery("Core:Types", query), requestDto.SecretKey);
            return result.OperationResult is OperationResult.Succeeded
                ? new(OperationResult.Succeeded)
                {
                    Data = [.. ListItems(result.Data)
                        .Select(t => new ExamImportOptionDto { Id = (int)(ReadLong(t, "id") ?? 0), Title = ReadString(t, "title")?.Trim() })
                        .Where(t => t.Id > 0)],
                }
                : new(result.OperationResult) { Errors = result.Errors };
        }

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto)
        {
            List<KeyValuePair<string, string?>> query = [requestDto.Latest ? new("sortby", "subdatedesc") : new("is_paper", "true")];
            AddIfSet(query, "section", requestDto.BoardId);
            AddIfSet(query, "base", requestDto.GradeId);
            AddIfSet(query, "lesson", requestDto.SubjectId);
            AddIfSet(query, "test_type", requestDto.ClassificationId);
            AddIfSet(query, "edu_year", requestDto.Year);
            AddIfSet(query, "edu_month", requestDto.SessionMonth);
            AddIfSet(query, "page", requestDto.Page);
            AddIfSet(query, "perpage", requestDto.PageSize);
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, WithQuery("Core:PastPapers", query), requestDto.SecretKey);
            return result.OperationResult is OperationResult.Succeeded
                ? new(OperationResult.Succeeded) { Data = [.. ListItems(result.Data).Select(ToPastPaper)] }
                : new(result.OperationResult) { Errors = result.Errors };
        }

        public async Task<ResultData<ExamImportPastPaperDto>> GetPastPaperAsync([NotNull] ExamImportRequestDto requestDto)
        {
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, ConfiguredUrl("Core:TestDetails", requestDto.Id), requestDto.SecretKey);
            return result.OperationResult is OperationResult.Succeeded && ReadLong(result.Data, "id") is not null
                ? new(OperationResult.Succeeded) { Data = ToPastPaper(result.Data) }
                : new(OperationResult.Failed) { Errors = result.Errors ?? [new() { Message = "gama-api returned no paper.", }] };
        }

        public async Task<ResultData<Uri>> GetPastPaperFileUrlAsync([NotNull] ExamImportPaperFileRequestDto requestDto)
        {
            var uri = ConfiguredUrl("Core:TestDownload", requestDto.PaperId, requestDto.Type);
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, requestDto.ExtraId is { } extraId ? $"{uri}/{extraId.ToString(CultureInfo.InvariantCulture)}" : uri, requestDto.SecretKey);
            return result.OperationResult is OperationResult.Succeeded && Uri.TryCreate(ReadString(result.Data, "url"), UriKind.Absolute, out var url)
                ? new(OperationResult.Succeeded) { Data = url }
                : new(OperationResult.Failed) { Errors = result.Errors ?? [new() { Message = "gama-api returned no download link.", }] };
        }

        public async Task<ResultData<string>> UploadFileAsync([NotNull] ExamImportUploadRequestDto requestDto)
        {
            using ByteArrayContent file = new(requestDto.Content);
            file.Headers.ContentType = new(requestDto.ContentType);
            using MultipartFormDataContent body = new() { { file, "file", requestDto.FileName } };

            var result = await SendExamBuilderRequestAsync(HttpMethod.Post, configuration.Value.GetValue<string>("Core:Upload"), requestDto.SecretKey, body);
            if (result.OperationResult is not OperationResult.Succeeded)
            {
                return new(result.OperationResult) { Errors = result.Errors };
            }

            // [{"file": {"name": "<uid>-<uuid>/<name>", "size": "..."}}]
            var key = ListItems(result.Data)
                .Where(t => t.ValueKind == JsonValueKind.Object)
                .SelectMany(t => t.EnumerateObject())
                .Select(t => ReadString(t.Value, "name"))
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));
            return key is null
                ? new(OperationResult.Failed) { Errors = [new() { Message = "gama-api returned no file key for the upload.", }] }
                : new(OperationResult.Succeeded) { Data = key };
        }

        public async Task<ResultData<long>> SaveExamTestAsync([NotNull] SaveExamTestRequestDto requestDto)
        {
            // The form gamatrain's test-maker sends.
            List<KeyValuePair<string, string?>> form =
            [
                new("type", requestDto.Type),
                new("direction", "ltr"),
                new("question", requestDto.QuestionHtml),
                new("answer_full", requestDto.AnswerHtml ?? string.Empty),
                new("resource", requestDto.Resource),
                new("testImgAnswers", requestDto.ImageOptions ? "1" : "0"),
            ];
            AddIfSet(form, "section", requestDto.BoardId);
            AddIfSet(form, "base", requestDto.GradeId);
            AddIfSet(form, "course", requestDto.CourseId);
            AddIfSet(form, "lesson", requestDto.SubjectId);
            AddIfSet(form, "topic", requestDto.TopicId);
            AddIfSet(form, "level", requestDto.Level);
            AddIfSet(form, "true_answer", requestDto.CorrectOption);
            form.AddRange(requestDto.OptionsHtml.Take(4).Select((t, i) => new KeyValuePair<string, string?>($"answer_{OptionLetters[i]}", t)));
            AddFile(form, "q_file", requestDto.QuestionFile);
            foreach (var (key, i) in requestDto.ImageOptions ? requestDto.OptionFiles.Take(4).Select((t, i) => (t, i)) : [])
            {
                AddFile(form, $"{OptionLetters[i]}_file", key);
            }

            AddFile(form, "answer_full_file", requestDto.AnswerFile);
            if (requestDto.Id is { } id)
            {
                var updated = await SendExamBuilderRequestAsync(HttpMethod.Put, ConfiguredUrl("Core:Test", id), requestDto.SecretKey, form);
                return updated.OperationResult is OperationResult.Succeeded ? new(OperationResult.Succeeded) { Data = id } : new(updated.OperationResult) { Errors = updated.Errors };
            }

            var result = await SendExamBuilderRequestAsync(HttpMethod.Post, configuration.Value.GetValue<string>("Core:AddExamTest"), requestDto.SecretKey, form);
            return result.OperationResult is OperationResult.Succeeded && ReadLong(result.Data, "id") is long newId
                ? new(OperationResult.Succeeded) { Data = newId }
                : new(OperationResult.Failed) { Errors = result.Errors ?? [new() { Message = "gama-api returned no question id.", }] };

            static void AddFile(List<KeyValuePair<string, string?>> form, string name, string? key)
            {
                if (key is not null)
                {
                    form.Add(new(name, key));
                }
            }
        }

        public async Task<ResultData<Void>> DeleteExamTestAsync([NotNull] ExamImportRequestDto requestDto) =>
            ToVoid(await SendExamBuilderRequestAsync(HttpMethod.Delete, ConfiguredUrl("Core:Test", requestDto.Id), requestDto.SecretKey));

        public async Task<ResultData<long?>> GetCurrentExamIdAsync([NotNull] ExamImportRequestDto requestDto)
        {
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, configuration.Value.GetValue<string>("Core:CurrentExam"), requestDto.SecretKey);
            return result switch
            {
                { OperationResult: OperationResult.Succeeded } => new(OperationResult.Succeeded) { Data = ReadLong(result.Data, "id") },
                // gama-api answers "noResult" when the caller has no draft.
                _ when result.Errors?.Any(t => t.Reference == "noResult") == true => new(OperationResult.Succeeded) { Data = null },
                _ => new(result.OperationResult) { Errors = result.Errors },
            };
        }

        public async Task<ResultData<ExamImportDraftDto>> GetExamAsync([NotNull] ExamImportRequestDto requestDto)
        {
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, ConfiguredUrl("Core:Exam", requestDto.Id), requestDto.SecretKey);
            if (result.OperationResult is not OperationResult.Succeeded || ReadLong(result.Data, "id") is not long examId)
            {
                return new(OperationResult.Failed) { Errors = result.Errors ?? [new() { Message = "gama-api returned no exam.", }] };
            }

            var item = result.Data;
            return new(OperationResult.Succeeded)
            {
                Data = new()
                {
                    Id = examId,
                    Title = ReadString(item, "title"),
                    BoardId = (int?)ReadLong(item, "section"),
                    Board = ReadString(item, "section_title")?.Trim(),
                    GradeId = (int?)ReadLong(item, "base"),
                    Grade = ReadString(item, "base_title")?.Trim(),
                    CourseId = ReadLong(item, "course") is > 0 and var course ? (int)course : null,
                    SubjectId = (int?)ReadLong(item, "lesson"),
                    Subject = ReadString(item, "lesson_title")?.Trim(),
                    PaperId = (int?)ReadLong(item, "azmoon_type"),
                    Paper = ReadString(item, "azmoon_type_title")?.Trim(),
                    DurationMinutes = (int?)ReadLong(item, "azmoon_time"),
                    Year = ReadLong(item, "edu_year") is > 0 and var year ? (int)year : null,
                    SessionMonth = ReadLong(item, "edu_month") is > 0 and var month ? (int)month : null,
                    Level = ReadLong(item, "level") is > 0 and var level ? (int)level : null,
                    QuestionIds = [.. ListItems(Property(item, "tests")).Select(ReadId).Where(t => t > 0)],
                    Owner = ReadBool(item, "owner"),
                    Status = (int)(ReadLong(item, "status") ?? 0),
                },
            };
        }

        public async Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync([NotNull] ExamImportRequestDto requestDto)
        {
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, ConfiguredUrl("Core:ExamTest", requestDto.Id), requestDto.SecretKey);
            return result.OperationResult is OperationResult.Succeeded
                ? new(OperationResult.Succeeded) { Data = [.. ListItems(result.Data).Select(ToDraftQuestion).Where(t => t.Id > 0)] }
                : new(result.OperationResult) { Errors = result.Errors };
        }

        public async Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto)
        {
            List<KeyValuePair<string, string?>> form =
            [
                new("title", SafeTitle(requestDto.Title)),
                new("negative_point", requestDto.NegativeMarking ? "1" : "0"),
            ];
            AddIfSet(form, "section", requestDto.BoardId);
            AddIfSet(form, "base", requestDto.GradeId);
            AddIfSet(form, "course", requestDto.CourseId);
            AddIfSet(form, "lesson", requestDto.SubjectId);
            AddIfSet(form, "exam_type", requestDto.PaperId);
            AddIfSet(form, "duration", requestDto.DurationMinutes);
            AddIfSet(form, "paperID", requestDto.PastPaperId);

            // gama-api's edit keeps a field it isn't sent, so a change sends the optional ones left out as 0 to clear them.
            foreach (var (name, value) in new[] { ("level", requestDto.Level), ("edu_year", requestDto.Year), ("edu_month", requestDto.SessionMonth) })
            {
                if (value is > 0 || requestDto.ExamId is not null)
                {
                    form.Add(new(name, (value ?? 0).ToString(CultureInfo.InvariantCulture)));
                }
            }
            if (requestDto.ExamId is { } id)
            {
                var updated = await SendExamBuilderRequestAsync(HttpMethod.Put, ConfiguredUrl("Core:Exam", id), requestDto.SecretKey, form);
                return updated.OperationResult is OperationResult.Succeeded ? new(OperationResult.Succeeded) { Data = id } : new(updated.OperationResult) { Errors = updated.Errors };
            }

            var result = await SendExamBuilderRequestAsync(HttpMethod.Post, configuration.Value.GetValue<string>("Core:AddExam"), requestDto.SecretKey, form);
            return result.OperationResult is OperationResult.Succeeded && ReadLong(result.Data, "id") is long newId
                ? new(OperationResult.Succeeded) { Data = newId }
                : new(OperationResult.Failed) { Errors = result.Errors ?? [new() { Message = "gama-api returned no exam id.", }] };
        }

        public async Task<ResultData<IEnumerable<long>>> GetExamTestIdsAsync([NotNull] ExamImportRequestDto requestDto)
        {
            var result = await SendExamBuilderRequestAsync(HttpMethod.Get, ConfiguredUrl("Core:ExamQuestions", requestDto.Id), requestDto.SecretKey);
            return result.OperationResult is OperationResult.Succeeded
                ? new(OperationResult.Succeeded) { Data = [.. ListItems(result.Data).Select(ReadId).Where(t => t > 0)] }
                : new(result.OperationResult) { Errors = result.Errors };
        }

        public async Task<ResultData<Void>> SetExamTestsAsync([NotNull] SetExamTestsRequestDto requestDto)
        {
            // tests[]=..., the form gamatrain's test-maker sends; an empty "tests" clears the list.
            List<KeyValuePair<string, string?>> form = [.. requestDto.TestIds.Select(t => new KeyValuePair<string, string?>("tests[]", t.ToString(CultureInfo.InvariantCulture)))];
            if (form.Count == 0)
            {
                form.Add(new("tests", string.Empty));
            }

            return ToVoid(await SendExamBuilderRequestAsync(HttpMethod.Put, ConfiguredUrl("Core:ExamQuestions", requestDto.ExamId), requestDto.SecretKey, form));
        }

        public async Task<ResultData<Void>> PublishExamAsync([NotNull] ExamImportRequestDto requestDto)
        {
            // An empty form, not an empty JSON body ("null").
            List<KeyValuePair<string, string?>> form = [];
            return ToVoid(await SendExamBuilderRequestAsync(HttpMethod.Put, ConfiguredUrl("Core:PublishExam", requestDto.Id), requestDto.SecretKey, form));
        }

        public async Task<ResultData<Void>> DeleteExamAsync([NotNull] ExamImportRequestDto requestDto) =>
            ToVoid(await SendExamBuilderRequestAsync(HttpMethod.Delete, ConfiguredUrl("Core:Exam", requestDto.Id), requestDto.SecretKey));

        /// <summary>
        /// One exam-builder call to gama-api. Its envelope is <c>{"status":1,"data":...}</c> on success and
        /// <c>{"status":0,"error":"code","message"?,"data"?}</c> on failure (with a 4xx, sometimes a 200), so success is the
        /// envelope's status, not the HTTP code. A failure keeps gama-api's code (<c>Reference</c>), its <c>reason</c>
        /// (<c>Info</c>) and the HTTP status (<c>Value</c>), which the import's retry logic reads; a body that isn't JSON
        /// (an nginx 502 page, a 429) fails with only the status.
        /// </summary>
        private async Task<ResultData<JsonElement>> SendExamBuilderRequestAsync(HttpMethod method, string? uri, string token, object? body = null)
        {
            HttpStatusCode? statusCode = null;
            try
            {
                HttpProviderRequest<object, IHttpRequest> request = new()
                {
                    Uri = uri,
                    Request = null,
                    Body = body,
                    HeaderParameters = GetHeaders(token),
                };
                Task CaptureStatusAsync(HttpResponseMessage response)
                {
                    statusCode = response.StatusCode;
                    return Task.CompletedTask;
                }

                var response = method.Method switch
                {
                    "GET" => await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<JsonElement>, object>(request, CaptureStatusAsync),
                    "POST" => await HttpProvider.Value.PostAsync<IHttpRequest, CoreResponse<JsonElement>, object>(request, CaptureStatusAsync),
                    "PUT" => await HttpProvider.Value.PutAsync<IHttpRequest, CoreResponse<JsonElement>, object>(request, CaptureStatusAsync),
                    _ => await HttpProvider.Value.DeleteAsync<IHttpRequest, CoreResponse<JsonElement>, object>(request, CaptureStatusAsync),
                };
                return response is { Status: 1 }
                    ? new(OperationResult.Succeeded) { Data = response.Data }
                    : new(OperationResult.Failed) { Errors = [ToExamBuilderError(response, statusCode)] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, Value = (int?)statusCode }] };
            }

            static Error ToExamBuilderError(CoreResponse<JsonElement>? response, HttpStatusCode? statusCode)
            {
                var data = response?.Data ?? default;
                var reason = ReadString(data, "reason");
                var details = data.ValueKind is JsonValueKind.Array or JsonValueKind.Object && reason is null ? $" {data.GetRawText()}" : null;
                return new()
                {
                    Message = $"{response?.Message ?? response?.Error ?? $"HTTP {(int?)statusCode}"}{details}",
                    Reference = response?.Error,
                    Info = reason,
                    Value = (int?)statusCode,
                };
            }
        }

        private string WithQuery(string uriKey, IEnumerable<KeyValuePair<string, string?>> query) =>
            QueryHelpers.AddQueryString(configuration.Value.GetValue<string>(uriKey)!, query.Where(t => !string.IsNullOrEmpty(t.Value)));

        private string ConfiguredUrl(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>(key)!, args);

        private static void AddIfSet(List<KeyValuePair<string, string?>> fields, string name, long? value)
        {
            if (value is > 0)
            {
                fields.Add(new(name, value.Value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>gama-api's exams endpoint strips <c>" ' ! = * #</c>, <c>/</c> and newlines from titles: replace <c>/</c>
        /// instead of losing it.</summary>
        private static string SafeTitle(string title)
        {
            var safe = WhitespaceRegex().Replace(TitleStripRegex().Replace(title.Replace('/', '-'), string.Empty), " ").Trim();
            return safe.Length > 200 ? safe[..200] : safe;
        }

        /// <summary>gama-api answers some lists bare and others as <c>{"list": [...]}</c>.</summary>
        private static List<JsonElement> ListItems(JsonElement data)
        {
            var list = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("list", out var inner) ? inner : data;
            return list.ValueKind == JsonValueKind.Array ? [.. list.EnumerateArray()] : [];
        }

        /// <summary>gama-api sends ids and numbers as strings or as numbers.</summary>
        private static string? ReadString(JsonElement item, string name) =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                    _ => null,
                }
                : null;

        private static long? ReadLong(JsonElement item, string name) =>
            long.TryParse(ReadString(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static bool ReadBool(JsonElement item, string name) => ReadString(item, name) is "true" or "1";

        private static JsonElement Property(JsonElement item, string name) =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value : default;

        /// <summary>
        /// A paper from <c>GET tests</c> (whether each main file exists, as the <c>q_file</c>, <c>q_file_word</c> and
        /// <c>a_file</c> flags) or from <c>GET tests/{id}</c> (<c>files</c>, extra files included, each with its price and
        /// whether the caller paid; whether the caller owns the paper or manages it as an admin; its linked <c>exams</c>).
        /// </summary>
        private static ExamImportPastPaperDto ToPastPaper(JsonElement item) => new()
        {
            Id = ReadLong(item, "id") ?? 0,
            Title = ReadString(item, "title"),
            BoardId = (int?)ReadLong(item, "section"),
            Board = ReadString(item, "section_title")?.Trim(),
            GradeId = (int?)ReadLong(item, "base"),
            Grade = ReadString(item, "base_title")?.Trim(),
            CourseId = ReadLong(item, "course") is > 0 and var course ? (int)course : null,
            SubjectId = (int?)ReadLong(item, "lesson"),
            Subject = ReadString(item, "lesson_title")?.Trim(),
            Classification = ReadString(item, "test_type_title")?.Trim(),
            Year = ReadLong(item, "edu_year") is > 0 and var year ? (int)year : null,
            Month = ReadLong(item, "edu_month") is > 0 and var month ? (int)month : null,
            ExamLinked = Property(item, "files").ValueKind == JsonValueKind.Object ? ListItems(Property(item, "exams")).Count > 0 : null,
            Managed = ReadBool(item, "owner") || ReadBool(item, "admin"),
            Files = [.. PaperFiles(item)],
        };

        private static IEnumerable<ExamImportPastPaperDto.FileDto> PaperFiles(JsonElement item)
        {
            var files = Property(item, "files");
            if (files.ValueKind != JsonValueKind.Object)
            {
                foreach (var (type, flag) in new[] { ("pdf", "q_file"), ("word", "q_file_word"), ("answer", "a_file") })
                {
                    if (ReadBool(item, flag))
                    {
                        yield return new() { Type = type };
                    }
                }

                yield break;
            }

            foreach (var type in new[] { "pdf", "word", "answer" })
            {
                if (files.TryGetProperty(type, out var file) && ReadBool(file, "exist"))
                {
                    yield return new() { Type = type, Extension = ReadString(file, "ext"), Free = IsFree(file) };
                }
            }

            foreach (var extra in ListItems(Property(files, "extra")))
            {
                yield return new()
                {
                    Type = "extra",
                    ExtraId = ReadLong(extra, "id"),
                    Label = ReadString(extra, "type_title")?.Trim(),
                    Extension = ReadString(extra, "ext"),
                    Free = IsFree(extra),
                };
            }

            static bool IsFree(JsonElement file) => ReadLong(file, "price") == 0 || ReadBool(file, "paid");
        }

        /// <summary>A bare id (<c>"12"</c> or <c>12</c>) from a list of ids.</summary>
        private static long ReadId(JsonElement item)
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        }

        /// <summary>A question from <c>examTests</c>: gama-api's files are full URLs (empty or <c>"0"</c> for none), its
        /// <c>owner</c> says whether the caller created it, and status 0 is not reviewed yet.</summary>
        private static ExamImportDraftQuestionDto ToDraftQuestion(JsonElement item)
        {
            Uri? File(string name) => Uri.TryCreate(ReadString(item, name), UriKind.Absolute, out var url) ? url : null;
            return new()
            {
                Id = ReadLong(item, "id") ?? 0,
                Type = ReadString(item, "type"),
                Html = ReadString(item, "question"),
                Image = File("q_file"),
                Options = [.. "abcd".Select(t => ReadString(item, $"answer_{t}"))],
                OptionImages = [.. "abcd".Select(t => File($"{t}_file"))],
                Correct = (int)(ReadLong(item, "true_answer") ?? 0),
                AnswerHtml = ReadString(item, "answer_full"),
                AnswerImage = File("answer_full_file"),
                Owner = ReadBool(item, "owner"),
                Pending = ReadLong(item, "status") == 0,
            };
        }

        private static ResultData<Void> ToVoid(ResultData<JsonElement> result) => result.OperationResult is OperationResult.Succeeded
            ? new(OperationResult.Succeeded) { Data = new() }
            : new(result.OperationResult) { Errors = result.Errors };

        private List<(string Key, string Value)>? GetHeaders(string? authorizationToken)
        {
            var ip = httpContextAccessor.Value.HttpContext.GetClientIpAddress();
            List<(string Key, string Value)> headers = [("TRUSTED_FORWARDED_IP", ip ?? ""), (XForwardedFor, ip ?? "")];
            if (!string.IsNullOrEmpty(authorizationToken))
            {
                headers.Add(("Authorization", $"Bearer {authorizationToken}"));
            }

            return headers;
        }

        private async Task<LegacyAuthResponseDto> MapAuthResultAsync(CoreAuthUserInfoResponse? info, string jwtToken)
        {
            LegacyAuthResponseDto result = new()
            {
                Token = jwtToken,
                FirstName = info?.FirstName,
                LastName = info?.LastName,
                Email = info?.Email,
                PhoneNumber = info?.Phone,
                Gender = MapGender(info?.Sex),
                // Teacher/student signal, mirrored verbatim from gama-api - 5 = Teacher, 6 = Student, see
                // ApplicationUser.Group's doc comment / docs/business/identity-and-access.md.
                Group = info?.Group.ValueOf<int?>(),
            };

            if (!string.IsNullOrEmpty(info?.Avatar))
            {
                var content = await HttpProvider.Value.GetByteArrayAsync<IHttpRequest, IHttpRequest>(new()
                {
                    Uri = info.Avatar,
                    Request = null,
                });
                if (content is not null)
                {
                    result.Avatar = new()
                    {
                        Name = Path.GetFileName(info.Avatar),
                        Content = content,
                    };
                }
            }

            return result;

            static GenderType? MapGender(string? sex) => sex switch
            {
                "1" => GenderType.Male,
                "2" => GenderType.Female,
                _ => null,
            };
        }

        [GeneratedRegex("[\"'!=*#\\n\\r\\t]")]
        private static partial Regex TitleStripRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();
    }
}
