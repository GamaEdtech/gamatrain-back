namespace GamaEdtech.Test.Application
{
    using System;
    using System.Linq;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.Ticket;

    using Xunit;

    public class TicketAiReplyServiceTests
    {
        [Fact]
        public void ToPlainTextKeepsParagraphsAndDecodesEntities()
        {
            var text = TicketAiReplyService.ToPlainText("<p>Hello&nbsp;there,</p><p>I can&#39;t <b>download</b> &amp; pay.</p><ul><li>One</li><li>Two</li></ul>");

            Assert.Equal("Hello there,\nI can't download & pay.\nOne\nTwo", text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ToPlainTextReturnsEmptyForBlankInput(string? html) => Assert.Equal(string.Empty, TicketAiReplyService.ToPlainText(html));

        [Theory]
        [InlineData("```html\n<p>Hi</p>\n```", "<p>Hi</p>")]
        [InlineData("```\n<p>Hi</p>```", "<p>Hi</p>")]
        [InlineData("  <p>Hi</p>  ", "<p>Hi</p>")]
        public void StripCodeFenceRemovesMarkdownFence(string input, string expected) => Assert.Equal(expected, TicketAiReplyService.StripCodeFence(input));

        [Fact]
        public void BuildTicketContextLabelsWhoWroteEachReply()
        {
            var context = string.Join('\n', TicketAiReplyService.BuildTicketContext(
                new() { FullName = "Sara", Email = "sara@example.com", Subject = "Cancel", Body = "<p>How do I cancel?</p>" },
                [
                    new TicketReplyDto { BySupport = true, Body = "<p>Which plan?</p>", CreationDate = DateTimeOffset.UnixEpoch },
                    new TicketReplyDto { BySupport = false, Body = "Monthly", CreationDate = DateTimeOffset.UnixEpoch },
                ]));

            Assert.Contains("Customer name: Sara", context, StringComparison.Ordinal);
            Assert.Contains("Original message: How do I cancel?", context, StringComparison.Ordinal);
            Assert.Contains("From: Support team\nDate: 1970-01-01 00:00\nBody: Which plan?", context, StringComparison.Ordinal);
            Assert.Contains("From: Customer\nDate: 1970-01-01 00:00\nBody: Monthly", context, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildTicketContextHandlesMissingFields()
        {
            var lines = TicketAiReplyService.BuildTicketContext(new(), []).ToList();

            Assert.Contains("Customer name: Unknown", lines);
            Assert.Contains("Subject: No subject", lines);
            Assert.Contains("Original message: No body", lines);
            Assert.Equal("No previous replies.", lines[^1]);
        }

        [Fact]
        public void HandbookIsEmbedded()
        {
            using var stream = typeof(TicketAiReplyService).Assembly.GetManifestResourceStream("SupportHandbook.md");

            Assert.NotNull(stream);
        }
    }
}
