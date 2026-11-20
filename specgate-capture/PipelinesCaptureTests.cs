#nullable enable

using System;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Microsoft.Spatial.Tests
{
    public sealed class PipelinesCaptureTests
    {
        private static readonly double?[] Point = [1, 2, null, null];
        private static readonly double?[] Line = [1, 2, null, null, 3, 4, 5, 6];
        private static readonly double?[] Ring = [1, 2, null, null, 3, 4, null, null, 5, 2, null, null, 1, 2, null, null];

        [Fact]
        public void DistanceRecordsAbsentOperandsAndDispatch()
        {
            AssertAbsent(PipelineOperationsCaptureAdapter.DistanceGeography(4326, null, 4326, Point, 12.5, false, null));
            AssertAbsent(PipelineOperationsCaptureAdapter.DistanceGeometry(0, Point, 0, null, 12.5, false, null));
            AssertNumber(12.5, PipelineOperationsCaptureAdapter.DistanceGeography(4326, [], 4326, Point, 12.5, false, null));
            AssertNumber(12.5, PipelineOperationsCaptureAdapter.DistanceGeometry(0, Point, 0, [3, 4, 5, 6], 12.5, false, null));
        }

        [Fact]
        public void LengthRecordsAbsentOperandsDispatchAndFailure()
        {
            AssertAbsent(PipelineOperationsCaptureAdapter.LengthGeography(4326, null, 4.5, false, null));
            AssertAbsent(PipelineOperationsCaptureAdapter.LengthGeometry(0, null, 4.5, false, null));
            AssertNumber(4.5, PipelineOperationsCaptureAdapter.LengthGeography(4326, Line, 4.5, false, null));
            AssertNumber(4.5, PipelineOperationsCaptureAdapter.LengthGeometry(0, Line, 4.5, false, null));
            AssertFailure(new NotImplementedException().Message, PipelineOperationsCaptureAdapter.LengthGeography(4326, Line, 4.5, true, null));
            AssertFailure("length provider failed", PipelineOperationsCaptureAdapter.LengthGeometry(0, Line, 4.5, false, "length provider failed"));
        }

        [Fact]
        public void IntersectsRecordsAbsentOperandsDispatchAndFailure()
        {
            AssertAbsent(PipelineOperationsCaptureAdapter.IntersectsGeography(4326, null, 4326, Ring, true, false, null));
            AssertAbsent(PipelineOperationsCaptureAdapter.IntersectsGeometry(0, Point, 0, null, false, false, null));
            AssertPredicate(true, PipelineOperationsCaptureAdapter.IntersectsGeography(4326, Point, 4326, Ring, true, false, null));
            AssertPredicate(false, PipelineOperationsCaptureAdapter.IntersectsGeometry(0, Point, 0, Ring, false, false, null));
            AssertFailure("intersection provider failed", PipelineOperationsCaptureAdapter.IntersectsGeography(4326, Point, 4326, Ring, true, false, "intersection provider failed"));
            AssertFailure(new NotImplementedException().Message, PipelineOperationsCaptureAdapter.IntersectsGeometry(0, Point, 0, Ring, false, true, null));
        }

        [Fact]
        public void ValidatorRejectsLatitudeAndLetsOppositeRootFinish()
        {
            var pipeline = PipelineEventCaptureAdapter.Create(validating: true);
            pipeline.SetCoordinateSystem(geography: true, 4326);
            pipeline.SetCoordinateSystem(geography: false, 0);
            pipeline.BeginShape(geography: true, "Point");
            pipeline.BeginShape(geography: false, "LineString");
            pipeline.BeginFigure(geography: false, 1, 2, null, null);
            var failure = Assert.Throws<FormatException>(() => pipeline.BeginFigure(geography: true, 91, 0, null, null));
            Assert.Equal(Error.Format(SRResources.Validator_InvalidLatitudeCoordinate, 91), failure.Message);
            pipeline.LineTo(geography: false, 3, 4, 5, 6);
            pipeline.EndFigure(geography: false);
            pipeline.EndShape(geography: false);
            pipeline.Reset(geography: true);
            pipeline.SetCoordinateSystem(geography: true, 1234);
            pipeline.BeginShape(geography: true, "FullGlobe");
            pipeline.EndShape(geography: true);
        }

        [Fact]
        public void BuilderAcceptsSecondRootWithoutReadingFirst()
        {
            var pipeline = PipelineEventCaptureAdapter.Create(validating: false);
            pipeline.SetCoordinateSystem(geography: false, 1234);
            pipeline.BeginShape(geography: false, "Point");
            pipeline.BeginFigure(geography: false, 1, 2, null, null);
            pipeline.EndFigure(geography: false);
            pipeline.EndShape(geography: false);
            pipeline.SetCoordinateSystem(geography: false, 1234);
            pipeline.BeginShape(geography: false, "Point");
            pipeline.BeginFigure(geography: false, 3, 4, 5, 6);
            pipeline.EndFigure(geography: false);
            pipeline.EndShape(geography: false);
        }

        private static void AssertAbsent(PipelineOperationObservation observation)
        {
            Assert.Equal(0, observation.DispatchCount);
            Assert.Null(observation.Number);
            Assert.Null(observation.Predicate);
            Assert.Null(observation.FailureMessage);
        }

        private static void AssertNumber(double expected, PipelineOperationObservation observation)
        {
            Assert.Equal(1, observation.DispatchCount);
            Assert.Equal(expected, observation.Number);
            Assert.Null(observation.Predicate);
            Assert.Null(observation.FailureMessage);
        }

        private static void AssertPredicate(bool expected, PipelineOperationObservation observation)
        {
            Assert.Equal(1, observation.DispatchCount);
            Assert.Null(observation.Number);
            Assert.Equal(expected, observation.Predicate);
            Assert.Null(observation.FailureMessage);
        }

        private static void AssertFailure(string message, PipelineOperationObservation observation)
        {
            Assert.Equal(1, observation.DispatchCount);
            Assert.Null(observation.Number);
            Assert.Null(observation.Predicate);
            Assert.Equal(message, observation.FailureMessage);
        }
    }
}
