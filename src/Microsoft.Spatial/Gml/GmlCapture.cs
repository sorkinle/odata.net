#nullable enable

namespace Microsoft.Spatial
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml;
    using SpecGate.Annotations;

    // The Rust receiver is a caller-provided XmlPull, not the built-in XML decoder.
    [SpecEvent(Setup = true)]
    internal sealed class GmlXmlInput
    {
        private readonly string xml;
        public string[] Kinds { get; private set; } = Array.Empty<string>();
        public string[] Names { get; private set; } = Array.Empty<string>();
        public string[] Namespaces { get; private set; } = Array.Empty<string>();
        public string[] Values { get; private set; } = Array.Empty<string>();
        public bool[] Empty { get; private set; } = Array.Empty<bool>();
        public int[] AttributeOwners { get; private set; } = Array.Empty<int>();
        public string[] AttributeNames { get; private set; } = Array.Empty<string>();
        public string[] AttributeNamespaces { get; private set; } = Array.Empty<string>();
        public string[] AttributeValues { get; private set; } = Array.Empty<string>();

        private GmlXmlInput(string xml) { this.xml = xml; }

        internal XmlReader Open()
        {
            var reader = XmlReader.Create(new StringReader(this.xml), new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit
            });
            reader.MoveToContent();
            return reader;
        }

        internal static GmlXmlInput FromXml(string xml)
        {
            var result = new GmlXmlInput(xml);
            var kinds = new List<string>();
            var names = new List<string>();
            var spaces = new List<string>();
            var values = new List<string>();
            var empty = new List<bool>();
            var owners = new List<int>();
            var attrNames = new List<string>();
            var attrSpaces = new List<string>();
            var attrValues = new List<string>();
            using var reader = result.Open();
            if (!reader.EOF)
            {
                do
                {
                    int index = kinds.Count;
                    kinds.Add(NodeKind(reader));
                    names.Add(reader.LocalName);
                    spaces.Add(reader.NamespaceURI);
                    values.Add(reader.Value);
                    empty.Add(reader.IsEmptyElement);
                    if (reader.NodeType == XmlNodeType.Element && reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (reader.NamespaceURI == "http://www.w3.org/2000/xmlns/") { continue; }
                            owners.Add(index);
                            attrNames.Add(reader.LocalName);
                            attrSpaces.Add(reader.NamespaceURI);
                            attrValues.Add(reader.Value);
                        }
                        while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                }
                while (reader.Read());
            }

            result.Kinds = kinds.ToArray();
            result.Names = names.ToArray();
            result.Namespaces = spaces.ToArray();
            result.Values = values.ToArray();
            result.Empty = empty.ToArray();
            result.AttributeOwners = owners.ToArray();
            result.AttributeNames = attrNames.ToArray();
            result.AttributeNamespaces = attrSpaces.ToArray();
            result.AttributeValues = attrValues.ToArray();
            return result;
        }

        internal static string NodeKind(XmlReader reader) => reader.NodeType switch
        {
            XmlNodeType.None => "eof",
            XmlNodeType.Element => "start",
            XmlNodeType.EndElement => "end",
            XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace => "text",
            XmlNodeType.CDATA => "cdata",
            XmlNodeType.Comment => "comment",
            XmlNodeType.ProcessingInstruction => "pi",
            _ => throw new NotSupportedException("Unsupported XML input node: " + reader.NodeType)
        };
    }

    [SpecEvent(Setup = true)]
    internal sealed class GmlShapeInput
    {
        public string[] Steps { get; set; } = Array.Empty<string>();
        public bool[] Geographies { get; set; } = Array.Empty<bool>();
        public int?[] Srids { get; set; } = Array.Empty<int?>();
        public double?[][] Positions { get; set; } = Array.Empty<double?[]>();

        internal void Drive(SpatialPipeline pipeline)
        {
            if (this.Geographies.Length != this.Steps.Length || this.Srids.Length != this.Steps.Length ||
                this.Positions.Length != this.Steps.Length)
            {
                throw new ArgumentException("Shape columns must have one row per step.");
            }

            for (int i = 0; i < this.Steps.Length; i++)
            {
                string step = this.Steps[i];
                bool geography = this.Geographies[i];
                var geo = pipeline.GeographyPipeline;
                var geom = pipeline.GeometryPipeline;
                if (step == "crs")
                {
                    if (geography) { geo.SetCoordinateSystem(CoordinateSystem.Geography(this.Srids[i])); }
                    else { geom.SetCoordinateSystem(CoordinateSystem.Geometry(this.Srids[i])); }
                }
                else if (step.StartsWith("begin:", StringComparison.Ordinal))
                {
                    var kind = Enum.Parse<SpatialType>(step.Substring(6));
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
                else if (step == "reset")
                {
                    if (geography) { geo.Reset(); } else { geom.Reset(); }
                }
                else if (step == "begin_figure" || step == "line_to")
                {
                    if (this.Positions[i].Length != 4 ||
                        this.Positions[i][0] is not double first || this.Positions[i][1] is not double second)
                    {
                        throw new ArgumentException("A position requires two ordinates and optional Z and M.");
                    }
                    if (geography)
                    {
                        var p = new GeographyPosition(first, second, this.Positions[i][2], this.Positions[i][3]);
                        if (step == "begin_figure") { geo.BeginFigure(p); } else { geo.LineTo(p); }
                    }
                    else
                    {
                        var p = new GeometryPosition(first, second, this.Positions[i][2], this.Positions[i][3]);
                        if (step == "begin_figure") { geom.BeginFigure(p); } else { geom.LineTo(p); }
                    }
                }
                else { throw new ArgumentException("Unknown shape step: " + step); }
            }
        }
    }

    internal static class GmlCapture
    {
        [SpecOperation("read", Spec = "spatial.gml")]
        public static GmlReadObservation Read(GmlXmlInput input, bool geography, bool materialize)
        {
            using var reader = input.Open();
            var result = new GmlReadObservation();
            var formatter = GmlFormatter.Create();
            try
            {
                if (materialize)
                {
                    ISpatial value = geography ? formatter.Read<Geography>(reader) : formatter.Read<Geometry>(reader);
                    result.IsEmpty = value.IsEmpty;
                    value.SendTo(result.Pipeline());
                }
                else if (geography) { formatter.Read<Geography>(reader, result.Pipeline()); }
                else { formatter.Read<Geometry>(reader, result.Pipeline()); }
            }
            catch (ParseErrorException error) { result.Error = error.Message; }
            result.CursorKind = GmlXmlInput.NodeKind(reader);
            result.CursorName = reader.LocalName;
            result.CursorNamespace = reader.NamespaceURI;
            return result;
        }

        [SpecOperation("write_xml", Spec = "spatial.gml")]
        public static string WriteXml(GmlShapeInput input, bool geography)
        {
            var builder = SpatialBuilder.Create();
            input.Drive(builder);
            return FormatterExtensions.Write(GmlFormatter.Create(),
                geography ? builder.ConstructedGeography : builder.ConstructedGeometry);
        }

        [SpecOperation("write_pipeline", Spec = "spatial.gml")]
        public static GmlWriteObservation WritePipeline(GmlShapeInput input, int failText)
        {
            var writer = new GmlCallWriter(failText);
            try { input.Drive(GmlFormatter.Create().CreateWriter(writer)); }
            catch (IOException error) when (ReferenceEquals(error, writer.InjectedFailure))
            {
                writer.Result.Error = error.Message;
            }
            writer.FinishText();
            return writer.Result;
        }
    }

    [SpecEvent("GmlReadObservation")]
    internal sealed class GmlReadObservation
    {
        [SpecEvent] public List<string> Events { get; } = new();
        [SpecEvent] public List<int?> Srids { get; } = new();
        [SpecEvent] public List<double?[]> Positions { get; } = new();
        [SpecEvent] public bool? IsEmpty { get; set; }
        [SpecEvent] public string? Error { get; set; }
        [SpecEvent] public string CursorKind { get; set; } = string.Empty;
        [SpecEvent] public string CursorName { get; set; } = string.Empty;
        [SpecEvent] public string CursorNamespace { get; set; } = string.Empty;
        internal SpatialPipeline Pipeline() => new(new Geo(this), new Geom(this));
        private void Position(string step, double first, double second, double? z, double? m)
        {
            this.Events.Add(step);
            this.Positions.Add(new[] { first, second, z, m });
        }
        private sealed class Geo(GmlReadObservation o) : GeographyPipeline
        {
            public override void SetCoordinateSystem(CoordinateSystem c) { o.Events.Add("crs"); o.Srids.Add(c.EpsgId); }
            public override void BeginGeography(SpatialType t) => o.Events.Add("begin:" + t);
            public override void EndGeography() => o.Events.Add("end");
            public override void BeginFigure(GeographyPosition p) => o.Position("begin_figure", p.Latitude, p.Longitude, p.Z, p.M);
            public override void LineTo(GeographyPosition p) => o.Position("line_to", p.Latitude, p.Longitude, p.Z, p.M);
            public override void EndFigure() => o.Events.Add("end_figure");
            public override void Reset() => o.Events.Add("reset:geography");
        }
        private sealed class Geom(GmlReadObservation o) : GeometryPipeline
        {
            public override void SetCoordinateSystem(CoordinateSystem c) { o.Events.Add("crs"); o.Srids.Add(c.EpsgId); }
            public override void BeginGeometry(SpatialType t) => o.Events.Add("begin:" + t);
            public override void EndGeometry() => o.Events.Add("end");
            public override void BeginFigure(GeometryPosition p) => o.Position("begin_figure", p.X, p.Y, p.Z, p.M);
            public override void LineTo(GeometryPosition p) => o.Position("line_to", p.X, p.Y, p.Z, p.M);
            public override void EndFigure() => o.Events.Add("end_figure");
            public override void Reset() => o.Events.Add("reset:geometry");
        }
    }

    [SpecEvent("GmlWriteObservation")]
    internal sealed class GmlWriteObservation
    {
        [SpecEvent] public List<string> Events { get; } = new();
        [SpecEvent] public string? Error { get; set; }
        [SpecEvent] public int Flushes { get; set; }
        public int Closes { get; set; }
    }

    // Observe XmlWriter calls, not namespace declarations synthesized by a stock XML serializer.
    internal sealed class GmlCallWriter(int failText) : XmlWriter
    {
        private string? attribute;
        private readonly StringBuilder text = new();
        private int textCalls;
        private WriteState state = WriteState.Start;
        internal GmlWriteObservation Result { get; } = new();
        internal IOException InjectedFailure { get; } = new("injected text failure");
        public override WriteState WriteState => this.state;
        internal void FinishText()
        {
            if (this.text.Length == 0) { return; }
            this.Result.Events.Add("text:" + this.text);
            this.text.Clear();
        }
        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            this.FinishText();
            this.Result.Events.Add($"start:{prefix}:{localName}:{ns}");
            this.state = WriteState.Element;
        }
        public override void WriteEndElement()
        {
            this.FinishText();
            this.Result.Events.Add("end");
            this.state = WriteState.Content;
        }
        public override void WriteFullEndElement() => this.WriteEndElement();
        public override void WriteStartAttribute(string? prefix, string localName, string? ns)
        {
            this.FinishText();
            this.attribute = $"attribute:{prefix}:{localName}:{ns}:";
            this.state = WriteState.Attribute;
        }
        public override void WriteEndAttribute()
        {
            if (this.attribute is null) { throw new InvalidOperationException("No attribute is open."); }
            this.Result.Events.Add(this.attribute + this.text);
            this.text.Clear();
            this.attribute = null;
            this.state = WriteState.Element;
        }
        public override void WriteString(string? value)
        {
            this.text.Append(value);
            if (this.attribute is null)
            {
                this.state = WriteState.Content;
                if (++this.textCalls == failText) { throw this.InjectedFailure; }
            }
        }
        public override void WriteWhitespace(string? ws) => this.WriteString(ws);
        public override void Flush() => this.Result.Flushes++;
        public override void Close() => this.Result.Closes++;
        public override string? LookupPrefix(string ns) => null;
        public override void WriteStartDocument() => throw new NotSupportedException();
        public override void WriteStartDocument(bool standalone) => throw new NotSupportedException();
        public override void WriteEndDocument() => throw new NotSupportedException();
        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) => throw new NotSupportedException();
        public override void WriteCData(string? value) => throw new NotSupportedException();
        public override void WriteComment(string? value) => throw new NotSupportedException();
        public override void WriteProcessingInstruction(string name, string? value) => throw new NotSupportedException();
        public override void WriteEntityRef(string name) => throw new NotSupportedException();
        public override void WriteCharEntity(char ch) => throw new NotSupportedException();
        public override void WriteSurrogateCharEntity(char low, char high) => throw new NotSupportedException();
        public override void WriteChars(char[] buffer, int index, int count) => this.WriteString(new string(buffer, index, count));
        public override void WriteRaw(char[] buffer, int index, int count) => throw new NotSupportedException();
        public override void WriteRaw(string value) => throw new NotSupportedException();
        public override void WriteBase64(byte[] buffer, int index, int count) => throw new NotSupportedException();
    }
}
