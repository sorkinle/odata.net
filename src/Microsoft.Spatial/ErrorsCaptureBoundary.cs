namespace Microsoft.Spatial
{
    using System;
    using System.IO;
    using SpecGate.Annotations;

    // Enum constants and exception constructors are not SpecOperation targets.
    // These observations supplement them; they do not replace their public APIs.
    internal static class ErrorsCaptureBoundary
    {
        [SpecOperation("spatial_type_identity", Spec = "spatial.errors_and_spatial_type")]
        public static SpatialTypeObservation ObserveSpatialType(string name)
        {
            SpatialType value;
            switch (name)
            {
                case "Unknown": value = SpatialType.Unknown; break;
                case "Point": value = SpatialType.Point; break;
                case "LineString": value = SpatialType.LineString; break;
                case "Polygon": value = SpatialType.Polygon; break;
                case "MultiPoint": value = SpatialType.MultiPoint; break;
                case "MultiLineString": value = SpatialType.MultiLineString; break;
                case "MultiPolygon": value = SpatialType.MultiPolygon; break;
                case "Collection": value = SpatialType.Collection; break;
                case "FullGlobe": value = SpatialType.FullGlobe; break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "No native SpatialType mapping exists.");
            }

            return new SpatialTypeObservation(value.ToString(), (int)value);
        }

        [SpecOperation("read_wkt_geography", Spec = "spatial.errors_and_spatial_type")]
        [SpecException(typeof(ParseErrorException))]
        public static void ReadWktGeography(string text, bool twoDimensions)
        {
            using (var reader = new StringReader(text))
            {
                WellKnownTextSqlFormatter.Create(twoDimensions).Read<Geography>(reader);
            }
        }
    }

    [SpecEvent("SpatialTypeObservation")]
    internal sealed class SpatialTypeObservation
    {
        internal SpatialTypeObservation(string name, int value)
        {
            this.Name = name;
            this.Value = value;
        }

        [SpecEvent("name")]
        public string Name { get; }

        [SpecEvent("value")]
        public int Value { get; }
    }
}
