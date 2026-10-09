namespace GamaEdtech.Presentation.Mcp
{
    using System.Net;

    using GamaEdtech.Data.Dto.Mcp;

    /// <summary>
    /// The pages the MCP connector serves to a browser: the Gamatrain sign-in page of its OAuth flow and notices.
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

        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
