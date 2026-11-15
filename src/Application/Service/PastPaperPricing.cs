namespace GamaEdtech.Application.Service
{
    using System.Globalization;

    /// <summary>
    /// gama-api's past paper price rule, reproduced exactly (its <c>Test_lib::getPrice</c>, <c>getRefereeScoreFactor</c> and
    /// the English site's <c>standardPrice</c>), so a paper costs the same after the move:
    /// <code>
    /// main file:  round10(base(ext) * sqrt(pages) * factor(refereeScore) / 10 * paper's price multiplier)
    /// extra file: round10(base(ext) * sqrt(length) * factor(refereeScore) / 10 * the extra file type's price multiplier)
    /// </code>
    /// A file type with no base price is free. Pure: the caller passes the copied data in. Nothing calls it until past paper
    /// downloads move here. See docs/business/past-papers.md, "Prices".
    /// </summary>
    public static class PastPaperPricing
    {
        /// <summary>The price of one file.</summary>
        /// <param name="basePrices">Extension (lower case, no dot) to base price, from <c>PastPaperFilePrices</c>.</param>
        /// <param name="extension">The file's extension.</param>
        /// <param name="pages">Pages of a main file; an extra file's <c>Length</c>.</param>
        /// <param name="refereeScore">The paper's <c>RefereeScore</c>.</param>
        /// <param name="multiplier">A main file: the paper's <c>PriceMultiplier</c>. An extra file: its type's
        /// <c>price_multiplier</c> (gama-api's lookup item metadata), 1 when it has none.</param>
        public static int Price(IReadOnlyDictionary<string, int> basePrices, string? extension, int? pages, int refereeScore, decimal multiplier)
        {
            ArgumentNullException.ThrowIfNull(basePrices);
            var ext = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
            if (!basePrices.TryGetValue(ext, out var basePrice) || basePrice == 0)
            {
                return 0;
            }

            // A Word file is never free for a low score: gama-api gives it half the base.
            var factor = RefereeFactor(refereeScore);
            if (factor <= 0 && ext is "doc" or "docx")
            {
                factor = 0.5;
            }

            var price = basePrice * Math.Sqrt(Math.Max(pages ?? 0, 0)) * factor / 10 * (double)multiplier;
            return RoundToTen(price);
        }

        /// <summary>Extension to base price from the copied rows (<c>doc,docx</c> gives both). When two rows name the same
        /// extension the later one wins, as in gama-api's own loop.</summary>
        public static Dictionary<string, int> BasePrices(IEnumerable<(string? Extensions, int BasePrice)> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Dictionary<string, int> prices = new(StringComparer.Ordinal);
            foreach (var (extensions, basePrice) in rows)
            {
                foreach (var ext in (extensions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    prices[ext.ToLowerInvariant()] = basePrice;
                }
            }

            return prices;
        }

        /// <summary>gama-api's referee score factor: D (1) 0, C (2) 1, B (3) 1.5, A and A+ (4, 5) 2, anything else 0.</summary>
        public static double RefereeFactor(int refereeScore) => refereeScore switch
        {
            2 => 1,
            3 => 1.5,
            4 or 5 => 2,
            _ => 0,
        };

        /// <summary>PHP's <c>round($price, -1)</c>: to the nearest ten, halves away from zero.</summary>
        public static int RoundToTen(double price) =>
            (int)(Math.Round(price / 10, MidpointRounding.AwayFromZero) * 10);

        /// <summary>An extra file type's price multiplier from its lookup item's metadata (<c>{"price_multiplier": 0.5}</c>);
        /// 1 when there is none or it can't be read, as on gama-api.</summary>
        public static decimal ExtraTypeMultiplier(string? metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata))
            {
                return 1;
            }

            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(metadata);
                return json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && json.RootElement.TryGetProperty("price_multiplier", out var value)
                    && decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier)
                    ? multiplier
                    : 1;
            }
            catch (System.Text.Json.JsonException)
            {
                return 1;
            }
        }
    }
}
