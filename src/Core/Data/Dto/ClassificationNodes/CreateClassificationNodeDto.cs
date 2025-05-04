namespace GamaEdtech.Data.Dto.ClassificationNodes
{
    using GamaEdtech.Domain.Entity;

    public class CreateClassificationNodeDto
    {
        #region Properties
        public string[]? ParentCategoryTitles { get; init; }
        public required string Title { get; init; }
        public required ClassificationNodeType FaqCategoryType { get; init; }
        #endregion
    }

    public class CreateClassificationSelectedDto
    {
        public required string Title { get; init; }
        public required ClassificationNodeType FaqCategoryType { get; init; }
    }

}
