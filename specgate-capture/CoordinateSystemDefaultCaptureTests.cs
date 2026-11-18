//---------------------------------------------------------------------
// <copyright file="CoordinateSystemDefaultCaptureTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------
using System.Globalization;
using Xunit;

namespace Microsoft.Spatial.Tests;

public class CoordinateSystemDefaultCaptureTests
{
    [Fact]
    public void GeographyNullUsesDefault()
    {
        var coordinateSystem = CoordinateSystem.Geography(null);

        Assert.Same(CoordinateSystem.DefaultGeography, coordinateSystem);
        Assert.Equal(4326, coordinateSystem.EpsgId);
        Assert.Equal("4326", coordinateSystem.Id);
        Assert.Equal("WGS84", coordinateSystem.Name);
    }

    [Fact]
    public void GeometryNullUsesDefault()
    {
        var coordinateSystem = CoordinateSystem.Geometry(null);

        Assert.Same(CoordinateSystem.DefaultGeometry, coordinateSystem);
        Assert.Equal(0, coordinateSystem.EpsgId);
        Assert.Equal("0", coordinateSystem.Id);
        Assert.Equal("Unitless Plane", coordinateSystem.Name);
    }
}

