//---------------------------------------------------------------------
// <copyright file="ShapesCaptureTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using Xunit;

namespace Microsoft.Spatial.Tests
{
    public class ShapesCaptureTests
    {
        [Fact]
        public void GeographyPointWithCustomCrsAndMWithoutZ()
        {
            CoordinateSystem crs = CoordinateSystem.Geography(1234);
            GeographyPoint point = GeographyPoint.Create(crs, 12, 34, null, 0);

            Assert.Equal(crs, point.CoordinateSystem);
            Assert.Equal(12, point.Latitude);
            Assert.Equal(34, point.Longitude);
            Assert.Null(point.Z);
            Assert.Equal(0d, point.M);
        }

        [Fact]
        public void GeometryPointWithMWithoutZ()
        {
            GeometryPoint point = GeometryPoint.Create(12, 34, null, 0);

            Assert.Equal(12, point.X);
            Assert.Equal(34, point.Y);
            Assert.Null(point.Z);
            Assert.Equal(0d, point.M);
        }

        [Fact]
        public void GeographyPointCreateAcceptsFiniteLatitudeAboveValidatorRange()
        {
            GeographyPoint point = GeographyPoint.Create(91, 20);

            Assert.Equal(91, point.Latitude);
            Assert.Equal(20, point.Longitude);
        }
    }
}
