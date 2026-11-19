//---------------------------------------------------------------------
// <copyright file="CoordinateSystemTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using Xunit;

namespace Microsoft.Spatial.Tests
{
    public class CoordinateSystemTests
    {
        private const CoordinateSystem.Topology Geography = CoordinateSystem.Topology.Geography;
        private const CoordinateSystem.Topology Geometry = CoordinateSystem.Topology.Geometry;

        [Fact]
        public void NotEqualToNull()
        {
            Assert.False(CoordinateSystem.DefaultGeometry.Equals(null));
        }

        [Fact]
        public void EqualWhenEverythingMatches()
        {
            Assert.Equal(Coords(), Coords());
        }

        [Fact]
        public void NotEqualWhenIdsDiffer()
        {
            Assert.NotEqual(Coords(epsgId: 1), Coords(epsgId: 2));
        }

        [Fact]
        public void NotEqualWhenFromDifferentTopologies()
        {
            Assert.NotEqual(Coords(topology: Geography), Coords(topology: Geometry));
        }

        [Fact]
        public void EqualWhenOnlyNameDiffers()
        {
            Assert.True(Coords(name:"funny name").Equals(Coords(name:"typical name")));
        }

        [Fact]
        public void EqualWhenSameInstance()
        {
            var testSubject = Coords();
            Assert.True(testSubject.Equals(testSubject));
        }

        [Fact]
        public void ObjectEqualityUsesCoordinateSystemValues()
        {
            object other = Coords(name: "different");
            Assert.True(Coords().Equals(other));
        }

        [Fact]
        public void ObjectEqualityRejectsOtherTypes()
        {
            Assert.False(Coords().Equals(new object()));
        }

        [Fact]
        public void EqualCoordinateSystemsHaveEqualHashCodes()
        {
            var first = Coords(name: "first");
            var second = Coords(name: "second");

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void GeometryPropertyCaches()
        {
            Assert.Same(CoordinateSystem.Geometry(1), CoordinateSystem.Geometry(1));
        }

        [Fact]
        public void GeographyPropertyCaches()
        {
            Assert.Same(CoordinateSystem.Geography(1), CoordinateSystem.Geography(1));
        }

        [Fact]
        public void GeographyAndGeometyPropertiesReturnDifferentInstancesForSameId()
        {
            Assert.NotEqual(CoordinateSystem.Geography(1), CoordinateSystem.Geometry(1));
        }

        [Fact]
        public void ToWktImplementation()
        {
            Assert.Equal("SRID=1;", CoordinateSystemCaptureSetup.Create(1, true).ToWktId());
        }

        [Fact]
        public void ToStringImplementation()
        {
            Assert.Equal("GeographyCoordinateSystem(EpsgId=1)", CoordinateSystemCaptureSetup.Create(1, true).ToString());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void FactoriesPreserveSignedIdentifiers(int epsgId)
        {
            Assert.Equal(epsgId, CoordinateSystem.Geography(epsgId).EpsgId);
            Assert.Equal(epsgId, CoordinateSystem.Geometry(epsgId).EpsgId);
        }

        [Theory]
        [InlineData(false, 1, "GeometryCoordinateSystem(EpsgId=1)", "SRID=1;")]
        [InlineData(true, null, "GeographyCoordinateSystem(EpsgId=4326)", "SRID=4326;")]
        [InlineData(false, null, "GeometryCoordinateSystem(EpsgId=0)", "SRID=0;")]
        public void InstanceFormattingUsesTopologyAndDefaults(
            bool isGeography, int? epsgId, string expectedText, string expectedWktId)
        {
            var coordinateSystem = CoordinateSystemCaptureSetup.Create(epsgId, isGeography);

            Assert.Equal(expectedText, coordinateSystem.ToString());
            Assert.Equal(expectedWktId, coordinateSystem.ToWktId());
        }

        [Fact]
        public void GetHashCodeImplementation()
        {
            Assert.Equal(1234, CoordinateSystem.Geography(1234).GetHashCode());
        }

        private static CoordinateSystem Coords(int epsgId = 99, string name = "usual name", CoordinateSystem.Topology topology = Geography)
        {
            return new CoordinateSystem(epsgId, name, topology);
        }
    }
}
