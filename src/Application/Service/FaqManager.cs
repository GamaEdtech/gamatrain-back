namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Data.Mapping.Media;
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Data.Mapping.FaqMapping;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;
    using GamaEdtech.Domain.Services.FaqDomainServices;
    using GamaEdtech.Common.DataAnnotation;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class FaqManager(IFaqDomainService faqDomainService, IFileManager fileManager) : IFaqManager
    {
        public Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync(GetFaqWithDynamicFilterDto getFaqWithDynamicFilterDTO, CancellationToken cancellationToken)
            => faqDomainService.GetFaqWithDynamicFilterAsync(getFaqWithDynamicFilterDTO.MapToRequest(), cancellationToken);
        public async Task<FaqResponse> AddForumAsync([NotNull] CreateForumDto createForumDTO, CancellationToken cancellationToken)
        {
            var mediaResult = new UploadFileResponse();
            if (createForumDTO.AttachFiles is not null && createForumDTO.AttachFiles.Any())
            {
                var uploadResult = await fileManager.UploadFilesAsync
                    (await createForumDTO.AttachFiles.MapToUploadFileRequestAsync(), "Content", cancellationToken);

                mediaResult = uploadResult.GetUploaderFileResultsAllUploaded()?.UploadFileResult ??
                    throw new ArgumentException("");
            }

            return await faqDomainService.CreateFaqAsync(createForumDTO.FaqCategoryTitles,
                createForumDTO.SummaryOfQuestion, createForumDTO.Question,
                mediaResult, cancellationToken);
        }
    }
}
