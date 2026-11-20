#nullable enable

namespace Microsoft.Spatial.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Xunit;

    public class GeoJsonCaptureTests
    {
        [Fact]
        public void ReadsAllShapeFamiliesForBothTopologies()
        {
            foreach (var source in ShapeObjects())
            {
                foreach (bool geography in new[] { true, false })
                {
                    var actual = Read(source, geography);
                    var formatter = GeoJsonObjectFormatter.Create();
                    var expected = geography
                        ? GeoJsonCaptureAdapter.Observe(formatter.Read<Geography>(source))
                        : GeoJsonCaptureAdapter.Observe(formatter.Read<Geometry>(source));
                    Assert.True(actual.HasValue);
                    Assert.Equal(expected.Events.Select(SpatialKey), actual.Events.Select(SpatialKey));
                }
            }
        }

        [Fact]
        public void ReadsEmptyShapesWithoutInventingPositions()
        {
            foreach (string kind in ShapeNames())
            {
                foreach (bool geography in new[] { true, false })
                {
                    var result = Read(Shape(kind, Array.Empty<object>()), geography);
                    Assert.True(result.HasValue);
                    Assert.DoesNotContain(result.Events, item => item.Name == "begin_figure" || item.Name == "line_to");
                }
            }
        }

        [Fact]
        public void PreservesCoordinateOrderAndOptionalOrdinates()
        {
            foreach (object[] coordinates in new[]
            {
                new object[] { 20.0, 10.0 },
                new object[] { 20.0, 10.0, 0.0 },
                new object[] { 20.0, 10.0, null!, 7.0 },
                new object[] { 20.0, 10.0, 3.0, 7.0 },
                new object[] { 20.0, 10.0, null!, null! }
            })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var result = Read(Shape("Point", coordinates), geography);
                    var position = Assert.Single(result.Events, item => item.Name == "begin_figure");
                    Assert.Equal(geography ? 10.0 : 20.0, position.First);
                    Assert.Equal(geography ? 20.0 : 10.0, position.Second);
                    Assert.Equal(coordinates.Length > 2 ? (double?)coordinates[2] : null, position.Z);
                    Assert.Equal(coordinates.Length > 3 ? (double?)coordinates[3] : null, position.M);
                }
            }
        }

        [Fact]
        public void PreservesMaterializedNumericInputTypes()
        {
            foreach (object number in new object[] { 20, 20L, 20.5f, 20.5, 20.50m })
            {
                var source = Shape("Point", new object[] { number, 10.0 });
                var input = GeoJsonCaptureValueInput.FromValue(source);
                var rebuilt = Assert.IsType<Dictionary<string, object>>(input.Materialize());
                var coordinates = Assert.IsType<List<object>>(rebuilt["coordinates"]);
                Assert.Equal(number.GetType(), coordinates[0].GetType());
                Assert.Equal(number, coordinates[0]);
                Assert.True(GeoJsonCaptureAdapter.ReadGeometry(input, false).HasValue);
                Assert.True(GeoJsonCaptureAdapter.ReadGeography(input, false).HasValue);
            }
        }

        [Fact]
        public void ReadsDefaultExplicitAndInheritedCoordinateSystems()
        {
            foreach (bool geography in new[] { true, false })
            {
                var point = Shape("Point", new object[] { 20.0, 10.0 });
                Assert.Equal(geography ? 4326 : 0, Read(point, geography).Events.First().Srid);
                foreach (string srid in new[] { "1234", "-1", "2147483647", "-2147483648", " +1234 " })
                {
                    point["crs"] = Crs(srid);
                    Assert.True(Read(point, geography).HasValue);
                }

                point.Remove("crs");
                var collection = Shape("GeometryCollection", new object[] { point });
                collection["crs"] = Crs("1234");
                var inherited = Read(collection, geography);
                Assert.All(inherited.Events.Where(item => item.Name == "set_crs"), item => Assert.Equal(1234, item.Srid));
                point["crs"] = Crs("4321");
                var changedChild = Read(collection, geography);
                Assert.Contains(changedChild.Events, item => item.Name == "set_crs" && item.Srid == 4321);
            }
        }

        [Fact]
        public void SingletonLineHasDifferentBaseAndConcreteReadResults()
        {
            var source = Shape("LineString", new object[] { new object[] { 20.0, 10.0 } });
            var input = GeoJsonCaptureValueInput.FromValue(source);
            foreach (bool geography in new[] { true, false })
            {
                var asBase = geography
                    ? GeoJsonCaptureAdapter.ReadGeography(input, false)
                    : GeoJsonCaptureAdapter.ReadGeometry(input, false);
                var asLine = geography
                    ? GeoJsonCaptureAdapter.ReadGeography(input, true)
                    : GeoJsonCaptureAdapter.ReadGeometry(input, true);
                Assert.True(asBase.HasValue);
                Assert.Contains(asBase.Events, item => item.Name == "begin_shape" && item.Shape == "Point");
                Assert.False(asLine.HasValue);
                Assert.Empty(asLine.Events);
            }
        }

        [Fact]
        public void RejectsMalformedMaterializedMembersAndPositions()
        {
            var invalid = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>(),
                new Dictionary<string, object> { { "type", "Point" } },
                Shape("point", new object[] { 20.0, 10.0 }),
                Shape("Unknown", new object[] { 20.0, 10.0 }),
                Shape("Point", new object[] { 20.0 }),
                Shape("Point", new object[] { 20.0, 10.0, 3.0, 4.0, 5.0 }),
                Shape("Point", new object[] { null!, 10.0 }),
                Shape("Point", new object[] { 20.0, null! }),
                Shape("Point", new object[] { true, 10.0 }),
                Shape("Point", new object[] { "20", 10.0 }),
                Shape("Point", new object[] { new object[] { 20.0 }, 10.0 }),
                Shape("Point", 20.0),
                Shape("Polygon", new object[] { 20.0 })
            };
            foreach (var source in invalid)
            {
                Assert.Throws<ParseErrorException>(() => Read(source, true));
                Assert.Throws<ParseErrorException>(() => Read(source, false));
            }
        }

        [Fact]
        public void RejectsMalformedExplicitCoordinateSystems()
        {
            foreach (object crs in new object[]
            {
                "EPSG:4326",
                new Dictionary<string, object>(),
                Crs("1.5"),
                Crs("2147483648")
            })
            {
                var source = Shape("Point", new object[] { 20.0, 10.0 });
                source["crs"] = crs;
                Assert.Throws<ParseErrorException>(() => Read(source, true));
                Assert.Throws<ParseErrorException>(() => Read(source, false));
            }
        }

        [Fact]
        public void ExplicitNullCrsPreservesNativeNullReferenceFault()
        {
            var source = Shape("Point", new object[] { 20.0, 10.0 });
            source["crs"] = null!;
            Assert.Throws<NullReferenceException>(() => Read(source, true));
            Assert.Throws<NullReferenceException>(() => Read(source, false));
        }

        [Fact]
        public void ReadsShortAndOpenRingsWithoutValidatorRepair()
        {
            foreach (object[] ring in new[]
            {
                new object[] { new object[] { 0.0, 0.0 }, new object[] { 1.0, 0.0 }, new object[] { 1.0, 1.0 } },
                new object[] { new object[] { 0.0, 0.0 }, new object[] { 1.0, 0.0 }, new object[] { 1.0, 1.0 }, new object[] { 0.0, 1.0 } }
            })
            {
                foreach (bool geography in new[] { true, false })
                {
                    var result = Read(Shape("Polygon", new object[] { ring }), geography);
                    Assert.Contains(result.Events, item => item.Name == "begin_shape" && item.Shape == "Polygon");
                    Assert.Equal(ring.Length - 1, result.Events.Count(item => item.Name == "line_to"));
                }
            }
        }

        [Fact]
        public void MaterializedGeographyReadDoesNotApplyValidatorRanges()
        {
            foreach (object[] coordinates in new[]
            {
                new object[] { 20.0, 100.0 },
                new object[] { 15070.0, 10.0 },
                new object[] { -15070.0, -100.0 }
            })
            {
                var result = Read(Shape("Point", coordinates), true);
                var position = Assert.Single(result.Events, item => item.Name == "begin_figure");
                Assert.Equal((double)coordinates[1], position.First);
                Assert.Equal((double)coordinates[0], position.Second);
            }
        }

        [Fact]
        public void NativeOnlyNonFiniteOrdinatesRemainOutsideTheTraceBoundary()
        {
            // The pinned trace writer encodes non-finite f64 as JSON null.
            // Keep this native evidence rather than publish a lossy observation.
            var formatter = GeoJsonObjectFormatter.Create();
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var invalid = Shape("Point", new object[] { value, 10.0 });
                Assert.Throws<ParseErrorException>(() => formatter.Read<Geography>(invalid));
                Assert.Throws<ParseErrorException>(() => formatter.Read<Geometry>(invalid));
                var optional = Shape("Point", new object[] { 20.0, 10.0, value, value });
                var geography = Assert.IsAssignableFrom<GeographyPoint>(formatter.Read<Geography>(optional));
                var geometry = Assert.IsAssignableFrom<GeometryPoint>(formatter.Read<Geometry>(optional));
                Assert.Equal(value, geography.Z);
                Assert.Equal(value, geography.M);
                Assert.Equal(value, geometry.Z);
                Assert.Equal(value, geometry.M);
            }
        }

        [Fact]
        public void IgnoresUnusedMembersAndPreservesEmptyChildren()
        {
            var point = Shape("Point", new object[] { 20.0, 10.0 });
            point["geometries"] = false;
            point["extension"] = new object[] { true, "retained input", null! };
            var collection = Shape("GeometryCollection", new object[]
            {
                point,
                Shape("LineString", Array.Empty<object>()),
                Shape("GeometryCollection", Array.Empty<object>())
            });
            collection["coordinates"] = false;
            foreach (bool geography in new[] { true, false })
            {
                var result = Read(collection, geography);
                Assert.Equal(new[] { "Collection", "Point", "LineString", "Collection" },
                    result.Events.Where(item => item.Name == "begin_shape").Select(item => item.Shape));
            }
        }

        [Fact]
        public void ReadsAtSharedDepthBoundaryAndOneLevelBeyond()
        {
            foreach (int depth in new[] { 28, 29 })
            {
                var source = Shape("Point", new object[] { 20.0, 10.0 });
                for (int level = 1; level < depth; level++)
                {
                    source = Shape("GeometryCollection", new object[] { source });
                }

                foreach (bool geography in new[] { true, false })
                {
                    var result = Read(source, geography);
                    Assert.Equal(depth, result.Events.Count(item => item.Name == "begin_shape"));
                }
            }
        }

        [Fact]
        public void WritesAllShapeFamiliesAndEmptyChildrenToMaterializedValues()
        {
            var sources = ShapeObjects().Concat(ShapeNames().Select(kind => Shape(kind, Array.Empty<object>())));
            foreach (var source in sources)
            {
                foreach (bool geography in new[] { true, false })
                {
                    var input = SpatialInput(source, geography);
                    var actual = GeoJsonCaptureAdapter.WriteMaterialized(input);
                    Assert.Equal("object", actual.First().Kind);
                    Assert.Contains(actual, item => item.Name == "crs" && item.Kind == "object");
                    Assert.Contains(actual, item => item.Name == "type" && item.Kind == "string");
                    Assert.DoesNotContain(actual, item => item.Parent > 0 && item.Name == "crs");
                }
            }
        }

        [Fact]
        public void WriterMOnlyEmitsAnExplicitNullAndPreservesOrder()
        {
            foreach (bool geography in new[] { true, false })
            {
                var source = Shape("Point", new object[] { 20.0, 10.0, null!, 7.0 });
                source["crs"] = Crs("1234");
                var input = SpatialInput(source, geography);
                var materialized = GeoJsonCaptureAdapter.WriteMaterialized(input);
                Assert.Equal(new[] { "double", "double", "null", "double" },
                    materialized.Where(item => item.Name == "" && item.Parent >= 0).Select(item => item.Kind));
                var result = GeoJsonCaptureAdapter.WriteEvents(input, 0);
                Assert.True(result.Succeeded);
                Assert.Equal(new[] { 20.0, 10.0, 7.0 }, result.Events.Where(item => item.Number.HasValue).Select(item => item.Number!.Value));
                Assert.Single(result.Events, item => item.Name == "null_value");
                Assert.Contains(result.Events, item => item.Name == "string_value" && item.Text == "EPSG:1234");
            }
        }

        [Fact]
        public void RawWriterPreservesSingletonLineKind()
        {
            foreach (bool geography in new[] { true, false })
            {
                var input = GeoJsonCaptureSpatialInput.FromEvents(geography, new List<GeoJsonCaptureSpatialEvent>
                {
                    new GeoJsonCaptureSpatialEvent { Name = "set_crs", Srid = geography ? 4326 : 0 },
                    new GeoJsonCaptureSpatialEvent { Name = "begin_shape", Shape = "LineString" },
                    new GeoJsonCaptureSpatialEvent { Name = "begin_figure", First = 10.0, Second = 20.0 },
                    new GeoJsonCaptureSpatialEvent { Name = "end_figure" },
                    new GeoJsonCaptureSpatialEvent { Name = "end_shape" }
                });
                var result = GeoJsonCaptureAdapter.WriteEvents(input, 0);
                Assert.True(result.Succeeded);
                Assert.Contains(result.Events, item => item.Name == "string_value" && item.Text == "LineString");
            }
        }

        [Fact]
        public void SinkFailureRetainsEveryAttemptedCallbackPrefix()
        {
            foreach (bool geography in new[] { true, false })
            {
                var input = SpatialInput(Shape("Point", new object[] { 20.0, 10.0, null!, 7.0 }), geography);
                var baseline = GeoJsonCaptureAdapter.WriteEvents(input, 0);
                Assert.True(baseline.Succeeded);
                foreach (string kind in new[]
                {
                    "start_object", "end_object", "start_array", "end_array",
                    "property_name", "number_value", "string_value", "null_value"
                })
                {
                    int failAt = baseline.Events.FindIndex(item => item.Name == kind) + 1;
                    Assert.True(failAt > 0);
                    var failed = GeoJsonCaptureAdapter.WriteEvents(input, failAt);
                    Assert.False(failed.Succeeded);
                    Assert.Equal(typeof(InvalidOperationException).FullName, failed.FailureType);
                    Assert.Equal("GeoJSON capture sink rejected callback " + failAt, failed.FailureMessage);
                    Assert.Equal(baseline.Events.Take(failAt).Select(WriterKey), failed.Events.Select(WriterKey));
                }
            }
        }

        [Fact]
        public void ValueTablePreservesNullAbsenceAndRejectsBrokenMetadata()
        {
            var source = new Dictionary<string, object>
            {
                { "presentNull", null! },
                { "values", new object[] { false, 1, 2L, 3.25f, 4.5, 5.50m, "text" } }
            };
            var input = GeoJsonCaptureValueInput.FromValue(source);
            var rebuilt = Assert.IsType<Dictionary<string, object>>(input.Materialize());
            Assert.True(rebuilt.ContainsKey("presentNull"));
            Assert.Null(rebuilt["presentNull"]);
            Assert.False(rebuilt.ContainsKey("absent"));
            Assert.Equal(GeoJsonCaptureValueInput.Flatten(source).Select(NodeKey),
                GeoJsonCaptureValueInput.Flatten(rebuilt).Select(NodeKey));
            input.Parents[1] = input.Parents.Length;
            Assert.Throws<InvalidOperationException>(() => input.Materialize());
        }

        private static GeoJsonCaptureShapeResult Read(Dictionary<string, object> source, bool geography)
        {
            var input = GeoJsonCaptureValueInput.FromValue(source);
            return geography
                ? GeoJsonCaptureAdapter.ReadGeography(input, false)
                : GeoJsonCaptureAdapter.ReadGeometry(input, false);
        }

        private static GeoJsonCaptureSpatialInput SpatialInput(Dictionary<string, object> source, bool geography)
        {
            var formatter = GeoJsonObjectFormatter.Create();
            var value = geography
                ? GeoJsonCaptureAdapter.Observe(formatter.Read<Geography>(source))
                : GeoJsonCaptureAdapter.Observe(formatter.Read<Geometry>(source));
            return GeoJsonCaptureSpatialInput.FromEvents(geography, value.Events);
        }

        private static Dictionary<string, object> Shape(string kind, object coordinates)
            => new Dictionary<string, object>
            {
                { "type", kind },
                { kind == "GeometryCollection" ? "geometries" : "coordinates", coordinates }
            };

        private static Dictionary<string, object> Crs(string value)
            => new Dictionary<string, object>
            {
                { "type", "name" },
                { "properties", new Dictionary<string, object> { { "name", "EPSG:" + value } } }
            };

        private static string[] ShapeNames()
            => new[] { "Point", "LineString", "Polygon", "MultiPoint", "MultiLineString", "MultiPolygon", "GeometryCollection" };

        private static IEnumerable<Dictionary<string, object>> ShapeObjects()
        {
            object[] point = { 20.0, 10.0 };
            object[] line = { point, new object[] { 21.0, 11.0 } };
            object[] ring = { point, new object[] { 21.0, 10.0 }, new object[] { 21.0, 11.0 }, point };
            object[] polygon = { ring };
            yield return Shape("Point", point);
            yield return Shape("LineString", line);
            yield return Shape("Polygon", polygon);
            yield return Shape("MultiPoint", line);
            yield return Shape("MultiLineString", new object[] { line });
            yield return Shape("MultiPolygon", new object[] { polygon });
            yield return Shape("GeometryCollection", new object[]
            {
                Shape("Point", point),
                Shape("GeometryCollection", new object[] { Shape("LineString", line) }),
                Shape("Point", Array.Empty<object>())
            });
        }

        private static object SpatialKey(GeoJsonCaptureSpatialEvent item)
            => (item.Name, item.Srid, item.Shape, item.First, item.Second, item.Z, item.M);
        private static object WriterKey(GeoJsonCaptureWriterEvent item) => (item.Name, item.Text, item.Number);
        private static object NodeKey(GeoJsonCaptureJsonNode item)
            => (item.Parent, item.Name, item.Kind, item.Number, item.Integer, item.Text);
    }
}
