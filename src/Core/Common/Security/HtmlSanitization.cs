namespace GamaEdtech.Common.Security
{
    using System.Net;
    using System.Text.RegularExpressions;

    using AngleSharp.Dom;

    using Ganss.Xss;

    /// <summary>
    /// Cleans user-written HTML once, when it is saved, so nothing that renders it later (the frontend uses
    /// <c>v-html</c> in many places) can be made to run script.
    /// </summary>
    /// <remarks>
    /// The sanitizers are built once and shared: <c>Sanitize</c> is thread-safe as long as the configuration is not
    /// changed afterwards, and building one is far more expensive than using it. Input without a '&lt;' cannot contain a tag, so it is returned untouched without being parsed.
    /// </remarks>
    public static partial class HtmlSanitization
    {
        // Shapes and text of an inline SVG diagram. No <script>, <style>, <use>, <image>, <foreignObject> or animation
        // elements: those are how script gets into an SVG.
        private static readonly string[] SvgTags =
        [
            "svg", "g", "defs", "rect", "circle", "ellipse", "line", "polyline", "polygon", "path", "text", "tspan", "title", "desc",
        ];

        private static readonly string[] SvgAttributes =
        [
            "viewBox", "preserveAspectRatio", "xmlns", "x", "y", "dx", "dy", "width", "height", "cx", "cy", "r", "rx", "ry",
            "x1", "y1", "x2", "y2", "points", "d", "transform", "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width",
            "stroke-dasharray", "stroke-linecap", "stroke-linejoin", "stroke-opacity", "opacity", "font-size", "font-family",
            "font-style", "font-weight", "text-anchor", "dominant-baseline", "letter-spacing",
        ];

        // Properties the exam layout relies on (tables, boxes, badges); the library's default list does not cover them all.
        private static readonly string[] ExtraCssProperties =
        [
            "border-collapse", "border-spacing", "border-radius", "border-left", "border-right", "border-top", "border-bottom",
            "border-left-width", "border-left-style", "border-left-color", "border-top-width", "border-top-style", "border-top-color",
            "overflow-x", "overflow-y", "white-space", "min-width", "max-width", "min-height", "max-height", "line-height",
            "vertical-align", "text-align", "display", "letter-spacing", "word-break", "list-style-type", "table-layout",
        ];

        private const int MaxPlainTextLengthToParse = 10_000;

        // Removed from the library's defaults: form controls (a <form action="https://evil"> is a phishing form), image
        // maps, and menus. Nothing in a post or a message needs them.
        private static readonly string[] BlockedTags =
        [
            "form", "input", "button", "select", "textarea", "option", "optgroup", "datalist", "fieldset", "legend", "label",
            "keygen", "output", "meter", "progress", "area", "map", "menu", "menuitem",
        ];

        private static readonly string[] BlockedAttributes =
        [
            "id", "name", "action", "method", "enctype", "accept", "accept-charset", "autocomplete", "autosave", "accesskey",
            "contenteditable", "draggable", "dropzone", "challenge", "keytype", "list", "placeholder", "prompt", "readonly",
            "required", "disabled", "checked", "selected", "multiple", "for", "tabindex", "usemap", "ismap",
            // target="_blank" without rel="noopener" lets the opened page take over the original tab.
            "target",
        ];

        // Positioning and animation let injected content paint a fake overlay or login box over the real page.
        private static readonly string[] BlockedCssProperties =
        [
            "position", "z-index", "top", "right", "bottom", "left", "pointer-events", "cursor", "content", "filter", "resize",
            "user-select", "clip", "background-image", "animation", "animation-delay", "animation-direction", "animation-duration",
            "animation-fill-mode", "animation-iteration-count", "animation-name", "animation-play-state",
            "animation-timing-function", "transition", "transition-delay", "transition-duration", "transition-property",
            "transition-timing-function", "mask", "mask-clip", "mask-composite", "mask-image", "mask-mode", "mask-origin",
            "mask-position", "mask-repeat", "mask-size", "mask-type",
        ];

        // Declared after the lists above on purpose: static fields initialize in textual order, and building a sanitizer
        // reads them.
        private static readonly HtmlSanitizer RichSanitizer = CreateRichSanitizer();
        private static readonly HtmlSanitizer TextSanitizer = CreateTextSanitizer();

        /// <summary>
        /// Rich HTML (post bodies, ticket messages): keeps formatting, tables, images, inline SVG diagrams and
        /// <c>class</c>/<c>style</c>; removes scripts, event handlers, <c>javascript:</c> links, frames and objects.
        /// </summary>
        public static string? SanitizeHtml(this string? html) => string.IsNullOrEmpty(html) || !html.Contains('<', StringComparison.Ordinal) ? html : RichSanitizer.Sanitize(html);

        /// <summary>
        /// Plain text (titles, summaries, subjects): strips every tag and returns text with no angle brackets, so it is
        /// safe whether the frontend prints it escaped or through <c>v-html</c>.
        /// </summary>
        public static string? SanitizePlainText(this string? text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains('<', StringComparison.Ordinal))
            {
                return text;
            }

            // A title or subject is short. Anything this long is not one, so skip the parsing and just drop the brackets.
            if (text.Length > MaxPlainTextLengthToParse)
            {
                return RemoveAngleBrackets(text);
            }

            // The sanitizer keeps the text inside removed tags, which is right for <b> but would leave a script's code
            // behind as visible text, so those blocks are dropped whole first.
            try
            {
                return RemoveAngleBrackets(WebUtility.HtmlDecode(TextSanitizer.Sanitize(ScriptOrStyleBlock().Replace(text, string.Empty))));
            }
            catch (RegexMatchTimeoutException)
            {
                return RemoveAngleBrackets(text);
            }
        }

        private static string RemoveAngleBrackets(string text) => text.Replace("<", string.Empty, StringComparison.Ordinal).Replace(">", string.Empty, StringComparison.Ordinal);

        private static HtmlSanitizer CreateRichSanitizer()
        {
            var sanitizer = new HtmlSanitizer();

            foreach (var tag in SvgTags)
            {
                _ = sanitizer.AllowedTags.Add(tag);
            }

            // CKEditor's media embed writes <oembed url="https://...">, which the frontend turns into a player.
            _ = sanitizer.AllowedTags.Add("oembed");
            _ = sanitizer.AllowedAttributes.Add("url");
            _ = sanitizer.UriAttributes.Add("url");

            foreach (var attribute in SvgAttributes)
            {
                _ = sanitizer.AllowedAttributes.Add(attribute);
            }

            foreach (var property in ExtraCssProperties)
            {
                _ = sanitizer.AllowedCssProperties.Add(property);
            }

            foreach (var tag in BlockedTags)
            {
                _ = sanitizer.AllowedTags.Remove(tag);
            }

            // An id or name also lets injected markup shadow a global (DOM clobbering); nothing here needs either.
            foreach (var attribute in BlockedAttributes)
            {
                _ = sanitizer.AllowedAttributes.Remove(attribute);
            }

            foreach (var property in BlockedCssProperties)
            {
                _ = sanitizer.AllowedCssProperties.Remove(property);
            }

            _ = sanitizer.AllowedSchemes.Add("mailto");
            _ = sanitizer.AllowedSchemes.Add("tel");

            // `class` is not an allowed attribute by default. Allow it, then keep only the class names the editor itself
            // produces (tables, media, image alignment/resizing, highlight and text-size marks, code language) and the
            // formula marker the frontend typesets - never arbitrary site classes, which could be used to restyle or
            // spoof the page.
            _ = sanitizer.AllowedAttributes.Add("class");
            sanitizer.PostProcessNode += (sender, args) =>
            {
                if (args.Node is not IElement element || !element.HasAttribute("class"))
                {
                    return;
                }

                var kept = element.ClassList.Where(name => AllowedClass().IsMatch(name)).ToArray();
                if (kept.Length == 0)
                {
                    _ = element.RemoveAttribute("class");
                }
                else
                {
                    element.SetAttribute("class", string.Join(' ', kept));
                }
            };

            // Images pasted or uploaded in the editor are stored inline as data URIs. Allow exactly that - raster
            // images on an <img> - and no other data: URL anywhere (a data:text/html link is a classic XSS vector).
            sanitizer.FilterUrl += (_, args) =>
            {
                if (string.Equals(args.Tag.LocalName, "img", StringComparison.OrdinalIgnoreCase)
                    && DataImageUrl().IsMatch(args.OriginalUrl))
                {
                    args.SanitizedUrl = args.OriginalUrl;
                }
            };

            return sanitizer;
        }

        private static HtmlSanitizer CreateTextSanitizer()
        {
            var sanitizer = new HtmlSanitizer
            {
                KeepChildNodes = true,
            };
            sanitizer.AllowedTags.Clear();
            sanitizer.AllowedAttributes.Clear();
            sanitizer.AllowedCssProperties.Clear();
            sanitizer.AllowedSchemes.Clear();

            return sanitizer;
        }

        [GeneratedRegex(@"^(?:math-tex|table|media|image|image_resized|image-style-[a-z-]+|text-(?:tiny|small|big|huge)|marker-[a-z]+|pen-[a-z]+|language-[a-z0-9+#-]+)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
        private static partial Regex AllowedClass();

        [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
        private static partial Regex ScriptOrStyleBlock();

        [GeneratedRegex(@"^data:image/(?:png|jpe?g|gif|webp|avif|bmp);base64,[A-Za-z0-9+/=\s]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
        private static partial Regex DataImageUrl();
    }
}
