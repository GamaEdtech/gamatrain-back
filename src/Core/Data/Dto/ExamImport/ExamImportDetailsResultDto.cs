namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>What setting the exam details came to: the details (checked, or saved on the draft), or what the user
    /// decides first.</summary>
    public sealed class ExamImportDetailsResultDto
    {
        /// <summary>The details as they will be saved, or the draft they were saved on, with the subject's topics. Null
        /// while the user decides.</summary>
        public ExamImportDraftDto? Details { get; set; }

        /// <summary>A required detail left out, or not valid under its parent, for the user to give: board, grade, course,
        /// subject, paper (picked from <see cref="Options"/>) or duration (typed).</summary>
        public string? Pick { get; set; }

        /// <summary>gama-api's choices for <see cref="Pick"/>.</summary>
        public IReadOnlyList<ExamImportOptionDto>? Options { get; set; }

        /// <summary>The unpublished draft the user already has, as gama-api keeps one at a time: continue it, or discard it
        /// to create this one.</summary>
        public ExamImportDraftDto? ExistingDraft { get; set; }
    }
}
