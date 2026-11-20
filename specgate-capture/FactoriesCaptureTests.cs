using System;
using System.Linq;
using Xunit;

namespace Microsoft.Spatial.Tests
{
    public class FactoriesCaptureTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AutoCloseCopiesAllOrdinates(bool geography)
        {
            var first = (10.0, 0.0, (double?)1.0, (double?)100.0);
            var ring = new[] { first, (20.0, 20.0, (double?)null, (double?)null), (20.0, 40.0, (double?)null, (double?)null) };
            var result = BuildPolygon(geography, 1234, new[] { ring });
            Assert.Equal(new[] { ring[0], ring[1], ring[2], first }, ReadRings(result)[0]);
            Assert.Equal(1234, result.CoordinateSystem.EpsgId);
            Assert.False(result.IsEmpty);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExactEndpointsPreserveDifferentZM(bool geography)
        {
            var ring = new[]
            {
                (10.0, 0.0, (double?)1.0, (double?)100.0),
                (20.0, 20.0, (double?)null, (double?)null),
                (20.0, 40.0, (double?)null, (double?)null),
                (10.0, 0.0, (double?)9.0, (double?)999.0)
            };
            var result = BuildPolygon(geography, null, new[] { ring });
            Assert.Equal(ring, ReadRings(result)[0]);
        }

        [Fact]
        public void GeographyLongitudeEquivalentEndpointAppendsCompleteFirstPosition()
        {
            var ring = new[]
            {
                (10.0, 0.0, (double?)1.0, (double?)100.0),
                (20.0, 20.0, (double?)null, (double?)null),
                (20.0, 40.0, (double?)null, (double?)null),
                (10.0, 360.0, (double?)9.0, (double?)999.0)
            };
            var result = BuildPolygon(true, null, new[] { ring });
            Assert.Equal(new[] { ring[0], ring[1], ring[2], ring[3], ring[0] }, ReadRings(result)[0]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IndependentOrdinatesAndZeroRemainPresent(bool geography)
        {
            var ring = new[]
            {
                (10.0, 20.0, (double?)null, (double?)0.0),
                (20.0, 30.0, (double?)0.0, (double?)null),
                (30.0, 40.0, (double?)null, (double?)null)
            };
            var result = BuildPolygon(geography, null, new[] { ring });
            Assert.Equal(new[] { ring[0], ring[1], ring[2], ring[0] }, ReadRings(result)[0]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompletedObservationPreservesRingOrder(bool geography)
        {
            var rings = new[]
            {
                new[] { (10.0, 20.0, (double?)1.0, (double?)2.0), (20.0, 30.0, (double?)null, (double?)null), (30.0, 40.0, (double?)null, (double?)null) },
                new[] { (15.0, 25.0, (double?)3.0, (double?)4.0), (25.0, 35.0, (double?)null, (double?)null), (35.0, 45.0, (double?)null, (double?)null) }
            };
            var result = BuildPolygon(geography, 1234, rings);
            Assert.Equal(2, result.FirstCoordinates.Length);
            for (int index = 0; index < rings.Length; index++)
            {
                var ring = rings[index];
                Assert.Equal(new[] { ring[0], ring[1], ring[2], ring[0] }, ReadRings(result)[index]);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompletedObservationPreservesEmptyPolygonAndDefaultCrs(bool geography)
        {
            var result = BuildPolygon(geography, null, Array.Empty<(double, double, double?, double?)[]>());
            Assert.True(result.IsEmpty);
            Assert.Empty(result.FirstCoordinates);
            Assert.Empty(result.SecondCoordinates);
            Assert.Empty(result.Z);
            Assert.Empty(result.M);
            Assert.Equal(geography ? 4326 : 0, result.CoordinateSystem.EpsgId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompletedBoundaryReportsShortRing(bool geography)
        {
            var rings = new[] { new[] { (10.0, 20.0, (double?)null, (double?)null) } };
            Assert.Throws<FormatException>(() => BuildPolygon(geography, null, rings));
        }

        [Fact]
        public void CompletedGeographyBoundaryReportsInvalidLatitude()
        {
            var rings = new[]
            {
                new[] { (91.0, 20.0, (double?)null, (double?)null), (20.0, 30.0, (double?)null, (double?)null), (30.0, 40.0, (double?)null, (double?)null) }
            };
            Assert.Throws<FormatException>(() => BuildPolygon(true, null, rings));
        }

        [Fact]
        public void NativeGeographyFactoryRejectsInvalidLatitudeAtRing()
        {
            var factory = GeographyFactory.Polygon();
            Assert.Throws<FormatException>(() => factory.Ring(91, 20));
        }

        [Fact]
        public void NativeGeometryFactoryRejectsNonFiniteCoordinateAtRing()
        {
            var factory = GeometryFactory.Polygon();
            Assert.Throws<FormatException>(() => factory.Ring(double.NaN, 20));
        }

        [Fact]
        public void NativeGeographyBuildCanReadTheCompletedValueAgain()
        {
            var factory = GeographyFactory.Point(10, 20);
            var first = factory.Build();
            Assert.Same(first, factory.Build());
        }

        [Fact]
        public void NativeGeometryBuildCanReadTheCompletedValueAgain()
        {
            var factory = GeometryFactory.Point(10, 20);
            var first = factory.Build();
            Assert.Same(first, factory.Build());
        }

        [Fact]
        public void NullGeographyFactoryConversionThrows()
        {
            GeographyFactory<GeographyPoint> factory = null;
            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                GeographyPoint point = factory;
            });
            Assert.Equal("factory", error.ParamName);
        }

        [Fact]
        public void NullGeometryFactoryConversionReturnsNull()
        {
            GeometryFactory<GeometryPoint> factory = null;
            GeometryPoint point = factory;
            Assert.Null(point);
        }

        private static FactoryPolygonObservation BuildPolygon(
            bool geography, int? srid, (double, double, double?, double?)[][] rings)
        {
            var first = rings.Select(ring => ring.Select(position => position.Item1).ToArray()).ToArray();
            var second = rings.Select(ring => ring.Select(position => position.Item2).ToArray()).ToArray();
            var z = rings.Select(ring => ring.Select(position => position.Item3).ToArray()).ToArray();
            var m = rings.Select(ring => ring.Select(position => position.Item4).ToArray()).ToArray();
            return geography
                ? FactoriesCaptureBoundary.BuildGeographyPolygon(srid, first, second, z, m)
                : FactoriesCaptureBoundary.BuildGeometryPolygon(srid, first, second, z, m);
        }

        private static (double, double, double?, double?)[][] ReadRings(FactoryPolygonObservation value)
        {
            Assert.Equal(value.FirstCoordinates.Length, value.SecondCoordinates.Length);
            Assert.Equal(value.FirstCoordinates.Length, value.Z.Length);
            Assert.Equal(value.FirstCoordinates.Length, value.M.Length);
            return value.FirstCoordinates.Select((ring, ringIndex) =>
            {
                Assert.Equal(ring.Length, value.SecondCoordinates[ringIndex].Length);
                Assert.Equal(ring.Length, value.Z[ringIndex].Length);
                Assert.Equal(ring.Length, value.M[ringIndex].Length);
                return ring.Select((first, position) =>
                    (first, value.SecondCoordinates[ringIndex][position], value.Z[ringIndex][position], value.M[ringIndex][position])).ToArray();
            }).ToArray();
        }
    }
}
