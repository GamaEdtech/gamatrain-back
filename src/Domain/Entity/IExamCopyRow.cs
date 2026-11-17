namespace GamaEdtech.Domain.Entity
{
    /// <summary>A row the exam copy mirrors from gama-api by its id (the entity's own <c>Id</c>).</summary>
    public interface IExamCopyRow
    {
        long ExamId { get; }
    }

    /// <summary>A copied row that gama-api stamps with a change date, so only changed rows are read again.</summary>
    public interface IExamCopyStampedRow
    {
        DateTimeOffset? LastModifyDate { get; set; }
    }
}
