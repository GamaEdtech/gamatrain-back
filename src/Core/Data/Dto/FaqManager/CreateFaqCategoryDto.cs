namespace GamaEdtech.Data.Dto.FaqManager
{
    using GamaEdtech.Domain.Entity;

    public class CreateFaqCategoryDto
    {
        #region Properties
        public string[]? ParentCategoryTitles { get; init; }
        public required string Title { get; init; }
        public required ClassificationNodeType FaqCategoryType { get; init; }
        #endregion
    }

    public class FaqCategorySelectedDto
    {
        public required string Title { get; init; }
        public required ClassificationNodeType FaqCategoryType { get; init; }
    }

}
