namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;

    using GamaEdtech.Data.Dto.Question;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class QuestionService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<FileService>> localizer
        , Lazy<ILogger<FileService>> logger)
        : LocalizableServiceBase<FileService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IQuestionService
    {
        public async Task<ResultData<ListDataSource<QuestionsDto>>> GetQuestionsAsync(ListRequestDto<Question>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Question>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new QuestionsDto
                {
                    Id = t.Id,
                    Body = t.Body,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IEnumerable<QuestionDto>>> GetRandomQuestionsAsync([NotNull] RandomQuestionsRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Question>().GetManyQueryable().OrderBy(t => Guid.NewGuid()).Take(requestDto.Count).Select(t => new QuestionDto
                {
                    Id = t.Id,
                    Body = t.Body,
                    Options = t.Options.Select(o => new OptionDto
                    {
                        Index = o.Index,
                        Body = o.Body,
                        IsCorrect = o.IsCorrect,
                    }),
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<QuestionDto>> GetQuestionAsync([NotNull] ISpecification<Question> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<Question>().GetManyQueryable(specification).Select(t => new QuestionDto
                {
                    Id = t.Id,
                    Body = t.Body,
                    Options = t.Options.Select(o => new OptionDto
                    {
                        Body = o.Body,
                        Index = o.Index,
                        IsCorrect = o.IsCorrect,
                    })
                }).FirstOrDefaultAsync();

                return question is null
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["QuestionNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = question };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageQuestionAsync([NotNull] ManageQuestionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Question>();
                Question? question = null;

                if (requestDto.Id.HasValue)
                {
                    question = await repository.GetAsync(requestDto.Id.Value);
                    if (question is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["QuestionNotFound"] },],
                        };
                    }

                    question.Body = requestDto.Body;
                    question.Options = [.. requestDto.Options.Select(t => new QuestionOption
                    {
                        Body = t.Body,
                        IsCorrect = t.IsCorrect,
                        Index = t.Index,
                    })];

                    _ = repository.Update(question);
                }
                else
                {
                    question = new Question
                    {
                        Body = requestDto.Body,
                        Options = [.. requestDto.Options.Select(t => new QuestionOption
                        {
                            Body = t.Body,
                            IsCorrect = t.IsCorrect,
                            Index = t.Index,
                        })],
                    };
                    repository.Add(question);
                }

                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = question.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> RemoveQuestionAsync([NotNull] ISpecification<Question> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Question>();
                var question = await repository.GetAsync(specification);
                if (question is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["QuestionNotFound"] },],
                    };
                }

                repository.Remove(question);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["QuestionCantBeRemoved"], },] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }
    }
}
