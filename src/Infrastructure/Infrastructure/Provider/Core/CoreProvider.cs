namespace GamaEdtech.Infrastructure.Provider.Core
{
    using System;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using SkiaSharp;
    using SkiaSharp.QrCode;
    using SkiaSharp.QrCode.Image;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    public sealed class CoreProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<CoreProvider>> localizer
        , Lazy<ILogger<CoreProvider>> logger, Lazy<IHttpContextAccessor> httpContextAccessor)
        : InfrastructureBase<CoreProvider>(httpProvider, localizer, logger), ICoreProvider
    {
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

        /// <summary>How many of an exam's per-question requests run at once (see GetExamInformationAsync) -- enough
        /// to load a 40-question exam in ~2s without flooding gama-api.</summary>
        private const int MaxConcurrentExamTestRequests = 8;

        private const int ExamRequestAttempts = 3;

        /// <summary>gama-api sends <c>"0"</c> (not null) in its <c>q_file</c>/<c>a_file</c>... fields for "no
        /// image" -- normalized to <see langword="null"/> here so every export treats it as absent, instead of
        /// as an image to show (the Word export used to add an empty full-width image row for it).</summary>
        private static string? FileUrlOrNull(string? value) => string.IsNullOrWhiteSpace(value) || value == "0" ? null : value;

        /// <summary>
        /// Loads everything an exam export needs from two read-only gama-api endpoints (2026-09-24): the exam's
        /// details and ordered question ids from <c>Core:Exam</c> (<c>exams/{id}</c>), then each question in full --
        /// including its correct option, for the exports' Answer Key -- from <c>Core:ExamTest</c>
        /// (<c>examTests?id={id}</c>), fetched in parallel. Replaces <c>exams/start</c>, which never returned correct
        /// answers and starts an exam attempt as a side effect. Any question that still fails after retries fails
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
                using var throttle = new SemaphoreSlim(MaxConcurrentExamTestRequests);
                var testResponses = await Task.WhenAll(testIds.Select(async id =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        var response = await GetWithRetryAsync<CoreExamTestListResponse>(string.Format(configuration.Value.GetValue<string>("Core:ExamTest")!, id), headers);
                        return response?.Data?.List?.FirstOrDefault(t => t.Id == id);
                    }
                    finally
                    {
                        _ = throttle.Release();
                    }
                }));

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
                            _ => exam.Level,
                        },
                        Author = string.Join(' ', new[] { exam.FirstName, exam.LastName }.Where(t => !string.IsNullOrWhiteSpace(t))),
                        AuthorCoreId = long.TryParse(exam.UserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authorCoreId) ? authorCoreId : null,
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

        /// <summary>
        /// One gama-api GET, retried a couple of times: its endpoints intermittently answer "Target resource is no
        /// longer available at the origin server" and then succeed moments later, and an exam export now makes
        /// one call per question. Returns the last response (possibly failed/null) once retries run out.
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
    }
}
