#nullable enable
#pragma warning disable CS3016 // SpecException accepts a params array of exception types.

namespace Microsoft.Spatial
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using SpecGate.Annotations;

    internal static class WktCapture
    {
        private const string Component = "spatial.wkt";

        [SpecOperation("read_value", Spec = Component)]
        [SpecException(typeof(ParseErrorException))]
        public static WktObservation ReadValue(string text, bool geography, bool twoDimensions)
        {
            using var reader = new StringReader(text);
            var formatter = CreateFormatter(twoDimensions);
            ISpatial value = geography ? formatter.Read<Geography>(reader) : formatter.Read<Geometry>(reader);
            var observation = new WktObservation { IsEmpty = value.IsEmpty };
            value.SendTo(observation.CreatePipeline());
            observation.Remaining = reader.ReadToEnd();
            return observation;
        }

        [SpecOperation("read_pipeline", Spec = Component)]
        public static WktObservation ReadPipeline(string text, bool geography, bool twoDimensions)
        {
            using var reader = new StringReader(text);
            var formatter = CreateFormatter(twoDimensions);
            var observation = new WktObservation();
            var destination = observation.CreatePipeline();
            try
            {
                if (geography)
                {
                    formatter.Read<Geography>(reader, destination);
                }
                else
                {
                    formatter.Read<Geometry>(reader, destination);
                }
            }
            catch (ParseErrorException error)
            {
                // Keep the emitted events and the reader position when parsing fails.
                observation.Error = error.Message;
            }

            observation.Remaining = reader.ReadToEnd();
            return observation;
        }

        [SpecOperation("write_text", Spec = Component)]
        public static string WriteText(bool geography, bool twoDimensions, int? srid, string[] events, double?[][] positions)
        {
            return FormatterExtensions.Write(CreateFormatter(twoDimensions), BuildShape(geography, srid, events, positions));
        }

        [SpecOperation("write_stream", Spec = Component)]
        public static string WriteStream(bool geography, bool twoDimensions, int? srid, string[] events, double?[][] positions, string culture)
        {
            using var writer = new StringWriter(CultureInfo.GetCultureInfo(culture));
            CreateFormatter(twoDimensions).Write(BuildShape(geography, srid, events, positions), writer);
            return writer.ToString();
        }

        [SpecOperation("write_pipeline", Spec = Component)]
        public static string WritePipeline(bool geography, bool twoDimensions, int? srid, string[] events, double?[][] positions, string culture)
        {
            using var writer = new StringWriter(CultureInfo.GetCultureInfo(culture));
            Drive(CreateFormatter(twoDimensions).CreateWriter(writer), geography, srid, events, positions);
            return writer.ToString();
        }

        [SpecOperation("write_xml_extension", Spec = Component)]
        public static string WriteXmlExtension(bool geography, int? srid, string[] events, double?[][] positions)
        {
            return FormatterExtensions.Write(GmlFormatter.Create(), BuildShape(geography, srid, events, positions));
        }

        private static WellKnownTextSqlFormatter CreateFormatter(bool twoDimensions)
        {
            return twoDimensions ? WellKnownTextSqlFormatter.Create(true) : WellKnownTextSqlFormatter.Create();
        }

        private static ISpatial BuildShape(bool geography, int? srid, string[] events, double?[][] positions)
        {
            var builder = SpatialBuilder.Create();
            Drive(builder, geography, srid, events, positions);
            return geography ? builder.ConstructedGeography : builder.ConstructedGeometry;
        }

        // The script is the shape's ordered event structure, not a fixture key or text to parse.
        // Coordinate arrays are lat/lon/Z/M for geography and X/Y/Z/M for geometry.
        private static void Drive(SpatialPipeline pipeline, bool geography, int? srid, string[] events, double?[][] positions)
        {
            GeographyPipeline geo = pipeline.GeographyPipeline;
            GeometryPipeline geom = pipeline.GeometryPipeline;
            if (geography)
            {
                geo.SetCoordinateSystem(CoordinateSystem.Geography(srid));
            }
            else
            {
                geom.SetCoordinateSystem(CoordinateSystem.Geometry(srid));
            }

            int positionIndex = 0;
            foreach (string step in events)
            {
                if (step.StartsWith("begin:", StringComparison.Ordinal))
                {
                    var kind = Enum.Parse<SpatialType>(step.Substring("begin:".Length));
                    if (geography) { geo.BeginGeography(kind); } else { geom.BeginGeometry(kind); }
                }
                else if (step == "end")
                {
                    if (geography) { geo.EndGeography(); } else { geom.EndGeometry(); }
                }
                else if (step == "end_figure")
                {
                    if (geography) { geo.EndFigure(); } else { geom.EndFigure(); }
                }
                else if (step == "begin_figure" || step == "line_to")
                {
                    if (positionIndex >= positions.Length || positions[positionIndex].Length != 4 ||
                        positions[positionIndex][0] is not double first || positions[positionIndex][1] is not double second)
                    {
                        throw new ArgumentException("A figure event requires two ordinates and optional Z and M.", nameof(positions));
                    }

                    double?[] position = positions[positionIndex++];
                    if (geography)
                    {
                        var value = new GeographyPosition(first, second, position[2], position[3]);
                        if (step == "begin_figure") { geo.BeginFigure(value); } else { geo.LineTo(value); }
                    }
                    else
                    {
                        var value = new GeometryPosition(first, second, position[2], position[3]);
                        if (step == "begin_figure") { geom.BeginFigure(value); } else { geom.LineTo(value); }
                    }
                }
                else
                {
                    throw new ArgumentException($"Unknown shape event '{step}'.", nameof(events));
                }
            }

            if (positionIndex != positions.Length)
            {
                throw new ArgumentException("Not every position was used by a figure event.", nameof(positions));
            }
        }
    }

    [SpecEvent("WktObservation")]
    internal sealed class WktObservation
    {
        private readonly List<string> events = new();
        private readonly List<double?[]> positions = new();
        private readonly List<int?> srids = new();

        [SpecEvent("events")]
        public string[] Events => this.events.ToArray();

        [SpecEvent("positions")]
        public double?[][] Positions => this.positions.ToArray();

        [SpecEvent("srids")]
        public int?[] Srids => this.srids.ToArray();

        [SpecEvent("isEmpty")]
        public bool? IsEmpty { get; set; }

        [SpecEvent("remaining")]
        public string Remaining { get; set; } = string.Empty;

        [SpecEvent("error")]
        public string? Error { get; set; }

        internal SpatialPipeline CreatePipeline() => new(new GeographySink(this), new GeometrySink(this));

        private void AddPosition(string step, double first, double second, double? z, double? m)
        {
            this.events.Add(step);
            this.positions.Add(new[] { first, second, z, m });
        }

        private void AddCrs(CoordinateSystem crs)
        {
            this.events.Add("srid");
            this.srids.Add(crs.EpsgId);
        }

        private sealed class GeographySink(WktObservation trace) : GeographyPipeline
        {
            public override void SetCoordinateSystem(CoordinateSystem crs) => trace.AddCrs(crs);
            public override void BeginGeography(SpatialType type) => trace.events.Add("begin:" + type);
            public override void BeginFigure(GeographyPosition p) => trace.AddPosition("begin_figure", p.Latitude, p.Longitude, p.Z, p.M);
            public override void LineTo(GeographyPosition p) => trace.AddPosition("line_to", p.Latitude, p.Longitude, p.Z, p.M);
            public override void EndFigure() => trace.events.Add("end_figure");
            public override void EndGeography() => trace.events.Add("end");
            public override void Reset() => trace.events.Add("reset");
        }

        private sealed class GeometrySink(WktObservation trace) : GeometryPipeline
        {
            public override void SetCoordinateSystem(CoordinateSystem crs) => trace.AddCrs(crs);
            public override void BeginGeometry(SpatialType type) => trace.events.Add("begin:" + type);
            public override void BeginFigure(GeometryPosition p) => trace.AddPosition("begin_figure", p.X, p.Y, p.Z, p.M);
            public override void LineTo(GeometryPosition p) => trace.AddPosition("line_to", p.X, p.Y, p.Z, p.M);
            public override void EndFigure() => trace.events.Add("end_figure");
            public override void EndGeometry() => trace.events.Add("end");
            public override void Reset() => trace.events.Add("reset");
        }
    }
}
