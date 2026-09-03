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

        public async Task<ResultData<ExamInformationResponseDto>> GetExamInformationAsync([NotNull] ExamInformationRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<CoreExamInformationResponse>, IHttpRequest>(new()
                {
                    Uri = string.Format(configuration.Value.GetValue<string>("Core:ExamInfo")!, requestDto.ExamId),
                    Request = null,
                    HeaderParameters = GetHeaders(requestDto.SecretKey),
                });

                if (response is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                if (response.Data?.Exam is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = response.Message, }] };
                }

                var examDetailsUrl = string.Format(configuration.Value.GetValue<string>("Core:ExamDetailsUrl")!, response.Data.Exam.Code);
                var qr = QRCodeImageBuilder.GetPngBytes(examDetailsUrl);

                ExamInformationResponseDto result = new()
                {
                    Exam = new()
                    {
                        Title = response.Data.Exam.Title,
                        TestsCount = response.Data.Exam.TestsCount.ValueOf<int>(),
                        StartDate = response.Data.Exam.StartDate,
                        EndDate = response.Data.Exam.EndDate,
                        ExamTime = response.Data.Exam.ExamTime,
                        ExamType = response.Data.Exam.ExamType,
                        Type = response.Data.Exam.Type,
                        ScoreType = response.Data.Exam.ScoreType,
                        QrCode = $"data:image/png;base64,{Convert.ToBase64String(qr)}",
                    },
                    Tests = response.Data?.Tests?.Select(t => new ExamInformationResponseDto.TestDto
                    {
                        Question = t.Question,
                        QuestionFile = t.QuestionFile,
                        OptionA = t.OptionA,
                        OptionAFile = t.OptionAFile,
                        OptionB = t.OptionB,
                        OptionBFile = t.OptionBFile,
                        OptionC = t.OptionC,
                        OptionCFile = t.OptionCFile,
                        OptionD = t.OptionD,
                        OptionDFile = t.OptionDFile,
                    }).ToList(),
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

        public async Task<ResultData<UserInformationResponseDto>> GetUserInformationAsync([NotNull] UserInformationRequestDto requestDto)
        {
            try
            {
                var response = await HttpProvider.Value.GetAsync<IHttpRequest, CoreResponse<CoreUserInformationResponse>, IHttpRequest>(new()
                {
                    Uri = configuration.Value.GetValue<string>("Core:UserInfo"),
                    Request = null,
                    HeaderParameters = GetHeaders(requestDto.Token),
                });
                if (response is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                if (response.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["InvalidToken"], }] };
                }

                UserInformationResponseDto data = new()
                {
                    FirstName = response.Data.FirstName,
                    LastName = response.Data.LastName,
                    PhoneNumber = response.Data.Phone,
                    Gender = MapGender(response.Data.Sex),
                    Grade = response.Data.Grade.ValueOf<int?>(),
                    CoreId = response.Data.CoreId.ValueOf<long?>(),
                    // Teacher/student signal, mirrored verbatim from gama-api - 5 = Teacher, 6 = Student, see
                    // ApplicationUser.Group's doc comment / docs/business/identity-and-access.md.
                    Group = response.Data.Group,
                };
                if (!string.IsNullOrEmpty(response.Data.Avatar))
                {
                    var content = await HttpProvider.Value.GetByteArrayAsync<IHttpRequest, IHttpRequest>(new()
                    {
                        Uri = response.Data.Avatar,
                        Request = null,
                    });
                    if (content is not null)
                    {
                        data.Avatar = new()
                        {
                            Name = Path.GetFileName(response.Data.Avatar),
                            Content = content,
                        };
                    }
                }

                return new(OperationResult.Succeeded)
                {
                    Data = data,
                };

                static GenderType? MapGender(string? sex) => sex switch
                {
                    "1" => GenderType.Male,
                    "2" => GenderType.Female,
                    _ => null,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
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
