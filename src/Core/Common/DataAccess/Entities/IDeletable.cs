namespace GamaEdtech.Common.DataAccess.Entities
{
    using GamaEdtech.Common.DataAnnotation.Schema;

    public interface IDeletable
    {
        [NotMapped]
        bool IsDeleted { get; set; }
    }
}
