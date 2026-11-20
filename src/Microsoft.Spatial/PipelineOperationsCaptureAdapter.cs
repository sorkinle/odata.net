#nullable enable

namespace Microsoft.Spatial
{
    using System;
    using SpecGate.Annotations;

    internal static class PipelineOperationsCaptureAdapter
    {
        private const string Component = "spatial.pipelines_and_services";

        [SpecOperation("distance_geography", Spec = Component)]
        public static PipelineOperationObservation DistanceGeography(
            int firstSrid, double?[]? first, int secondSrid, double?[]? second,
            double providerValue, bool unsupported, string? providerFailure)
            => ObserveNumber(providerValue, unsupported, providerFailure, creator =>
                MakeGeographyPoint(creator, firstSrid, first).Distance(MakeGeographyPoint(creator, secondSrid, second)));

        [SpecOperation("distance_geometry", Spec = Component)]
        public static PipelineOperationObservation DistanceGeometry(
            int firstSrid, double?[]? first, int secondSrid, double?[]? second,
            double providerValue, bool unsupported, string? providerFailure)
            => ObserveNumber(providerValue, unsupported, providerFailure, creator =>
                MakeGeometryPoint(creator, firstSrid, first).Distance(MakeGeometryPoint(creator, secondSrid, second)));

        [SpecOperation("length_geography", Spec = Component)]
        public static PipelineOperationObservation LengthGeography(
            int srid, double?[]? line, double providerValue, bool unsupported, string? providerFailure)
            => ObserveNumber(providerValue, unsupported, providerFailure, creator =>
                MakeGeographyLine(creator, CoordinateSystem.Geography(srid), line).Length());

        [SpecOperation("length_geometry", Spec = Component)]
        public static PipelineOperationObservation LengthGeometry(
            int srid, double?[]? line, double providerValue, bool unsupported, string? providerFailure)
            => ObserveNumber(providerValue, unsupported, providerFailure, creator =>
                MakeGeometryLine(creator, CoordinateSystem.Geometry(srid), line).Length());

        [SpecOperation("intersects_geography", Spec = Component)]
        public static PipelineOperationObservation IntersectsGeography(
            int pointSrid, double?[]? point, int polygonSrid, double?[]? ring,
            bool providerValue, bool unsupported, string? providerFailure)
            => ObserveBoolean(providerValue, unsupported, providerFailure, creator =>
            {
                var crs = CoordinateSystem.Geography(polygonSrid);
                var line = MakeGeographyLine(creator, crs, ring);
                var polygon = line == null ? null : ring!.Length == 0
                    ? new GeographyPolygonImplementation(crs, creator)
                    : new GeographyPolygonImplementation(crs, creator, line);
                return MakeGeographyPoint(creator, pointSrid, point).Intersects(polygon);
            });

        [SpecOperation("intersects_geometry", Spec = Component)]
        public static PipelineOperationObservation IntersectsGeometry(
            int pointSrid, double?[]? point, int polygonSrid, double?[]? ring,
            bool providerValue, bool unsupported, string? providerFailure)
            => ObserveBoolean(providerValue, unsupported, providerFailure, creator =>
            {
                var crs = CoordinateSystem.Geometry(polygonSrid);
                var line = MakeGeometryLine(creator, crs, ring);
                var polygon = line == null ? null : ring!.Length == 0
                    ? new GeometryPolygonImplementation(crs, creator)
                    : new GeometryPolygonImplementation(crs, creator, line);
                return MakeGeometryPoint(creator, pointSrid, point).Intersects(polygon);
            });

        private static GeographyPoint? MakeGeographyPoint(SpatialImplementation creator, int srid, double?[]? ordinates)
        {
            if (ordinates == null) { return null; }
            var crs = CoordinateSystem.Geography(srid);
            if (ordinates.Length == 0) { return new GeographyPointImplementation(crs, creator); }
            RequirePoint(ordinates);
            return GeographyPointAt(creator, crs, ordinates, 0);
        }

        private static GeometryPoint? MakeGeometryPoint(SpatialImplementation creator, int srid, double?[]? ordinates)
        {
            if (ordinates == null) { return null; }
            var crs = CoordinateSystem.Geometry(srid);
            if (ordinates.Length == 0) { return new GeometryPointImplementation(crs, creator); }
            RequirePoint(ordinates);
            return GeometryPointAt(creator, crs, ordinates, 0);
        }

        private static GeographyLineString? MakeGeographyLine(
            SpatialImplementation creator, CoordinateSystem crs, double?[]? ordinates)
        {
            if (ordinates == null) { return null; }
            RequirePositions(ordinates);
            var points = new GeographyPoint[ordinates.Length / 4];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = GeographyPointAt(creator, crs, ordinates, i * 4);
            }

