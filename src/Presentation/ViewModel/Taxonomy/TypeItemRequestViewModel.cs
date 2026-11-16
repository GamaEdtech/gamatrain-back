namespace GamaEdtech.Presentation.ViewModel.Taxonomy
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>A lookup item (board, grade, subject, topic, classification...) an admin adds or edits.</summary>
    public sealed class TypeItemRequestViewModel
    {
        /// <summary>The list: 1 board, 2 grade, 4 subject, 5 course, 6 classification, 11 topic, 20 extra file type,
        /// 22 paper, 29 variant.</summary>
        [Display]
        [Required]
        public short? Kind { get; set; }

        [Display]
        [Required]
        [StringLength(500)]
        public string? Title { get; set; }

        [Display]
        public int ListOrder { get; set; }

        /// <summary>The parents, "id+id" (a subject's are "course,grade").</summary>
        [Display]
        [StringLength(1000)]
        public string? Parent { get; set; }

        [Display]
        [StringLength(100)]
        public string? Parent2 { get; set; }

        /// <summary>A JSON object, e.g. <c>{"is_paper":true}</c> or <c>{"season":true}</c>.</summary>
        [Display]
        [StringLength(4000)]
        public string? Metadata { get; set; }
    }
}
