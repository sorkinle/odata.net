#nullable enable

namespace Microsoft.Spatial.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    using Xunit;

    public class GmlCaptureTests
    {
        private const string Gml = "http://www.opengis.net/gml";

        [Fact]
        public void ReadsAllShapeFamiliesAndEmptyChildren()
        {
            foreach (string body in new[]
            {
                "<Point><pos>10 20</pos></Point>",
                "<LineString><posList>10 20 30 40</posList></LineString>",
                "<Polygon><exterior><LinearRing><posList>0 0 0 1 1 1 0 0</posList></LinearRing></exterior></Polygon>",
                "<MultiPoint><pointMembers><Point><pos/></Point><Point><pos>10 20</pos></Point></pointMembers></MultiPoint>",
                "<MultiCurve><curveMembers><LineString><posList>10 20 30 40</posList></LineString></curveMembers></MultiCurve>",
                "<MultiSurface><surfaceMembers><Polygon/></surfaceMembers></MultiSurface>",
                "<MultiGeometry><geometryMembers><Point><pos>10 20</pos></Point><MultiGeometry/></geometryMembers></MultiGeometry>"
            })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var input = GmlXmlInput.FromXml(WithNamespace(body));
                    var raw = GmlCapture.Read(input, geography, false);
                    var value = GmlCapture.Read(input, geography, true);
                    Assert.Null(raw.Error);
                    Assert.Null(value.Error);
                    Assert.Contains("end", value.Events);
                }
            }
        }

        [Fact]
        public void ReadsEmptyShapeKinds()
        {
            foreach (string kind in new[] { "Point", "LineString", "Polygon", "MultiPoint", "MultiCurve", "MultiSurface", "MultiGeometry" })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var result = GmlCapture.Read(GmlXmlInput.FromXml(WithNamespace($"<{kind}/>")), geography, true);
                    Assert.Null(result.Error);
                    Assert.True(result.IsEmpty);
                    Assert.Empty(result.Positions);
                }
            }
        }

        [Fact]
        public void ReadsAsymmetricPositionsAndIndependentOrdinates()
        {
            foreach (var (text, expectedZ, expectedM) in new[]
            {
                ("10 20", (double?)null, (double?)null),
                ("10 20 0", (double?)0, (double?)null),
                ("10 20 NaN 7", (double?)null, (double?)7),
                ("10 20 3 7", (double?)3, (double?)7),
                ("10 20 3 7 999", (double?)3, (double?)7)
            })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var result = GmlCapture.Read(GmlXmlInput.FromXml(WithNamespace($"<Point><pos>{text}</pos></Point>")), geography, true);
                    Assert.Null(result.Error);
                    var position = Assert.Single(result.Positions);
                    Assert.Equal(10.0, position[0]);
                    Assert.Equal(20.0, position[1]);
                    Assert.Equal(expectedZ, position[2]);
                    Assert.Equal(expectedM, position[3]);
                }
            }
        }

        [Fact]
        public void ReadsNamespacesAttributesAndMetadata()
        {
            foreach (string xml in new[]
            {
                $"<p:Point xmlns:p='{Gml}' p:srsName='http://www.opengis.net/def/crs/EPSG/0/1234'><p:pos>10 20</p:pos></p:Point>",
                WithNamespace("<LineString><name>label</name><posList srsDimension='3'>10 20 3 30 40 4</posList><description/></LineString>"),
                WithNamespace("<MultiPoint><pointMember> <!--before--> <Point><pos>10 20</pos></Point> <?after ok?> </pointMember></MultiPoint>"),
                WithNamespace("<LineString><posList>10 <![CDATA[20 30]]> 40</posList></LineString>")
            })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var result = GmlCapture.Read(GmlXmlInput.FromXml(xml), geography, true);
                    Assert.Null(result.Error);
                }
            }
        }

        [Fact]
        public void PreservesFollowingCursorAndFullGlobe()
        {
            foreach (bool geography in new[] { true, false })
            {
                var input = GmlXmlInput.FromXml(WithNamespace("<Point><pos>10 20</pos></Point>") + "<next/>");
                foreach (bool materialize in new[] { false, true })
                {
                    var result = GmlCapture.Read(input, geography, materialize);
                    Assert.Null(result.Error);
                    Assert.Equal("start", result.CursorKind);
                    Assert.Equal("next", result.CursorName);
                }
            }
            var globe = GmlCapture.Read(GmlXmlInput.FromXml("<FullGlobe xmlns='http://schemas.microsoft.com/sqlserver/2011/geography'/>"), true, true);
            Assert.Null(globe.Error);
            Assert.Contains("begin:FullGlobe", globe.Events);
        }

        [Fact]
        public void RecordsRawFailuresAndSeparatesValidation()
        {
            foreach (string body in new[]
            {
                "<Point foo='bar'><pos>10 20</pos></Point>",
                "<Point><pos>10</pos></Point>",
                "<LineString><posList>10 20 30</posList></LineString>",
                "<LineString><posList>10 20 30 bad</posList></LineString>",
                "<Surface/>"
            })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var input = GmlXmlInput.FromXml(WithNamespace(body));
                    Assert.NotNull(GmlCapture.Read(input, geography, false).Error);
                    Assert.NotNull(GmlCapture.Read(input, geography, true).Error);
                }
            }
            var invalid = GmlXmlInput.FromXml(WithNamespace("<Point><pos>100 20</pos></Point>"));
            Assert.Null(GmlCapture.Read(invalid, true, false).Error);
            Assert.NotNull(GmlCapture.Read(invalid, true, true).Error);
        }

        [Fact]
        public void RecordsDepthBoundaries()
        {
            foreach (int depth in new[] { 28, 29 })
            {
                string body = string.Concat(Enumerable.Repeat("<MultiGeometry><geometryMembers>", depth - 1)) +
                    "<Point><pos>10 20</pos></Point>" +
                    string.Concat(Enumerable.Repeat("</geometryMembers></MultiGeometry>", depth - 1));
                foreach (bool geography in new[] { true, false })
                {
                    var input = GmlXmlInput.FromXml(WithNamespace(body));
                    GmlCapture.Read(input, geography, false);
                    var result = GmlCapture.Read(input, geography, true);
                    if (depth == 28) { Assert.Null(result.Error); }
                    else { Assert.NotNull(result.Error); }
                }
            }
        }

        [Fact]
        public void WritesShapesAndOptionalOrdinates()
        {
            foreach (bool geography in new[] { true, false })
            {
                foreach (string kind in new[] { "Point", "LineString", "Polygon", "MultiPoint", "MultiLineString", "MultiPolygon", "Collection" })
                {
                    var empty = Script(geography, new[] { "crs", "begin:" + kind, "end" });
                    Assert.NotEmpty(GmlCapture.WriteXml(empty, geography));
                    Assert.Null(GmlCapture.WritePipeline(empty, 0).Error);
                }
                foreach (double?[] p in new[]
                {
                    new double?[] { 10, 20, null, null }, new double?[] { 10, 20, 0, null },
                    new double?[] { 10, 20, null, 7 }, new double?[] { 10, 20, 3, 7 }
                })
                {
                    var point = Script(geography, new[] { "crs", "begin:Point", "begin_figure", "end_figure", "end" }, p);
                    Assert.Contains("10 20", GmlCapture.WriteXml(point, geography));
                    Assert.Null(GmlCapture.WritePipeline(point, 0).Error);
                }
            }
            Assert.Contains("FullGlobe", GmlCapture.WriteXml(Script(true, new[] { "crs", "begin:FullGlobe", "end" }), true));
        }

        [Fact]
        public void RecordsSharedCrsHistoryAndReset()
        {
            var first = Script(true, new[] { "crs", "begin:Point", "end" });
            var second = Script(false, new[] { "crs", "begin:Point", "end" });
            var result = GmlCapture.WritePipeline(Concat(first, second), 0);
            Assert.Single(result.Events, x => x.StartsWith("attribute::srsName:", StringComparison.Ordinal));
            var reset = Script(false, new[] { "reset" });
            result = GmlCapture.WritePipeline(Concat(first, reset, second), 0);
            Assert.Equal(2, result.Events.Count(x => x.StartsWith("attribute::srsName:", StringComparison.Ordinal)));
            Assert.Equal(0, result.Flushes);
            Assert.Equal(0, result.Closes);
        }

        [Fact]
        public void RecordsFailingTextSinkPrefix()
        {
            foreach (bool geography in new[] { true, false })
            {
                var point = Script(geography, new[] { "crs", "begin:Point", "begin_figure", "end_figure", "end" },
                    new double?[] { 10, 20, 3, 7 });
                var result = GmlCapture.WritePipeline(point, 1);
                Assert.Equal("injected text failure", result.Error);
                Assert.Contains("text:10", result.Events);
                Assert.Equal(0, result.Flushes);
                Assert.Equal(0, result.Closes);
            }
        }

        [Fact]
        public void KeepsXmlDecoderAndNonFiniteCasesNativeOnly()
        {
            Assert.Throws<XmlException>(() => GmlXmlInput.FromXml("<Point>"));
            using var reader = XmlReader.Create(new System.IO.StringReader(WithNamespace("<Point><pos>NaN 20</pos></Point>")));
            Assert.Throws<ParseErrorException>(() => GmlFormatter.Create().Read<Geography>(reader));
        }

        private static string WithNamespace(string body)
        {
            int end = body.IndexOf('>');
            int insertion = body[end - 1] == '/' ? end - 1 : end;
            return body.Insert(insertion, " xmlns='" + Gml + "'");
        }

        private static GmlShapeInput Script(bool geography, string[] steps, params double?[][] positions)
        {
            int position = 0;
            return new GmlShapeInput
            {
                Steps = steps,
                Geographies = steps.Select(_ => geography).ToArray(),
                Srids = steps.Select(_ => (int?)1234).ToArray(),
                Positions = steps.Select(s => s == "begin_figure" || s == "line_to" ? positions[position++] : Array.Empty<double?>()).ToArray()
            };
        }

        private static GmlShapeInput Concat(params GmlShapeInput[] scripts) => new()
        {
            Steps = scripts.SelectMany(s => s.Steps).ToArray(),
            Geographies = scripts.SelectMany(s => s.Geographies).ToArray(),
            Srids = scripts.SelectMany(s => s.Srids).ToArray(),
            Positions = scripts.SelectMany(s => s.Positions).ToArray()
        };
    }
}