            return new GeographyLineStringImplementation(crs, creator, points);
        }

        private static GeometryLineString? MakeGeometryLine(
            SpatialImplementation creator, CoordinateSystem crs, double?[]? ordinates)
        {
            if (ordinates == null) { return null; }
            RequirePositions(ordinates);
            var points = new GeometryPoint[ordinates.Length / 4];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = GeometryPointAt(creator, crs, ordinates, i * 4);
            }

            return new GeometryLineStringImplementation(crs, creator, points);
        }

        private static GeographyPoint GeographyPointAt(
            SpatialImplementation creator, CoordinateSystem crs, double?[] ordinates, int start)
            => new GeographyPointImplementation(crs, creator, Required(ordinates[start]),
                Required(ordinates[start + 1]), ordinates[start + 2], ordinates[start + 3]);

        private static GeometryPoint GeometryPointAt(
            SpatialImplementation creator, CoordinateSystem crs, double?[] ordinates, int start)
            => new GeometryPointImplementation(crs, creator, Required(ordinates[start]),
                Required(ordinates[start + 1]), ordinates[start + 2], ordinates[start + 3]);

        private static double Required(double? ordinate)
            => ordinate ?? throw new ArgumentException("The first two ordinates of each position are required.");

        private static void RequirePoint(double?[] ordinates)
        {
            if (ordinates.Length != 4) { throw new ArgumentException("A point requires four ordinate slots.", nameof(ordinates)); }
        }

        private static void RequirePositions(double?[] ordinates)
        {
            if (ordinates.Length % 4 != 0)
            {
                throw new ArgumentException("Each position requires four ordinate slots.", nameof(ordinates));
            }
        }

        private static PipelineOperationObservation ObserveNumber(
            double value, bool unsupported, string? failure, Func<SpatialImplementation, double?> invoke)
            => Observe(new RecordingProvider(value, false, unsupported, failure),
                (creator, observation) => observation.Number = invoke(creator));

        private static PipelineOperationObservation ObserveBoolean(
            bool value, bool unsupported, string? failure, Func<SpatialImplementation, bool?> invoke)
            => Observe(new RecordingProvider(0, value, unsupported, failure),
                (creator, observation) => observation.Predicate = invoke(creator));

        private static PipelineOperationObservation Observe(
            RecordingProvider provider, Action<SpatialImplementation, PipelineOperationObservation> invoke)
        {
            var observation = new PipelineOperationObservation();
            var creator = new DataServicesSpatialImplementation { Operations = provider };
            try
            {
                invoke(creator, observation);
            }
            catch (NotImplementedException error) when (provider.Unsupported && provider.DispatchCount > 0)
            {
                observation.FailureMessage = error.Message;
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, provider.Failure))
            {
                observation.FailureMessage = error.Message;
            }

            observation.DispatchCount = provider.DispatchCount;
            return observation;
        }

        private sealed class RecordingProvider(double number, bool predicate, bool unsupported, string? failure) : SpatialOperations
        {
            public bool Unsupported { get; } = unsupported;
            public InvalidOperationException? Failure { get; } = failure == null ? null : new InvalidOperationException(failure);
            public int DispatchCount { get; private set; }

            public override double Distance(Geography first, Geography second)
                => Dispatch(() => Unsupported ? base.Distance(first, second) : number);

            public override double Distance(Geometry first, Geometry second)
                => Dispatch(() => Unsupported ? base.Distance(first, second) : number);

            public override double Length(Geography value)
                => Dispatch(() => Unsupported ? base.Length(value) : number);

            public override double Length(Geometry value)
                => Dispatch(() => Unsupported ? base.Length(value) : number);

            public override bool Intersects(Geography first, Geography second)
                => Dispatch(() => Unsupported ? base.Intersects(first, second) : predicate);

            public override bool Intersects(Geometry first, Geometry second)
                => Dispatch(() => Unsupported ? base.Intersects(first, second) : predicate);

            private T Dispatch<T>(Func<T> invoke)
            {
                this.DispatchCount++;
                if (this.Failure != null) { throw this.Failure; }
                return invoke();
            }
        }
    }

    [SpecEvent("PipelineOperationObservation")]
    internal sealed class PipelineOperationObservation
    {
        [SpecEvent("number")]
        public double? Number { get; set; }

        [SpecEvent("predicate")]
        public bool? Predicate { get; set; }

        [SpecEvent("failure_message")]
        public string? FailureMessage { get; set; }

        [SpecEvent("dispatch_count")]
        public int DispatchCount { get; set; }
    }
}
