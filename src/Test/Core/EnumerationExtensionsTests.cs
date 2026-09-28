namespace GamaEdtech.Test.Core
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Domain.Enumeration;

    using Xunit;

    /// <summary>Pure in-memory tests of <see cref="EnumerationExtensions.TryGetFromNameOrValue{TEnum, TKey}"/>.</summary>
    public class EnumerationExtensionsTests
    {
        [Theory]
        [InlineData("4")]
        [InlineData("Annual")]
        [InlineData("annual")]
        public void ByteKeyedEnumParsesFromNameOrNumericValue(string input)
        {
            Assert.True(input.TryGetFromNameOrValue<BillingInterval, byte>(out var result));
            Assert.Equal(BillingInterval.Annual, result);
        }

        [Theory]
        [InlineData("300")]
        [InlineData("-1")]
        [InlineData("99")]
        [InlineData("NotAnInterval")]
        [InlineData("")]
        [InlineData(null)]
        public void UnknownOrOutOfRangeInputIsNoMatchNotAnException(string? input)
        {
            Assert.False(input.TryGetFromNameOrValue<BillingInterval, byte>(out var result));
            Assert.Null(result);
        }
    }
}
