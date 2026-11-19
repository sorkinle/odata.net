#nullable enable

using System;
using Xunit;

namespace Microsoft.Spatial.Tests
{
    public class ErrorsCaptureTests
    {
        [Theory]
        [InlineData("Unknown", 0)]
        [InlineData("Point", 1)]
        [InlineData("LineString", 2)]
        [InlineData("Polygon", 3)]
        [InlineData("MultiPoint", 4)]
        [InlineData("MultiLineString", 5)]
        [InlineData("MultiPolygon", 6)]
        [InlineData("Collection", 7)]
        [InlineData("FullGlobe", 11)]
        public void ObservesNativeSpatialType(string name, int expectedValue)
        {
            var actual = ErrorsCaptureBoundary.ObserveSpatialType(name);
            Assert.Equal(name, actual.Name);
            Assert.Equal(expectedValue, actual.Value);
        }

        [Fact]
        public void DefaultParseErrorConstructorRetainsRuntimeDefaults()
        {
            var actual = new ParseErrorException();
            Assert.IsType<ParseErrorException>(actual);
            Assert.False(string.IsNullOrEmpty(actual.Message));
            Assert.Null(actual.InnerException);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid spatial input")]
        public void MessageConstructorPreservesMessage(string? message)
        {
            var actual = new ParseErrorException(message);
            Assert.Equal(message ?? new ParseErrorException().Message, actual.Message);
            Assert.Null(actual.InnerException);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("invalid spatial input", false)]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("invalid spatial input", true)]
        public void InnerExceptionConstructorPreservesMessageAndCause(string? message, bool hasInner)
        {
            Exception? inner = hasInner ? new FormatException("inner parse failure") : null;
            var actual = new ParseErrorException(message, inner);
            Assert.Equal(message ?? new ParseErrorException().Message, actual.Message);
            Assert.Same(inner, actual.InnerException);
        }

        [Fact]
        public void ErrorObservationAllowsASuccessfulNativeRead()
        {
            ErrorsCaptureBoundary.ReadWktGeography("POINT EMPTY", false);
        }
    }
}
