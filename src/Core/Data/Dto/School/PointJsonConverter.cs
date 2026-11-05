namespace GamaEdtech.Data.Dto.School
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    using NetTopologySuite;
    using NetTopologySuite.Geometries;

    /// <summary>Stores a coordinate as [longitude, latitude]; System.Text.Json cannot round-trip a Point on its own.</summary>
    public sealed class PointJsonConverter : JsonConverter<Point>
    {
        public override Point? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<double[]>(ref reader, options);
            return values is { Length: 2 } ? NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePoint(new Coordinate(values[0], values[1])) : null;
        }

        public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            JsonSerializer.Serialize(writer, new[] { value.X, value.Y }, options);
        }
    }
}
