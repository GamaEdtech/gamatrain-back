namespace GamaEdtech.Domain.Services.FaqDomainServices
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.DataAccess.Mappers.FaqMappers;
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Repositories.ClassificationNodes;
    using GamaEdtech.Domain.Repositories.Faq;
    using GamaEdtech.Domain.Specification.ClassificationNodeSpecs;
    using GamaEdtech.Domain.Specification.FaqSpecs;

    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class FaqDomainService
        (
            IClassificationNodeRepository classificationNodeRepository,
            IFaqRepository faqRepository
        )
        : IFaqDomainService
    {
        public async Task<IEnumerable<FaqResponse>> GetFaqWithDynamicFilterAsync([NotNull] GetFaqWithDynamicFilterRequest dynamicFilterRequest, CancellationToken cancellationToken)
        {
            var faqList = await faqRepository.ListAsync(new GetFaqWithDynamicFilterSpecification(dynamicFilterRequest,
                FaqRelations.ClassificationNodeRelationships, FaqRelations.Media), cancellationToken);

            return faqList.MapToResult();
        }

        public async Task<FaqResponse> CreateFaqAsync(IEnumerable<string> classificationNodes, string summaryOfQuestion, string question,
            UploadFileResponse uploadFileResult, CancellationToken cancellationToken)
        {
            var fetchedClassificationNodes = await classificationNodeRepository.ListAsync
                (new GetClassificationNodeWithTitlesSpecification(classificationNodes), cancellationToken);

            if (fetchedClassificationNodes.Count == 0)
            {
                throw new ArgumentException("");
            }

            var faq = Faq.Create(summaryOfQuestion, question, fetchedClassificationNodes);

            if (uploadFileResult is not null && uploadFileResult.FileResults != null
                && uploadFileResult.FileResults.Any())
            {
                faq.AddMedia(
                       uploadFileResult.FileResults.Select
                       (file => Media.Create(file.FileName, file.FileAddress, MediaEntity.Faq, MediaType.Photo, faq.Id,
                   file.ContentType))
                );
            }

            _ = await faqRepository.AddAsync(faq, cancellationToken);
            return faq.MapToResult();
        }

        public async Task AddFaqRelationShipAsync(IEnumerable<Guid> classificationNodeIds, Guid faqId, CancellationToken cancellationToken)
        {
            var faq = await faqRepository.FirstOrDefaultAsync(new GetFaqWithDynamicFilterSpecification(
            new GetFaqWithDynamicFilterRequest
            {
                FaqId = faqId,
            }, FaqRelations.ClassificationNodeRelationships), cancellationToken) ??
            throw new ArgumentException("");

            var classificationNodes = await classificationNodeRepository.ListAsync(
                new GetClassificationNodeByIdSpecification(classificationNodeIds), cancellationToken)
                ?? throw new ArgumentException("");

            faq.AddRelationShip(classificationNodes);
            _ = await faqRepository.UpdateAsync(faq, cancellationToken);
        }
    }
}
