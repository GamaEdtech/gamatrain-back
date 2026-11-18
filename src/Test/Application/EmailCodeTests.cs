namespace GamaEdtech.Test.Application
{
    using System.Threading.Tasks;

    using Xunit;

    using static GamaEdtech.Application.Service.EmailCode;

    /// <summary>
    /// The emailed code of signing up and resetting a password (2026-11-18): bound to the purpose and the address, checked without
    /// being used up on the code screen, used up by the last step, and thrown away after too many wrong tries.
    /// </summary>
    public class EmailCodeTests
    {
        private const string Email = "Someone@Example.test";

        [Fact]
        public async Task TheCodeScreenDoesNotUseItUpTheLastStepDoes()
        {
            var cache = AccountDeletionCodeTests.MemoryCache.Create();
            var code = await IssueAsync(cache, Purpose.Register, Email);

            Assert.Matches("^[0-9]{6}$", code);
            Assert.Equal(CheckResult.Valid, await CheckAsync(cache, Purpose.Register, Email, code, useUp: false));
            Assert.Equal(CheckResult.Valid, await CheckAsync(cache, Purpose.Register, Email, code, useUp: true));
            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, Purpose.Register, Email, code, useUp: true));
        }

        [Fact]
        public async Task TheCodeIsBoundToThePurposeAndTheAddressWhateverItsCase()
        {
            var cache = AccountDeletionCodeTests.MemoryCache.Create();
            var code = await IssueAsync(cache, Purpose.Register, Email);

            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, Purpose.ResetPassword, Email, code, useUp: false));
            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, Purpose.Register, "other@example.test", code, useUp: false));
            Assert.Equal(CheckResult.Valid, await CheckAsync(cache, Purpose.Register, " someone@example.TEST ", code, useUp: false));
        }

        [Fact]
        public async Task ANewCodeWaitsAMinute()
        {
            var cache = AccountDeletionCodeTests.MemoryCache.Create();

            Assert.NotNull(await IssueAsync(cache, Purpose.ResetPassword, Email));
            Assert.Null(await IssueAsync(cache, Purpose.ResetPassword, Email));
            Assert.NotNull(await IssueAsync(cache, Purpose.Register, Email));
        }

        [Fact]
        public async Task WrongCodesOnTheCodeScreenCountTowardsTheLimit()
        {
            var cache = AccountDeletionCodeTests.MemoryCache.Create();
            var code = await IssueAsync(cache, Purpose.Register, Email);
            var wrong = code == "000000" ? "111111" : "000000";

            for (var i = 1; i < MaxFailedAttempts; i++)
            {
                Assert.Equal(CheckResult.Wrong, await CheckAsync(cache, Purpose.Register, Email, wrong, useUp: false));
            }

            Assert.Equal(CheckResult.TooManyAttempts, await CheckAsync(cache, Purpose.Register, Email, wrong, useUp: false));
            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, Purpose.Register, Email, code, useUp: true));
        }
    }
}
