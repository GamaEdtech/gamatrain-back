namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Domain.Enumeration;

    using Xunit;

    /// <summary>Pure tests of the exam export price rule: question count x the format's multiplier, rounded up.</summary>
    public class ExamExportPricingTests
    {
        [Fact]
        public void DefaultsApplyWhenMultipliersWereNeverSet()
        {
            var settings = new ApplicationSettingsDto();

            Assert.Equal(40, ExamExportPricing.Price(settings, ExportFileType.Pdf, 40));
            Assert.Equal(80, ExamExportPricing.Price(settings, ExportFileType.Word, 40));
            Assert.Equal(100, ExamExportPricing.Price(settings, ExportFileType.PowerPoint, 40));
        }

        [Fact]
        public void FractionalPriceRoundsUp() =>
            Assert.Equal(103, ExamExportPricing.Price(new ApplicationSettingsDto(), ExportFileType.PowerPoint, 41)); // 41 x 2.5 = 102.5

        [Fact]
        public void AdminSetMultiplierWins()
        {
            var settings = new ApplicationSettingsDto { ExamExportPdfMultiplier = 1.5m, ExamExportWordMultiplier = 0m };

            Assert.Equal(15, ExamExportPricing.Price(settings, ExportFileType.Pdf, 10));
            Assert.Equal(0, ExamExportPricing.Price(settings, ExportFileType.Word, 10));
            Assert.Equal(25, ExamExportPricing.Price(settings, ExportFileType.PowerPoint, 10));
        }

        [Fact]
        public void ThumbnailAndEmptyExamsAreFree()
        {
            var settings = new ApplicationSettingsDto();

            Assert.Equal(0, ExamExportPricing.Price(settings, ExportFileType.Thumbnail, 40));
            Assert.Null(ExamExportPricing.Multiplier(settings, ExportFileType.Thumbnail));
            Assert.Equal(0, ExamExportPricing.Price(settings, ExportFileType.Word, 0));
        }
    }
}
