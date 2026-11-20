namespace Microsoft.Spatial
{
    using System;
    using System.Linq;
    using SpecGate.Annotations;

    // Approved replacement boundary: completed polygon observations, not fluent factory parity.
    internal static class FactoriesCaptureBoundary
    {
        [SpecOperation("build_geography_polygon", Spec = "spatial.factories.polygons")]
        [SpecException(typeof(FormatException), typeof(ArgumentException))]
        public static FactoryPolygonObservation BuildGeographyPolygon(
            int? srid, double[][] firstCoordinates, double[][] secondCoordinates, double?[][] z, double?[][] m)
        {
            ValidateColumns(firstCoordinates, secondCoordinates, z, m);
            var coordinateSystem = srid.HasValue ? CoordinateSystem.Geography(srid.Value) : CoordinateSystem.DefaultGeography;
            var factory = GeographyFactory.Polygon(coordinateSystem);
            for (int ring = 0; ring < firstCoordinates.Length; ring++)
            {
                factory.Ring(firstCoordinates[ring][0], secondCoordinates[ring][0], z[ring][0], m[ring][0]);
                for (int position = 1; position < firstCoordinates[ring].Length; position++)
                {
                    factory.LineTo(firstCoordinates[ring][position], secondCoordinates[ring][position], z[ring][position], m[ring][position]);
                }
            }

            var polygon = factory.Build();
            return new FactoryPolygonObservation
            {
                CoordinateSystem = FactoryCoordinateSystemObservation.Observe(polygon.CoordinateSystem),
                IsEmpty = polygon.IsEmpty,
                FirstCoordinates = polygon.Rings.Select(ring => ring.Points.Select(point => point.Latitude).ToArray()).ToArray(),
                SecondCoordinates = polygon.Rings.Select(ring => ring.Points.Select(point => point.Longitude).ToArray()).ToArray(),
                Z = polygon.Rings.Select(ring => ring.Points.Select(point => point.Z).ToArray()).ToArray(),
                M = polygon.Rings.Select(ring => ring.Points.Select(point => point.M).ToArray()).ToArray()
            };
        }

        [SpecOperation("build_geometry_polygon", Spec = "spatial.factories.polygons")]
        [SpecException(typeof(FormatException), typeof(ArgumentException))]
        public static FactoryPolygonObservation BuildGeometryPolygon(
            int? srid, double[][] firstCoordinates, double[][] secondCoordinates, double?[][] z, double?[][] m)
        {
            ValidateColumns(firstCoordinates, secondCoordinates, z, m);
            var coordinateSystem = srid.HasValue ? CoordinateSystem.Geometry(srid.Value) : CoordinateSystem.DefaultGeometry;
            var factory = GeometryFactory.Polygon(coordinateSystem);
            for (int ring = 0; ring < firstCoordinates.Length; ring++)
            {
                factory.Ring(firstCoordinates[ring][0], secondCoordinates[ring][0], z[ring][0], m[ring][0]);
                for (int position = 1; position < firstCoordinates[ring].Length; position++)
                {
                    factory.LineTo(firstCoordinates[ring][position], secondCoordinates[ring][position], z[ring][position], m[ring][position]);
                }
            }

            var polygon = factory.Build();
            return new FactoryPolygonObservation
            {
                CoordinateSystem = FactoryCoordinateSystemObservation.Observe(polygon.CoordinateSystem),
                IsEmpty = polygon.IsEmpty,
                FirstCoordinates = polygon.Rings.Select(ring => ring.Points.Select(point => point.X).ToArray()).ToArray(),
                SecondCoordinates = polygon.Rings.Select(ring => ring.Points.Select(point => point.Y).ToArray()).ToArray(),
                Z = polygon.Rings.Select(ring => ring.Points.Select(point => point.Z).ToArray()).ToArray(),
                M = polygon.Rings.Select(ring => ring.Points.Select(point => point.M).ToArray()).ToArray()
            };
        }

        private static void ValidateColumns(double[][] firstCoordinates, double[][] secondCoordinates, double?[][] z, double?[][] m)
        {
            if (firstCoordinates.Length != secondCoordinates.Length || firstCoordinates.Length != z.Length || firstCoordinates.Length != m.Length)
            {
                throw new ArgumentException("Coordinate columns must have the same ring count.");
            }

            for (int ring = 0; ring < firstCoordinates.Length; ring++)
            {
                int count = firstCoordinates[ring].Length;
                if (count == 0)
                {
                    throw new ArgumentException("The C# factory has no empty-ring entry point.");
                }

                if (count != secondCoordinates[ring].Length || count != z[ring].Length || count != m[ring].Length)
                {
                    throw new ArgumentException("Coordinate columns must have the same position count.");
                }
            }
        }
    }

    [SpecEvent("FactoryPolygonObservation")]
    internal sealed class FactoryPolygonObservation
    {
        [SpecEvent]
        public FactoryCoordinateSystemObservation CoordinateSystem { get; set; }

        [SpecEvent]
        public bool IsEmpty { get; set; }

        [SpecEvent]
        public double[][] FirstCoordinates { get; set; }

        [SpecEvent]
        public double[][] SecondCoordinates { get; set; }

        [SpecEvent]
        public double?[][] Z { get; set; }

        [SpecEvent]
        public double?[][] M { get; set; }
    }

    [SpecEvent("FactoryCoordinateSystemObservation")]
    internal sealed class FactoryCoordinateSystemObservation
    {
        [SpecEvent]
        public int? EpsgId { get; set; }

        [SpecEvent]
        public string Id { get; set; }

        [SpecEvent]
        public string Name { get; set; }

        internal static FactoryCoordinateSystemObservation Observe(CoordinateSystem value)
        {
            return new FactoryCoordinateSystemObservation { EpsgId = value.EpsgId, Id = value.Id, Name = value.Name };
        }
    }
}
