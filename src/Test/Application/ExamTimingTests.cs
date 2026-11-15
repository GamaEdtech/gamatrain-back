namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Data.Dto.Exam;

    using Xunit;

    public class ExamTimingTests
    {
        [Theory]
        [InlineData(725, 45, 725)]
        [InlineData(2820, 45, 2820)]
        [InlineData(12761, 45, 161)]
        [InlineData(28290, 60, null)]
        [InlineData(0, 45, null)]
        [InlineData(725, null, null)]
        public void GamaApiTimeIsKeptOnlyWhenBelievable(int? seconds, int? minutes, int? expected) =>
            Assert.Equal(expected, ExamTiming.FromGamaApi(seconds, minutes));

        [Theory]
        [InlineData(3090, 60, 3090)]
        [InlineData(9000, 60, 3600)]
        [InlineData(-5, 60, null)]
        [InlineData(null, 60, null)]
        [InlineData(500, null, 500)]
        public void StudentTimeIsNeverMoreThanTheDuration(int? seconds, int? minutes, int? expected) =>
            Assert.Equal(expected, ExamTiming.FromStudent(seconds, minutes));
    }
}
