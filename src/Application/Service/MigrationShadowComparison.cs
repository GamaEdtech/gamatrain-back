namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;

    /// <summary>
    /// Compares gama-api's answer with the native source's for the shadow route. It reports where the first difference is
    /// and never what the values are, because an answer can hold a user's data and the report goes to the log.
    /// </summary>
    internal static class MigrationShadowComparison
    {
        /// <summary>Null when both answers are the same; else the place of the first difference (a JSON path, no values).</summary>
        public static string? FirstDifference<T>(ResultData<T> legacy, ResultData<T> native) => legacy.OperationResult != native.OperationResult
            ? $"operationResult: {legacy.OperationResult} != {native.OperationResult}"
            : FirstDifference(JsonSerializer.SerializeToNode(legacy.Data), JsonSerializer.SerializeToNode(native.Data), "$");

        private static string? FirstDifference(JsonNode? legacy, JsonNode? native, string path)
        {
            if (legacy is null || native is null)
            {
                return legacy is null && native is null ? null : $"{path}: null on one side";
            }

            var legacyKind = legacy.GetValueKind();
            var nativeKind = native.GetValueKind();
            return legacyKind != nativeKind
                ? $"{path}: {legacyKind} != {nativeKind}"
                : legacy switch
                {
                    JsonObject legacyObject => FirstDifference(legacyObject, native.AsObject(), path),
                    JsonArray legacyArray => FirstDifference(legacyArray, native.AsArray(), path),
                    _ => ValueDifference(legacy, native, path),
                };
        }

        // Property order is not a difference: the two sides build their objects independently.
        private static string? FirstDifference(JsonObject legacy, JsonObject native, string path)
        {
            foreach (var name in legacy.Select(t => t.Key).Union(native.Select(t => t.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var hasLegacy = legacy.TryGetPropertyValue(name, out var legacyValue);
                var hasNative = native.TryGetPropertyValue(name, out var nativeValue);
                if (hasLegacy != hasNative)
                {
                    return $"{path}.{name}: only in the {(hasLegacy ? "legacy" : "native")} answer";
                }

                var difference = FirstDifference(legacyValue, nativeValue, $"{path}.{name}");
                if (difference is not null)
                {
                    return difference;
                }
            }

            return null;
        }

        // Item order is a difference: lists are shown in the order they are returned.
        private static string? FirstDifference(JsonArray legacy, JsonArray native, string path)
        {
            if (legacy.Count != native.Count)
            {
                return $"{path}: {legacy.Count} items != {native.Count} items";
            }

            for (var i = 0; i < legacy.Count; i++)
            {
                var difference = FirstDifference(legacy[i], native[i], string.Create(CultureInfo.InvariantCulture, $"{path}[{i}]"));
                if (difference is not null)
                {
                    return difference;
                }
            }

            return null;
        }

        private static string? ValueDifference(JsonNode legacy, JsonNode native, string path) =>
            JsonNode.DeepEquals(legacy, native) ? null : $"{path}: value differs";
    }
}
