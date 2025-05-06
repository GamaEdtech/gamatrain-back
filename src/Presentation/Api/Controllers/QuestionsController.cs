namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Presentation.ViewModel.Question;

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class QuestionsController(Lazy<ILogger<QuestionsController>> logger, Lazy<IQuestionService> questionService)
        : ApiControllerBase<QuestionsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<IEnumerable<RandomQuestionResponseViewModel>>>()]
        public async Task<IActionResult<IEnumerable<RandomQuestionResponseViewModel>>> GetRandomQuestions([NotNull, FromQuery] RandomQuestionsRequestViewModel request)
        {
            try
            {
                var result = await questionService.Value.GetRandomQuestionsAsync(new()
                {
                    Count = request.Count.GetValueOrDefault(),
                });
                if (result.Data is null)
                {
                    return Ok<IEnumerable<RandomQuestionResponseViewModel>>(new(result.Errors));
                }

                List<RandomQuestionResponseViewModel> lst = [];
                var secret = TokenAuthenticationHandler.GetTokenFromHeader(Request).AsSpan(0, 32);
                foreach (var item in result.Data)
                {
                    List<OptionInfoViewModel> options = [];
                    List<int> values = [];
                    foreach (var option in item.Options)
                    {
                        if (option.IsCorrect)
                        {
                            values.Add(option.Index);
                        }

                        options.Add(new()
                        {
                            Body = option.Body,
                            Index = option.Index,
                        });
                    }

                    lst.Add(new()
                    {
                        Id = item.Id,
                        Body = item.Body,
                        Options = options,
                        Values = Cryptography.EncryptAes(JsonSerializer.Serialize(values), secret.ToString()),
                    });
                }

                return Ok<IEnumerable<RandomQuestionResponseViewModel>>(new()
                {
                    Data = lst,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<RandomQuestionResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
