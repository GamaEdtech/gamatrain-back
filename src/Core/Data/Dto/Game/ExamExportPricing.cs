namespace GamaEdtech.Data.Dto.Game
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// Exam export pricing (2026-09-27), the one place its rule lives: an export costs the exam's question count x
    /// its format's multiplier (admin-editable in ApplicationSettings), rounded up. The thumbnail is free.
    /// </summary>
    public static class ExamExportPricing
    {
        public const decimal DefaultPdfMultiplier = 1m;
        public const decimal DefaultWordMultiplier = 2m;
        public const decimal DefaultPowerPointMultiplier = 2.5m;

        /// <summary>The format's multiplier -- the stored setting, or its default when never set; <see langword="null"/>
        /// for a free format (the thumbnail).</summary>
        public static decimal? Multiplier([NotNull] ApplicationSettingsDto settings, ExportFileType? fileType) => fileType switch
        {
            _ when fileType == ExportFileType.Pdf => settings.ExamExportPdfMultiplier ?? DefaultPdfMultiplier,
            _ when fileType == ExportFileType.Word => settings.ExamExportWordMultiplier ?? DefaultWordMultiplier,
            _ when fileType == ExportFileType.PowerPoint => settings.ExamExportPowerPointMultiplier ?? DefaultPowerPointMultiplier,
            _ => null,
        };

        /// <summary>The price in points (and quota units): <paramref name="questionCount"/> x the format's multiplier,
        /// rounded up; 0 for a free format or an exam without questions.</summary>
        public static long Price([NotNull] ApplicationSettingsDto settings, ExportFileType? fileType, int questionCount) =>
            Multiplier(settings, fileType) is { } multiplier && questionCount > 0
                ? (long)Math.Ceiling(questionCount * multiplier)
                : 0;
    }
}
