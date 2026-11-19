//---------------------------------------------------------------------
// <copyright file="PositionsCapture.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.Spatial
{
    using SpecGate.Annotations;

    [SpecEvent]
    internal readonly record struct PositionValues(
        [property: SpecEvent] double First,
        [property: SpecEvent] double Second,
        [property: SpecEvent] double? Z,
        [property: SpecEvent] double? M);

    [SpecEvent(Setup = true)]
    internal readonly record struct LeftPosition(
        [property: SpecEvent("left_first")] double First,
        [property: SpecEvent("left_second")] double Second,
        [property: SpecEvent("left_z")] double? Z,
        [property: SpecEvent("left_m")] double? M);

    [SpecEvent(Setup = true)]
    internal readonly record struct RightPosition(
        [property: SpecEvent("right_first")] double First,
        [property: SpecEvent("right_second")] double Second,
        [property: SpecEvent("right_z")] double? Z,
        [property: SpecEvent("right_m")] double? M);

    // Keep actual operands at the boundary. SpecGate keys constructor setup by
    // declaration and cannot distinguish two instances in the same test.
    internal static class PositionsCapture
    {
        private const string Component = "spatial.positions";

        [SpecOperation("geography_new", Spec = Component)]
        public static PositionValues GeographyNew(double latitude, double longitude, double? z, double? m)
            => Observe(new GeographyPosition(latitude, longitude, z, m));

        [SpecOperation("geography_new_2d", Spec = Component)]
        public static PositionValues GeographyNew2D(double latitude, double longitude)
            => Observe(new GeographyPosition(latitude, longitude));

        [SpecOperation("geometry_new", Spec = Component)]
        public static PositionValues GeometryNew(double x, double y, double? z, double? m)
            => Observe(new GeometryPosition(x, y, z, m));

        [SpecOperation("geometry_new_2d", Spec = Component)]
        public static PositionValues GeometryNew2D(double x, double y)
            => Observe(new GeometryPosition(x, y));

        [SpecOperation("geography_to_string", Spec = Component)]
        public static string GeographyToString(double latitude, double longitude, double? z, double? m)
            => new GeographyPosition(latitude, longitude, z, m).ToString();

        [SpecOperation("geometry_to_string", Spec = Component)]
        public static string GeometryToString(double x, double y, double? z, double? m)
            => new GeometryPosition(x, y, z, m).ToString();

        [SpecOperation("geography_equals", Spec = Component)]
        public static bool GeographyEquals(LeftPosition left, RightPosition right)
            => Geography(left).Equals(Geography(right));

        [SpecOperation("geometry_equals", Spec = Component)]
        public static bool GeometryEquals(LeftPosition left, RightPosition right)
            => Geometry(left).Equals(Geometry(right));

        [SpecOperation("geography_eq", Spec = Component)]
        public static bool GeographyEq(LeftPosition left, RightPosition right)
            => Geography(left) == Geography(right);

        [SpecOperation("geometry_eq", Spec = Component)]
        public static bool GeometryEq(LeftPosition left, RightPosition right)
            => Geometry(left) == Geometry(right);

        [SpecOperation("geography_ne", Spec = Component)]
        public static bool GeographyNe(LeftPosition left, RightPosition right)
            => Geography(left) != Geography(right);

        [SpecOperation("geometry_ne", Spec = Component)]
        public static bool GeometryNe(LeftPosition left, RightPosition right)
            => Geometry(left) != Geometry(right);

        private static PositionValues Observe(GeographyPosition position)
            => new PositionValues(position.Latitude, position.Longitude, position.Z, position.M);

        private static PositionValues Observe(GeometryPosition position)
            => new PositionValues(position.X, position.Y, position.Z, position.M);

        private static GeographyPosition Geography(LeftPosition value)
            => new GeographyPosition(value.First, value.Second, value.Z, value.M);

        private static GeographyPosition Geography(RightPosition value)
            => new GeographyPosition(value.First, value.Second, value.Z, value.M);

        private static GeometryPosition Geometry(LeftPosition value)
            => new GeometryPosition(value.First, value.Second, value.Z, value.M);

        private static GeometryPosition Geometry(RightPosition value)
            => new GeometryPosition(value.First, value.Second, value.Z, value.M);
    }
}
