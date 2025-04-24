namespace GamaEdtech.Data.Dto.FaqManager
{
    using GamaEdtech.Domain.Entity;

    public class CreateFaqCategoryDto
    {
        #region Properties
        public string? ParentCategoryTitle { get; init; }
        public required string Title { get; init; }
        public required FaqCategoryType FaqCategoryType { get; init; }
        #endregion
    }

    public class FaqCategorySelectedDto
    {
        public required string Title { get; init; }
        public required FaqCategoryType FaqCategoryType { get; init; }
    }

}
