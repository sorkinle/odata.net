#nullable enable

namespace Microsoft.Spatial
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using SpecGate.Annotations;

    // Capture-fork adapters only. These observations do not add formatter APIs.
    internal static class GeoJsonCaptureAdapter
    {
        private const string Component = "spatial.geojson";

        [SpecOperation("read_geography", Spec = Component)]
        [SpecException]
        public static GeoJsonCaptureShapeResult ReadGeography(GeoJsonCaptureValueInput input, bool concreteLineString)
        {
            var source = (IDictionary<string, object>)input.Materialize()!;
            var formatter = GeoJsonObjectFormatter.Create();
            Geography? value = concreteLineString
                ? formatter.Read<GeographyLineString>(source)
                : formatter.Read<Geography>(source);
            return Observe(value);
        }

        [SpecOperation("read_geometry", Spec = Component)]
        [SpecException]
        public static GeoJsonCaptureShapeResult ReadGeometry(GeoJsonCaptureValueInput input, bool concreteLineString)
        {
            var source = (IDictionary<string, object>)input.Materialize()!;
            var formatter = GeoJsonObjectFormatter.Create();
            Geometry? value = concreteLineString
                ? formatter.Read<GeometryLineString>(source)
                : formatter.Read<Geometry>(source);
            return Observe(value);
        }

        [SpecOperation("write_materialized", Spec = Component)]
        [SpecException]
        public static List<GeoJsonCaptureJsonNode> WriteMaterialized(GeoJsonCaptureSpatialInput input)
        {
            var builder = SpatialBuilder.Create();
            input.SendTo(builder);
            ISpatial value = input.IsGeography
                ? (ISpatial)builder.ConstructedGeography
                : builder.ConstructedGeometry;
            return GeoJsonCaptureValueInput.Flatten(GeoJsonObjectFormatter.Create().Write(value));
        }

        [SpecOperation("write_events", Spec = Component)]
        [SpecException]
        public static GeoJsonCaptureWriterResult WriteEvents(GeoJsonCaptureSpatialInput input, int failAtCallback)
        {
            var result = new GeoJsonCaptureWriterResult();
            var sink = new JsonRecorder(result.Events, failAtCallback);
            var pipeline = GeoJsonObjectFormatter.Create().CreateWriter(sink);
            try
            {
                input.SendTo(pipeline);
                result.Succeeded = true;
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, sink.Failure))
            {
                // Keep the attempted callback prefix even when the sink fails.
                result.FailureType = error.GetType().FullName;
                result.FailureMessage = error.Message;
            }

            return result;
        }

        public static GeoJsonCaptureShapeResult Observe(Geography? value)
        {
            var result = new GeoJsonCaptureShapeResult { HasValue = value != null };
            if (value != null)
            {
                value.SendTo(new GeographyRecorder(result.Events));
            }

            return result;
        }

        public static GeoJsonCaptureShapeResult Observe(Geometry? value)
        {
            var result = new GeoJsonCaptureShapeResult { HasValue = value != null };
            if (value != null)
            {
                value.SendTo(new GeometryRecorder(result.Events));
            }

            return result;
        }

        private sealed class GeographyRecorder : GeographyPipeline
        {
            private readonly List<GeoJsonCaptureSpatialEvent> events;

            public GeographyRecorder(List<GeoJsonCaptureSpatialEvent> events) { this.events = events; }
            public override void SetCoordinateSystem(CoordinateSystem coordinateSystem)
                => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "set_crs", Srid = coordinateSystem.EpsgId });
            public override void BeginGeography(SpatialType type)
                => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "begin_shape", Shape = type.ToString() });
            public override void BeginFigure(GeographyPosition position)
                => this.AddPosition("begin_figure", position);
            public override void LineTo(GeographyPosition position) => this.AddPosition("line_to", position);
            public override void EndFigure() => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "end_figure" });
            public override void EndGeography() => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "end_shape" });
            public override void Reset() => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "reset" });

            private void AddPosition(string name, GeographyPosition position)
                => this.events.Add(new GeoJsonCaptureSpatialEvent
                {
                    Name = name, First = position.Latitude, Second = position.Longitude, Z = position.Z, M = position.M
                });
        }

        private sealed class GeometryRecorder : GeometryPipeline
        {
            private readonly List<GeoJsonCaptureSpatialEvent> events;

            public GeometryRecorder(List<GeoJsonCaptureSpatialEvent> events) { this.events = events; }
            public override void SetCoordinateSystem(CoordinateSystem coordinateSystem)
                => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "set_crs", Srid = coordinateSystem.EpsgId });
            public override void BeginGeometry(SpatialType type)
                => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "begin_shape", Shape = type.ToString() });
            public override void BeginFigure(GeometryPosition position) => this.AddPosition("begin_figure", position);
            public override void LineTo(GeometryPosition position) => this.AddPosition("line_to", position);
            public override void EndFigure() => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "end_figure" });
            public override void EndGeometry() => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "end_shape" });
            public override void Reset() => this.events.Add(new GeoJsonCaptureSpatialEvent { Name = "reset" });

            private void AddPosition(string name, GeometryPosition position)
                => this.events.Add(new GeoJsonCaptureSpatialEvent
                {
                    Name = name, First = position.X, Second = position.Y, Z = position.Z, M = position.M
                });
        }

        private sealed class JsonRecorder : IGeoJsonWriter
        {
            private readonly List<GeoJsonCaptureWriterEvent> events;
            private readonly int failAtCallback;

            public InvalidOperationException? Failure { get; private set; }

            public JsonRecorder(List<GeoJsonCaptureWriterEvent> events, int failAtCallback)
            {
                if (failAtCallback < 0) { throw new ArgumentOutOfRangeException(nameof(failAtCallback)); }
                this.events = events;
                this.failAtCallback = failAtCallback;
            }

            public void StartObjectScope() => this.Add(new GeoJsonCaptureWriterEvent { Name = "start_object" });
            public void EndObjectScope() => this.Add(new GeoJsonCaptureWriterEvent { Name = "end_object" });
            public void StartArrayScope() => this.Add(new GeoJsonCaptureWriterEvent { Name = "start_array" });
            public void EndArrayScope() => this.Add(new GeoJsonCaptureWriterEvent { Name = "end_array" });
            public void AddPropertyName(string name)
                => this.Add(new GeoJsonCaptureWriterEvent { Name = "property_name", Text = name });
            public void AddValue(double value)
                => this.Add(new GeoJsonCaptureWriterEvent { Name = "number_value", Number = value });
            public void AddValue(string? value)
                => this.Add(new GeoJsonCaptureWriterEvent { Name = value == null ? "null_value" : "string_value", Text = value });

            private void Add(GeoJsonCaptureWriterEvent value)
            {
                this.events.Add(value);
                if (this.failAtCallback > 0 && this.events.Count == this.failAtCallback)
                {
                    this.Failure = new InvalidOperationException("GeoJSON capture sink rejected callback " + this.failAtCallback);
                    throw this.Failure;
                }
            }
        }
    }

    [SpecEvent("GeoJsonShapeResult")]
    internal sealed class GeoJsonCaptureShapeResult
    {
        [SpecEvent] public bool HasValue { get; set; }
        [SpecEvent] public List<GeoJsonCaptureSpatialEvent> Events { get; set; } = new List<GeoJsonCaptureSpatialEvent>();
    }

    [SpecEvent("GeoJsonSpatialEvent")]
    internal sealed class GeoJsonCaptureSpatialEvent
    {
        [SpecEvent] public string Name { get; set; } = "";
        [SpecEvent] public int? Srid { get; set; }
        [SpecEvent] public string? Shape { get; set; }
        [SpecEvent] public double? First { get; set; }
        [SpecEvent] public double? Second { get; set; }
        [SpecEvent] public double? Z { get; set; }
        [SpecEvent] public double? M { get; set; }
    }

    [SpecEvent("GeoJsonWriterEvent")]
    internal sealed class GeoJsonCaptureWriterEvent
    {
        [SpecEvent] public string Name { get; set; } = "";
        [SpecEvent] public string? Text { get; set; }
        [SpecEvent] public double? Number { get; set; }
    }

    [SpecEvent("GeoJsonWriterResult")]
    internal sealed class GeoJsonCaptureWriterResult
    {
        [SpecEvent] public bool Succeeded { get; set; }
        [SpecEvent] public string? FailureType { get; set; }
        [SpecEvent] public string? FailureMessage { get; set; }
        [SpecEvent] public List<GeoJsonCaptureWriterEvent> Events { get; set; } = new List<GeoJsonCaptureWriterEvent>();
    }

    [SpecEvent("GeoJsonNode")]
    internal sealed class GeoJsonCaptureJsonNode
    {
        [SpecEvent] public int Parent { get; set; }
        [SpecEvent] public string Name { get; set; } = "";
        [SpecEvent] public string Kind { get; set; } = "";
        [SpecEvent] public double? Number { get; set; }
        [SpecEvent] public long? Integer { get; set; }
        [SpecEvent] public string? Text { get; set; }
    }

    // Parallel primitive arrays preserve the actual tree without record inputs,
    // fixture identifiers, or a JSON text parser at the materialized boundary.
    [SpecEvent(Setup = true)]
    internal sealed class GeoJsonCaptureValueInput
    {
        public int[] Parents { get; set; } = Array.Empty<int>();
        public string[] Names { get; set; } = Array.Empty<string>();
        public string[] Kinds { get; set; } = Array.Empty<string>();
        public double?[] Numbers { get; set; } = Array.Empty<double?>();
        public long?[] Integers { get; set; } = Array.Empty<long?>();
        public string?[] Texts { get; set; } = Array.Empty<string?>();

        public static GeoJsonCaptureValueInput FromValue(object? value)
        {
            var nodes = Flatten(value);
            return new GeoJsonCaptureValueInput
            {
                Parents = nodes.Select(node => node.Parent).ToArray(),
                Names = nodes.Select(node => node.Name).ToArray(),
                Kinds = nodes.Select(node => node.Kind).ToArray(),
                Numbers = nodes.Select(node => node.Number).ToArray(),
                Integers = nodes.Select(node => node.Integer).ToArray(),
                Texts = nodes.Select(node => node.Text).ToArray()
            };
        }

        public object? Materialize()
        {
            int count = this.Kinds.Length;
            if (count == 0 || this.Parents.Length != count || this.Names.Length != count ||
                this.Numbers.Length != count || this.Integers.Length != count || this.Texts.Length != count)
            {
                throw new InvalidOperationException("GeoJSON capture input arrays must have the same nonzero length.");
            }

            var values = new object?[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = this.MaterializeNode(index);
                int parent = this.Parents[index];
                if (index == 0)
                {
                    if (parent != -1) { throw new InvalidOperationException("The capture root must have parent -1."); }
                    continue;
                }

                if (parent < 0 || parent >= index)
                {
                    throw new InvalidOperationException("A capture node must follow its parent.");
                }

                if (values[parent] is IDictionary<string, object?> map)
                {
                    map.Add(this.Names[index], values[index]);
                }
                else if (values[parent] is List<object?> array)
                {
                    array.Add(values[index]);
                }
                else
                {
                    throw new InvalidOperationException("A capture parent must be an object or array.");
                }
            }

            return values[0];
        }

        public static List<GeoJsonCaptureJsonNode> Flatten(object? value)
        {
            var nodes = new List<GeoJsonCaptureJsonNode>();
            Append(value, -1, "", nodes);
            return nodes;
        }

        private object? MaterializeNode(int index)
        {
            switch (this.Kinds[index])
            {
                case "object": return new Dictionary<string, object?>();
                case "array": return new List<object?>();
                case "null": return null;
                case "string": return this.Texts[index] ?? throw new InvalidOperationException("Missing capture string.");
                case "double": return this.Numbers[index]!.Value;
                case "single": return checked((float)this.Numbers[index]!.Value);
                case "int": return checked((int)this.Integers[index]!.Value);
                case "long": return this.Integers[index]!.Value;
                case "bool":
                    if (this.Integers[index] == 0) { return false; }
                    if (this.Integers[index] == 1) { return true; }
                    throw new InvalidOperationException("A capture boolean must be 0 or 1.");
                case "decimal": return decimal.Parse(this.Texts[index]!, CultureInfo.InvariantCulture);
                default: throw new InvalidOperationException("Unsupported capture node kind: " + this.Kinds[index]);
            }
        }

        private static void Append(object? value, int parent, string name, List<GeoJsonCaptureJsonNode> nodes)
        {
            var node = new GeoJsonCaptureJsonNode { Parent = parent, Name = name };
            int index = nodes.Count;
            nodes.Add(node);
            switch (value)
            {
                case null: node.Kind = "null"; break;
                case string text: node.Kind = "string"; node.Text = text; break;
                case bool flag: node.Kind = "bool"; node.Integer = flag ? 1 : 0; break;
                case double number: node.Kind = "double"; node.Number = number; break;
                case float number: node.Kind = "single"; node.Number = number; break;
                case int number: node.Kind = "int"; node.Integer = number; break;
                case long number: node.Kind = "long"; node.Integer = number; break;
                case decimal number:
                    node.Kind = "decimal";
                    node.Text = number.ToString(CultureInfo.InvariantCulture);
                    break;
                case IDictionary<string, object> map:
                    node.Kind = "object";
                    foreach (var member in map) { Append(member.Value, index, member.Key, nodes); }
                    break;
                case IEnumerable array:
                    node.Kind = "array";
                    foreach (object? item in array) { Append(item, index, "", nodes); }
                    break;
                default:
                    throw new InvalidOperationException("Unsupported capture CLR value type: " + value.GetType().FullName);
            }
        }
    }

    [SpecEvent(Setup = true)]
    internal sealed class GeoJsonCaptureSpatialInput
    {
        public bool IsGeography { get; set; }
        public string[] Calls { get; set; } = Array.Empty<string>();
        public int?[] Srids { get; set; } = Array.Empty<int?>();
        public string?[] Shapes { get; set; } = Array.Empty<string?>();
        public double?[] Firsts { get; set; } = Array.Empty<double?>();
        public double?[] Seconds { get; set; } = Array.Empty<double?>();
        public double?[] Zs { get; set; } = Array.Empty<double?>();
        public double?[] Ms { get; set; } = Array.Empty<double?>();

        public static GeoJsonCaptureSpatialInput FromEvents(bool isGeography, List<GeoJsonCaptureSpatialEvent> events)
            => new GeoJsonCaptureSpatialInput
            {
                IsGeography = isGeography,
                Calls = events.Select(item => item.Name).ToArray(),
                Srids = events.Select(item => item.Srid).ToArray(),
                Shapes = events.Select(item => item.Shape).ToArray(),
                Firsts = events.Select(item => item.First).ToArray(),
                Seconds = events.Select(item => item.Second).ToArray(),
                Zs = events.Select(item => item.Z).ToArray(),
                Ms = events.Select(item => item.M).ToArray()
            };

        public void SendTo(SpatialPipeline pipeline)
        {
            int count = this.Calls.Length;
            if (this.Srids.Length != count || this.Shapes.Length != count || this.Firsts.Length != count ||
                this.Seconds.Length != count || this.Zs.Length != count || this.Ms.Length != count)
            {
                throw new InvalidOperationException("Spatial capture input arrays must have the same length.");
            }

            for (int index = 0; index < count; index++)
            {
                if (this.IsGeography) { this.SendGeography(pipeline.GeographyPipeline, index); }
                else { this.SendGeometry(pipeline.GeometryPipeline, index); }
            }
        }

        private void SendGeography(GeographyPipeline pipeline, int index)
        {
            switch (this.Calls[index])
            {
                case "set_crs": pipeline.SetCoordinateSystem(CoordinateSystem.Geography(this.Srids[index])); break;
                case "begin_shape": pipeline.BeginGeography(Enum.Parse<SpatialType>(this.Shapes[index]!)); break;
                case "begin_figure": pipeline.BeginFigure(this.GeographyPosition(index)); break;
                case "line_to": pipeline.LineTo(this.GeographyPosition(index)); break;
                case "end_figure": pipeline.EndFigure(); break;
                case "end_shape": pipeline.EndGeography(); break;
                case "reset": pipeline.Reset(); break;
                default: throw new InvalidOperationException("Unknown spatial capture call: " + this.Calls[index]);
            }
        }

        private void SendGeometry(GeometryPipeline pipeline, int index)
        {
            switch (this.Calls[index])
            {
                case "set_crs": pipeline.SetCoordinateSystem(CoordinateSystem.Geometry(this.Srids[index])); break;
                case "begin_shape": pipeline.BeginGeometry(Enum.Parse<SpatialType>(this.Shapes[index]!)); break;
                case "begin_figure": pipeline.BeginFigure(this.GeometryPosition(index)); break;
                case "line_to": pipeline.LineTo(this.GeometryPosition(index)); break;
                case "end_figure": pipeline.EndFigure(); break;
                case "end_shape": pipeline.EndGeometry(); break;
                case "reset": pipeline.Reset(); break;
                default: throw new InvalidOperationException("Unknown spatial capture call: " + this.Calls[index]);
            }
        }

        private GeographyPosition GeographyPosition(int index)
            => new GeographyPosition(this.Firsts[index]!.Value, this.Seconds[index]!.Value, this.Zs[index], this.Ms[index]);
        private GeometryPosition GeometryPosition(int index)
            => new GeometryPosition(this.Firsts[index]!.Value, this.Seconds[index]!.Value, this.Zs[index], this.Ms[index]);
    }
}
