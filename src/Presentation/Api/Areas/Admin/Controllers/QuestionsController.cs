namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Question;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Question;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class QuestionsController(Lazy<ILogger<QuestionsController>> logger, Lazy<IQuestionService> questionService)
        : ApiControllerBase<QuestionsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<QuestionsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<QuestionsResponseViewModel>>> GetQuestions([NotNull, FromQuery] QuestionsRequestViewModel request)
        {
            try
            {
                var result = await questionService.Value.GetQuestionsAsync(new ListRequestDto<Question>
                {
                    PagingDto = request.PagingDto,
                });
                return Ok<ListDataSource<QuestionsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new QuestionsResponseViewModel
                        {
                            Id = t.Id,
                            Body = t.Body,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<QuestionsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("{id:long}"), Produces<ApiResponse<QuestionResponseViewModel>>()]
        public async Task<IActionResult<QuestionResponseViewModel>> GetQuestion([FromRoute] long id)
        {
            try
            {
                var result = await questionService.Value.GetQuestionAsync(new IdEqualsSpecification<Question, long>(id));
                return Ok<QuestionResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Body = result.Data.Body,
                        Options = result.Data.Options.Select(t => new OptionViewModel
                        {
                            Body = t.Body,
                            IsCorrect = t.IsCorrect,
                        }),
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<QuestionResponseViewModel> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost, Produces<ApiResponse<ManageQuestionResponseViewModel>>()]
        public async Task<IActionResult<ManageQuestionResponseViewModel>> CreateQuestion([NotNull] ManageQuestionRequestViewModel request)
        {
            try
            {
                List<OptionDto> options = [];
                var index = 1;
                foreach (var item in request.Options)
                {
                    options.Add(new()
                    {
                        Body = item.Body,
                        IsCorrect = item.IsCorrect,
                        Index = index,
                    });

                    index++;
                }

                var result = await questionService.Value.ManageQuestionAsync(new()
                {
                    Body = request.Body!,
                    Options = options,
                });
                return Ok<ManageQuestionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageQuestionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("{id:long}"), Produces<ApiResponse<ManageQuestionResponseViewModel>>()]
        public async Task<IActionResult<ManageQuestionResponseViewModel>> UpdateQuestion([FromRoute] long id, [NotNull, FromBody] ManageQuestionRequestViewModel request)
        {
            try
            {
                List<OptionDto> options = [];
                var index = 1;
                foreach (var item in request.Options)
                {
                    options.Add(new()
                    {
                        Body = item.Body,
                        IsCorrect = item.IsCorrect,
                        Index = index,
                    });

                    index++;
                }
                var result = await questionService.Value.ManageQuestionAsync(new ManageQuestionRequestDto
                {
                    Id = id,
                    Body = request.Body!,
                    Options = options,
                });
                return Ok<ManageQuestionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageQuestionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemoveQuestion([FromRoute] long id)
        {
            try
            {
                var result = await questionService.Value.RemoveQuestionAsync(new IdEqualsSpecification<Question, long>(id));
                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
