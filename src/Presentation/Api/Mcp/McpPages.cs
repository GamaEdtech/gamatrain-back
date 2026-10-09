namespace GamaEdtech.Presentation.Api.Mcp
{
    using System.Globalization;
    using System.Net;
    using System.Text;

    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Mcp;

    /// <summary>
    /// The pages the MCP connector serves to a browser: the Gamatrain sign-in page of its OAuth flow, notices, and the full
    /// exam preview (the same data as the in-chat card, with MathJax 2.7 configured like gamatrain.com so formulas look
    /// the way students will see them).
    /// </summary>
    internal static class McpPages
    {
        private const string Head = """
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex">
            """;

        private const string FormStyle = """
            <style>
            :root{--bg:#f4f6fb;--card:#fff;--fg:#1d2433;--muted:#5b6475;--acc:#f28c28;--err:#c62828;--line:#dde2ec}
            @media (prefers-color-scheme:dark){:root{--bg:#12151c;--card:#1b2029;--fg:#e8ebf2;--muted:#a3abbb;--line:#2c3340}}
            body{margin:0;min-height:100vh;display:grid;place-items:center;background:var(--bg);color:var(--fg);font:16px/1.5 system-ui,sans-serif;padding:16px}
            main{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:32px;max-width:380px;width:100%;box-sizing:border-box}
            h1{font-size:22px;margin:0 0 6px} .sub{color:var(--muted);font-size:14px;margin:0 0 20px}
            label{display:block;font-size:14px;margin-bottom:14px} input{display:block;width:100%;box-sizing:border-box;margin-top:6px;padding:10px 12px;border:1px solid var(--line);border-radius:8px;background:transparent;color:inherit;font:inherit}
            button{width:100%;padding:11px;border:0;border-radius:8px;background:var(--acc);color:#fff;font:600 16px system-ui;cursor:pointer}
            .err{color:var(--err);font-size:14px} .app{font-size:15px;padding:10px 12px;border:1px solid var(--line);border-radius:8px;margin:0 0 14px}
            </style></head><body><main>
            """;

        private const string PreviewStyle = """
            <script type="text/x-mathjax-config">
            MathJax.Hub.Config({tex2jax:{inlineMath:[['$','$'],['\\(','\\)']],displayMath:[['$$','$$'],['\\[','\\]']],processEscapes:true}});
            </script>
            <script src="https://cdnjs.cloudflare.com/ajax/libs/mathjax/2.7.9/MathJax.js?config=TeX-MML-AM_CHTML" defer></script>
            <style>
            :root{--bg:#f5f6fa;--card:#fff;--fg:#1d2433;--muted:#5d6678;--line:#e1e5ee;--ok:#2e7d32;--warn:#b26a00;--err:#c62828;--okbg:#e8f5e9}
            @media (prefers-color-scheme:dark){:root{--bg:#11141a;--card:#1a1f28;--fg:#e7eaf0;--muted:#a1a9b8;--line:#2b323e;--okbg:#1f3322}}
            *{box-sizing:border-box} body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.6 system-ui,-apple-system,Segoe UI,sans-serif}
            .wrap{max-width:860px;margin:0 auto;padding:24px 16px 64px}
            h1{font-size:24px;margin:0 0 4px} .sub{color:var(--muted);margin:0 0 20px;font-size:14px}
            dl{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0 0 16px;padding:16px;background:var(--card);border:1px solid var(--line);border-radius:12px}
            dt{font-size:12px;color:var(--muted)} dd{margin:0;font-weight:600}
            .stats{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:24px} .stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:8px 14px;display:flex;gap:8px;align-items:baseline} .stat span{color:var(--muted);font-size:13px}
            .card{background:var(--card);border:1px solid var(--line);border-left:4px solid var(--ok);border-radius:12px;padding:16px 18px;margin-bottom:14px}
            .card.review{border-left-color:var(--warn)} .card.blocked{border-left-color:var(--err)} .card.skipped{opacity:.55;border-left-color:var(--muted)}
            header{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-bottom:8px} .num{font-weight:700} .tags{color:var(--muted);font-size:13px;flex:1}
            .badge{font-size:12px;padding:2px 8px;border-radius:999px;border:1px solid currentColor} .badge.ready{color:var(--ok)} .badge.review{color:var(--warn)} .badge.blocked{color:var(--err)} .badge.skipped{color:var(--muted)}
            .qtext p{margin:.4em 0} img{max-width:100%;height:auto;display:block;margin:8px 0;border:1px solid var(--line);border-radius:6px;background:#fff}
            .opts{list-style:none;padding:0;margin:10px 0;display:grid;gap:6px} .opt{display:flex;gap:10px;align-items:flex-start;padding:6px 10px;border:1px solid var(--line);border-radius:8px}
            .opt p{margin:0} .opt.correct{border-color:var(--ok);background:var(--okbg)} .letter{font-weight:700;min-width:1.2em}
            details{margin-top:10px;padding:8px 12px;border:1px dashed var(--line);border-radius:8px} summary{cursor:pointer;font-weight:600} details p{margin:.3em 0}
            .issues{margin:10px 0 0;padding-left:18px;font-size:14px} .issues .error{color:var(--err)} .issues .review{color:var(--warn)}
            .missing{color:var(--err);font-style:italic}
            </style></head><body><div class="wrap">
            """;

        /// <summary>The sign-in form, posting back the protected <paramref name="request"/>. It shows who is asking
        /// (<paramref name="client"/>: the app's name and the host the browser goes back to), so a teacher can tell an app
        /// they didn't start from (consent phishing).</summary>
        public static string SignIn(string? request, McpAuthorizationRequestDto? client, string? identity, string? error, bool codeRequired)
        {
            var code = codeRequired
                ? """<p class="sub">Gamatrain sent a one-time code to your email or phone. Enter it with your password.</p><label>One-time code<input name="code" inputmode="numeric" autocomplete="one-time-code" required></label>"""
                : string.Empty;
            var host = Uri.TryCreate(client?.RedirectUri, UriKind.Absolute, out var redirect) ? redirect.Authority : null;
            var app = host is null
                ? string.Empty
                : $"""<p class="app"><b>{Encode(client?.ClientName ?? "An app")}</b> wants to add questions and exams to your Gamatrain account. After you sign in you go back to <b>{Encode(host)}</b>. Sign in only if you started this from that app.</p>""";
            return $"""
                {Head}<title>Gamatrain sign in</title>{FormStyle}
                <h1>Sign in to Gamatrain</h1>
                {app}
                <p class="sub">Connect your Gamatrain teacher account to the AI assistant. Your password goes straight to Gamatrain: the assistant never sees it, and it is not stored.</p>
                {(error is null ? string.Empty : $"<p class=\"err\">{Encode(error)}</p>")}
                <form method="post" autocomplete="on"><input type="hidden" name="request" value="{Encode(request)}">
                <label>Email or username<input name="identity" type="text" value="{Encode(identity)}" required autofocus></label>
                <label>Password<input name="password" type="password" required></label>
                {code}
                <button type="submit">Sign in</button>
                </form></main></body></html>
                """;
        }

        public static string Notice(string message) => $"""
            {Head}<title>Gamatrain</title>{FormStyle}
            <h1>Gamatrain</h1><p>{Encode(message)}</p></main></body></html>
            """;

        public static string Preview(ExamImportPreviewDto preview)
        {
            StringBuilder html = new();
            _ = html.Append(CultureInfo.InvariantCulture, $"{Head}<title>Exam preview</title>{PreviewStyle}<h1>{Encode(preview.Title)}</h1>")
                .Append("<p class=\"sub\">Nothing is uploaded to Gamatrain until it is confirmed in the chat.</p><dl>");
            foreach (var detail in preview.Details ?? [])
            {
                _ = html.Append(CultureInfo.InvariantCulture, $"<div><dt>{Encode(detail.Label)}</dt><dd>{(detail.Value is null ? "<span class=\"missing\">not set</span>" : Encode(detail.Value))}</dd></div>");
            }

            var summary = preview.Summary ?? new();
            _ = html.Append("</dl><div class=\"stats\">");
            foreach (var (label, value) in new[] { ("Questions", summary.QuestionsFound), ("Ready", summary.Ready.Count), ("Needs review", summary.NeedsReview.Count), ("Must fix", summary.Blocked.Count), ("Skipped", summary.Skipped.Count) })
            {
                _ = html.Append(CultureInfo.InvariantCulture, $"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>");
            }

            _ = html.Append("</div>");
            foreach (var question in preview.Questions ?? [])
            {
                AppendQuestion(html, question);
            }

            return html.Append(preview.Questions?.Count > 0 ? string.Empty : "<p>No questions yet.</p>").Append("</div></body></html>").ToString();
        }

        private static void AppendQuestion(StringBuilder html, ExamImportPreviewDto.QuestionDto question)
        {
            List<string> tags = [question.Type ?? string.Empty];
            if (question.Marks is { } marks)
            {
                tags.Add(marks == 1 ? "1 mark" : $"{marks} marks");
            }

            if (question.Topic is not null)
            {
                tags.Add(question.Topic);
            }

            // Question and answer HTML come from the service: escaped text with only p, b, u and br tags.
            _ = html.Append(CultureInfo.InvariantCulture, $"<article class=\"card {Encode(question.Status)}\"><header><span class=\"num\">Q{Encode(question.Number)}</span>")
                .Append(CultureInfo.InvariantCulture, $"<span class=\"tags\">{Encode(string.Join(" · ", tags))}</span><span class=\"badge {Encode(question.Status)}\">{Encode(question.StatusLabel)}</span></header>")
                .Append(CultureInfo.InvariantCulture, $"<div class=\"qtext\">{question.Html}</div>{Image(question.Image)}");
            if (question.Options is { Count: > 0 } options)
            {
                _ = html.Append("<ol class=\"opts\">");
                foreach (var option in options)
                {
                    _ = html.Append(CultureInfo.InvariantCulture, $"<li class=\"opt{(option.Correct ? " correct" : string.Empty)}\"><span class=\"letter\">{Encode(option.Letter)}</span><div>{(option.Image is null ? option.Html : Image(option.Image))}</div></li>");
                }

                _ = html.Append("</ol>");
            }

            if (question.AnswerHtml is not null || question.AnswerImage is not null)
            {
                var source = question.AnswerSource switch
                {
                    "markScheme" => " <small>(mark scheme)</small>",
                    "paper" => " <small>(paper)</small>",
                    "ai" => " <small>(AI, please verify)</small>",
                    "user" => " <small>(you)</small>",
                    _ => string.Empty,
                };
                _ = html.Append(CultureInfo.InvariantCulture, $"<details open><summary>Answer{source}</summary>{question.AnswerHtml}{Image(question.AnswerImage)}</details>");
            }

            if (question.Issues is { Count: > 0 } issues)
            {
                _ = html.Append("<ul class=\"issues\">");
                foreach (var issue in issues)
                {
                    _ = html.Append(CultureInfo.InvariantCulture, $"<li class=\"{Encode(issue.Severity)}\">{Encode(issue.Message)}</li>");
                }

                _ = html.Append("</ul>");
            }

            _ = html.Append("</article>");
        }

        private static string Image(Uri? url) => url is null ? string.Empty : $"<img src=\"{Encode(url.AbsoluteUri)}\" alt=\"figure\" loading=\"lazy\">";

        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
