//---------------------------------------------------------------------
// <copyright file="PositionsCaptureTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System;
using Xunit;

namespace Microsoft.Spatial.Tests
{
    public class PositionsCaptureTests
    {
        [Fact]
        public void GeographyRetainsOutOfRangeCoordinatesAndZeroElevation()
        {
            Assert.Equal(new PositionValues(91, -15070, 0, null),
                PositionsCapture.GeographyNew(91, -15070, 0, null));
        }

        [Fact]
        public void GeometryRetainsCoordinateOrderAndZeroMeasure()
        {
            Assert.Equal(new PositionValues(-12.5, 23.75, null, 0),
                PositionsCapture.GeometryNew(-12.5, 23.75, null, 0));
        }

        [Fact]
        public void GeographyTwoArgumentConstructorLeavesOrdinatesAbsent()
        {
            Assert.Equal(new PositionValues(31, -122, null, null),
                PositionsCapture.GeographyNew2D(31, -122));
        }

        [Fact]
        public void GeometryTwoArgumentConstructorLeavesOrdinatesAbsent()
        {
            Assert.Equal(new PositionValues(3, -9, null, null),
                PositionsCapture.GeometryNew2D(3, -9));
        }

        [Fact]
        public void GeographyFormatsAValueLargeEnoughForExponentNotation()
        {
            Assert.Equal("GeographyPosition(latitude:1E+20, longitude:2, z:null, m:null)",
                PositionsCapture.GeographyToString(1e20, 2, null, null));
        }

        [Fact]
        public void GeometryFormatsSmallMeasureAndAbsentElevation()
        {
            Assert.Equal("GeometryPosition(-12.5, 23.75, null, 1E-07)",
                PositionsCapture.GeometryToString(-12.5, 23.75, null, 1e-7));
        }

        [Fact]
        public void GeometryKeepsNegativeZeroBits()
        {
            var (x, _, _, m) = PositionsCapture.GeometryNew(-0.0, 1, null, -0.0);
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(x));
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(m.Value));
        }

        [Fact]
        public void GeographyTypedEqualityUsesDotNetFloatEquality()
        {
            var left = new GeographyPosition(double.NaN, double.PositiveInfinity, double.NaN, -0.0);
            var right = new GeographyPosition(double.NaN, double.PositiveInfinity, double.NaN, 0.0);
            Assert.True(left.Equals(right));
        }

        [Fact]
        public void GeometryTypedEqualityUsesDotNetFloatEquality()
        {
            var left = new GeometryPosition(double.NaN, double.NegativeInfinity, -0.0, double.NaN);
            var right = new GeometryPosition(double.NaN, double.NegativeInfinity, 0.0, double.NaN);
            Assert.True(left.Equals(right));
        }

        [Fact]
        public void GeographyTypedEqualityDistinguishesZeroFromMissingElevation()
        {
            Assert.False(PositionsCapture.GeographyEquals(new(10, 20, 0, 1), new(10, 20, null, 1)));
        }

        [Fact]
        public void GeometryTypedEqualityAcceptsDistinctEqualPositions()
        {
            Assert.True(PositionsCapture.GeometryEquals(new(10, 20, 3, null), new(10, 20, 3, null)));
        }

        [Fact]
        public void GeographyOperatorsPreserveSignedZeroEqualityAndOrdinatePresence()
        {
            Assert.True(PositionsCapture.GeographyEq(new(-0.0, 20, null, 1), new(0.0, 20, null, 1)));
            Assert.True(PositionsCapture.GeographyNe(new(10, 20, 0, 1), new(10, 20, null, 1)));
        }

        [Fact]
        public void GeometryOperatorsPreserveSignedZeroEqualityAndOrdinatePresence()
        {
            Assert.True(PositionsCapture.GeometryEq(new(10, -0.0, 3, null), new(10, 0.0, 3, null)));
            Assert.True(PositionsCapture.GeometryNe(new(10, 20, 3, 0), new(10, 20, 3, null)));
        }

        [Fact]
        public void GeographyOperatorsHandleNullOperands()
        {
            GeographyPosition missing = null;
            var position = new GeographyPosition(10, 20);
            Assert.True(missing == (GeographyPosition)null);
            Assert.True(missing != position);
            Assert.False(position == missing);
        }

        [Fact]
        public void GeometryOperatorsHandleNullOperands()
        {
            GeometryPosition missing = null;
            var position = new GeometryPosition(10, 20);
            Assert.True(missing == (GeometryPosition)null);
            Assert.True(missing != position);
            Assert.False(position == missing);
        }
    }
}
