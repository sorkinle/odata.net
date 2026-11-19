namespace Microsoft.Spatial.Tests
{
    using System;
    using Xunit;

    public sealed class WktCaptureTests
    {
        private static readonly string[] Point = { "begin:Point", "begin_figure", "end_figure", "end" };
        private static readonly string[] FullGlobe = { "begin:FullGlobe", "end" };
        private static readonly double?[][] Point2D = { new double?[] { 20, 10, null, null } };
        private static readonly double?[][] PointM = { new double?[] { 20, 10, null, 40.25 } };
        private static readonly double?[][] NoPositions = Array.Empty<double?[]>();

        [Fact]
        public void PositionedMaterializedReadsKeepOrderDimensionsAndSuffix()
        {
            // Same asymmetric point as ReadPoint_Ordering and ReadSRIDTest.
            var geography = WktCapture.ReadValue("SRID=1234;POINT(10 20)POINT(30 40)", true, false);
            Assert.False(geography.IsEmpty);
            Assert.Equal(new int?[] { 1234 }, geography.Srids);
            Assert.Equal(new double?[] { 20, 10, null, null }, Assert.Single(geography.Positions));
            Assert.Equal("POINT(30 40)", geography.Remaining);

            var geometry = WktCapture.ReadValue("pOiNt (10 20 NULL 40)", false, false);
            Assert.Equal(new int?[] { 0 }, geometry.Srids);
            Assert.Equal(new double?[] { 10, 20, null, 40 }, Assert.Single(geometry.Positions));

            var empty = WktCapture.ReadValue("POINT EMPTY", true, false);
            Assert.True(empty.IsEmpty);
            Assert.Empty(empty.Positions);
        }

        [Fact]
        public void MaterializedReadsKeepShapeFamiliesAndCollectionOrder()
        {
            const string text = "GEOMETRYCOLLECTION(POINT(10 20 30),LINESTRING(10 20,30 40)," +
                "POLYGON((0 0,10 0,10 10,0 0)),MULTIPOINT((10 20),EMPTY)," +
                "MULTILINESTRING((10 20,30 40)),MULTIPOLYGON(((0 0,10 0,10 10,0 0)))," +
                "GEOMETRYCOLLECTION(POINT(10 20 30 40)))";
            foreach (bool geography in new[] { true, false })
            {
                var result = WktCapture.ReadValue(text, geography, false);
                Assert.False(result.IsEmpty);
                Assert.Equal(new[]
                {
                    "begin:Collection", "begin:Point", "begin:LineString", "begin:Polygon", "begin:MultiPoint",
                    "begin:Point", "begin:Point", "begin:MultiLineString", "begin:LineString",
                    "begin:MultiPolygon", "begin:Polygon", "begin:Collection", "begin:Point",
                }, Array.FindAll(result.Events, step => step.StartsWith("begin:", StringComparison.Ordinal)));
            }
        }

        [Fact]
        public void PipelineReadsKeepEventsAndPartialFailure()
        {
            var geography = WktCapture.ReadPipeline("POINT(10 20 NULL 40)POINT(10 30 NULL 40)", true, false);
            Assert.Equal(new[] { "srid", "begin:Point", "begin_figure", "end_figure", "end" }, geography.Events);
            Assert.Equal(new double?[] { 20, 10, null, 40 }, Assert.Single(geography.Positions));
            Assert.Equal("POINT(10 30 NULL 40)", geography.Remaining);

            var geometry = WktCapture.ReadPipeline("LINESTRING(10 20,30 40)", false, true);
            Assert.Equal(new[] { "srid", "begin:LineString", "begin_figure", "line_to", "end_figure", "end" }, geometry.Events);
            Assert.Null(geometry.Error);

            var partial = WktCapture.ReadPipeline("LINESTRING(10 20,30 40", false, false);
            Assert.NotNull(partial.Error);
            Assert.Contains("begin_figure", partial.Events);
        }

        [Fact]
        public void TwoDimensionalReadersRejectAdditionalSlots()
        {
            // The first input is from ErrorOnRead3DPoint; NULL exercises a distinct slot.
            foreach (bool geography in new[] { true, false })
            {
                Assert.Throws<ParseErrorException>(() => WktCapture.ReadValue("SRID=1234;POINT(10 20 30)", geography, true));
                Assert.Throws<ParseErrorException>(() => WktCapture.ReadValue("POINT(10 20 NULL)", geography, true));
            }
        }

        [Fact]
        public void WritersKeepModeCultureAndTopologySeparate()
        {
            Assert.Equal("SRID=4326;POINT (10 20 NULL 40.25)", WktCapture.WriteText(true, false, null, Point, PointM));
            Assert.Equal("SRID=0;POINT (20 10)", WktCapture.WriteText(false, true, null, Point, PointM));
            Assert.Equal("SRID=1234;POINT (10 20)", WktCapture.WriteText(true, true, 1234, Point, PointM));
            Assert.Equal("SRID=0;POINT (20 10 NULL 40,25)", WktCapture.WriteStream(false, false, null, Point, PointM, "fr-FR"));
            Assert.Equal("SRID=0;POINT (20 10 NULL 40.25)", WktCapture.WriteStream(false, false, null, Point, PointM, ""));
            Assert.Equal("SRID=4326;POINT (10 20 NULL 40,25)", WktCapture.WritePipeline(true, false, null, Point, PointM, "fr-FR"));
            Assert.Equal("SRID=1234;POINT (20 10)", WktCapture.WritePipeline(false, true, 1234, Point, PointM, ""));
        }

        [Fact]
        public void WriterRetainsNestedMemberOrderAndEmptiness()
        {
            string[] events =
            {
                "begin:Collection", "begin:Point", "end", "begin:Collection", "begin:Point",
                "begin_figure", "end_figure", "end", "end", "end",
            };
            const string expected = "SRID=4326;GEOMETRYCOLLECTION (POINT EMPTY, GEOMETRYCOLLECTION (POINT (10 20)))";
            Assert.Equal(expected, WktCapture.WriteText(true, false, null, events, Point2D));
            Assert.Equal(expected, WktCapture.WritePipeline(true, false, null, events, Point2D, ""));
        }

        [Fact]
        public void FullGlobeReadAndWriteAreIndependent()
        {
            var observation = WktCapture.ReadValue("FULLGLOBE)", true, false);
            Assert.Contains("begin:FullGlobe", observation.Events);
            Assert.Equal(")", observation.Remaining);
            Assert.Equal("SRID=4326;FULLGLOBE)", WktCapture.WriteText(true, false, null, FullGlobe, NoPositions));
            Assert.Equal("SRID=4326;FULLGLOBE)", WktCapture.WritePipeline(true, false, null, FullGlobe, NoPositions, ""));
        }

        [Fact]
        public void XmlExtensionWritesTheRealGmlForEachTopology()
        {
            string geography = WktCapture.WriteXmlExtension(true, null, Point, Point2D);
            Assert.StartsWith("<gml:Point ", geography);
            Assert.Contains(">20 10</gml:pos>", geography);
            string geometry = WktCapture.WriteXmlExtension(false, null, Point, Point2D);
            Assert.StartsWith("<gml:Point ", geometry);
            Assert.Contains(">20 10</gml:pos>", geometry);
        }
    }
}
