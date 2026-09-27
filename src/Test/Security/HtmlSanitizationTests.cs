namespace GamaEdtech.Test.Security
{
    using System;
    using System.Linq;

    using GamaEdtech.Common.Security;

    using Xunit;

    public class HtmlSanitizationTests
    {
        [Theory]
        [InlineData("<div style=\"position:fixed;top:0;left:0;width:100%;height:100%;z-index:9999\">login</div>", "position")]
        [InlineData("<div style=\"pointer-events:none;animation:x 1s\">x</div>", "animation")]
        [InlineData("<div class=\"position-fixed v-overlay\">x</div>", "position-fixed")]
        [InlineData("<a href=\"https://a.example\" target=\"_blank\">x</a>", "target")]
        [InlineData("<button onclick=\"x()\">b</button><select><option>1</option></select><textarea>t</textarea>", "button")]
        [InlineData("<script>alert(1)</script><p>hi</p>", "alert")]
        [InlineData("<img src=x onerror=alert(1)>", "onerror")]
        [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript")]
        [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>", "data:")]
        [InlineData("<svg onload=alert(1)><rect width=\"5\" height=\"5\"/></svg>", "onload")]
        [InlineData("<svg><script>alert(1)</script></svg>", "alert")]
        [InlineData("<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>", "javascript")]
        [InlineData("<svg><foreignObject><iframe src=\"//evil\"></iframe></foreignObject></svg>", "iframe")]
        [InlineData("<svg><animate onbegin=alert(1) attributeName=x></animate></svg>", "onbegin")]
        [InlineData("<iframe src=\"https://evil.example\"></iframe>", "iframe")]
        [InlineData("<object data=\"https://evil.example\"></object><embed src=\"https://evil.example\">", "evil.example")]
        [InlineData("<div style=\"background:url(javascript:alert(1))\">x</div>", "javascript")]
        [InlineData("<div style=\"width:expression(alert(1))\">x</div>", "expression")]
        [InlineData("<form action=\"https://evil.example\"><input name=a></form>", "evil.example")]
        [InlineData("<p id=\"x\" name=\"y\">x</p>", "id=")]
        [InlineData("<img src=\"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=\">", "svg+xml")]
        public void SanitizeHtmlRemovesDangerousMarkup(string input, string mustNotContain)
        {
            var result = input.SanitizeHtml();

            Assert.NotNull(result);
            Assert.DoesNotContain(mustNotContain, result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SanitizeHtmlKeepsSvgDiagramWithViewBox()
        {
            const string input = "<svg viewBox=\"0 0 460 310\" style=\"display:block;height:auto;margin:0 auto;width:100%;\"><rect x=\"10\" y=\"10\" width=\"440\" height=\"290\" fill=\"none\" stroke=\"#24292F\" stroke-width=\"1.8\"></rect><circle cx=\"160\" cy=\"135\" r=\"78\" fill=\"none\" stroke=\"#24292F\"></circle><polyline points=\"70,348 70,30\" fill=\"none\"></polyline><text x=\"68\" y=\"84\" font-size=\"15\" font-family=\"Georgia, serif\" font-style=\"italic\" fill=\"#24292F\">A</text></svg>";

            var result = input.SanitizeHtml();

            Assert.NotNull(result);
            Assert.Contains("viewBox=\"0 0 460 310\"", result, StringComparison.Ordinal);
            Assert.Contains("<rect", result, StringComparison.Ordinal);
            Assert.Contains("<circle", result, StringComparison.Ordinal);
            Assert.Contains("<polyline points=\"70,348 70,30\"", result, StringComparison.Ordinal);
            Assert.Contains("font-family=\"Georgia, serif\"", result, StringComparison.Ordinal);
            Assert.Contains(">A</text>", result, StringComparison.Ordinal);
            Assert.Contains("width: 100%", result, StringComparison.Ordinal);
        }

        [Fact]
        public void SanitizeHtmlKeepsFormulasAndExamLayout()
        {
            const string input = "<div style=\"color:#24292F;font-family:'DejaVu Sans',Verdana,Arial,sans-serif;font-size:14.5px;line-height:1.4;\">"
                + "<figure class=\"table\" style=\"width:100%;\"><table style=\"border-collapse:collapse;\"><tbody><tr>"
                + "<td style=\"background:#F5F7F8;border:1px solid #E4E7EC;border-left:7px solid #24292F;border-radius:8px 0 0 8px;padding:10px 14px;vertical-align:top;\">"
                + "<p style=\"margin:0;\"><strong>Question 1</strong></p></td>"
                + "<td style=\"text-align:right;white-space:nowrap;\"><span style=\"display:inline-block;border-radius:99px;\">6 marks</span><br></td></tr></tbody></table></figure>"
                + "<div style=\"overflow-x:auto;width:100%;\"><p style=\"margin:6px auto;text-align:center;width:max-content;\"><span class=\"math-tex\">\\[\\mathrm{P}(T &lt; 3) = 0.0616\\]</span></p></div>"
                + "<p>Find <span class=\"math-tex\">\\(\\mathrm{P}(A \\mid B')\\)</span> &amp; <span class=\"math-tex\">\\(p\\)</span></p></div>";

            var result = input.SanitizeHtml();

            Assert.NotNull(result);
            Assert.Contains("class=\"math-tex\"", result, StringComparison.Ordinal);
            Assert.Contains("\\[\\mathrm{P}(T &lt; 3) = 0.0616\\]", result, StringComparison.Ordinal);
            Assert.Contains("\\(\\mathrm{P}(A \\mid B')\\)", result, StringComparison.Ordinal);
            Assert.Contains("border-left: 7px solid", result, StringComparison.Ordinal);
            Assert.Contains("border-radius: 8px 0 0 8px", result, StringComparison.Ordinal);
            Assert.Contains("border-collapse: collapse", result, StringComparison.Ordinal);
            Assert.Contains("white-space: nowrap", result, StringComparison.Ordinal);
            Assert.Contains("width: max-content", result, StringComparison.Ordinal);
            Assert.Contains("overflow-x: auto", result, StringComparison.Ordinal);
            Assert.Contains("vertical-align: top", result, StringComparison.Ordinal);
            Assert.Contains("<figure class=\"table\"", result, StringComparison.Ordinal);
        }

        [Fact]
        public void SanitizeHtmlKeepsEditorClassesOnly()
        {
            var result = "<figure class=\"table\"><span class=\"math-tex text-big evil-class\">x</span></figure><figure class=\"media image-style-side\"></figure>".SanitizeHtml();

            Assert.NotNull(result);
            Assert.Contains("class=\"table\"", result, StringComparison.Ordinal);
            Assert.Contains("math-tex", result, StringComparison.Ordinal);
            Assert.Contains("text-big", result, StringComparison.Ordinal);
            Assert.Contains("image-style-side", result, StringComparison.Ordinal);
            Assert.DoesNotContain("evil-class", result, StringComparison.Ordinal);
        }

        [Fact]
        public void SanitizeHtmlKeepsPastedRasterImageButNotOtherDataUrls()
        {
            var result = "<img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"x\">".SanitizeHtml();

            Assert.NotNull(result);
            Assert.Contains("src=\"data:image/png;base64,iVBORw0KGgo=\"", result, StringComparison.Ordinal);
        }

        [Fact]
        public void SanitizeHtmlKeepsSafeLinksAndMediaEmbed()
        {
            var result = ("<a href=\"https://example.com/a?b=1\">ok</a> <a href=\"mailto:a@b.co\">m</a>"
                + "<figure class=\"media\"><oembed url=\"https://www.youtube.com/watch?v=abc\"></oembed></figure>").SanitizeHtml();

            Assert.NotNull(result);
            Assert.Contains("href=\"https://example.com/a?b=1\"", result, StringComparison.Ordinal);
            Assert.Contains("href=\"mailto:a@b.co\"", result, StringComparison.Ordinal);
            Assert.Contains("<oembed url=\"https://www.youtube.com/watch?v=abc\">", result, StringComparison.Ordinal);
        }

        [Fact]
        public void SanitizeHtmlDropsNonHttpMediaEmbedUrl()
        {
            var result = "<oembed url=\"javascript:alert(1)\"></oembed>".SanitizeHtml();

            Assert.NotNull(result);
            Assert.DoesNotContain("javascript", result, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("plain text with no markup")]
        [InlineData("Tom & Jerry, a > b is fine")]
        public void SanitizeHtmlLeavesTextWithoutTagsUntouched(string? input) => Assert.Equal(input, input.SanitizeHtml());

        [Theory]
        [InlineData("<script>alert(1)</script>Title", "Title")]
        [InlineData("<b>Bold</b> title", "Bold title")]
        [InlineData("Fish &amp; Chips", "Fish &amp; Chips")]
        [InlineData("<img src=x onerror=alert(1)>Hello", "Hello")]
        public void SanitizePlainTextStripsTags(string input, string expected) => Assert.Equal(expected, input.SanitizePlainText());

        [Theory]
        [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;")]
        [InlineData("<<script>script>alert(1)<</script>/script>")]
        public void SanitizePlainTextNeverReturnsAngleBrackets(string input)
        {
            var result = input.SanitizePlainText();

            Assert.NotNull(result);
            Assert.DoesNotContain('<', result);
            Assert.DoesNotContain('>', result);
        }

        [Fact]
        public void SanitizeHtmlIsSafeToCallConcurrently()
        {
            const string input = "<p onclick=\"x()\">a</p><script>b</script><svg viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"></rect></svg>";

            var results = Enumerable.Range(0, 200).AsParallel().Select(_ => input.SanitizeHtml()).ToList();

            Assert.All(results, r => Assert.Equal(results[0], r));
            Assert.DoesNotContain("onclick", results[0], StringComparison.Ordinal);
        }
    }
}
