namespace Microsoft.Spatial
{
    using System;
    using SpecGate.Annotations;

    [SpecEvent("PipelineEventCaptureAdapter")]
    internal sealed class PipelineEventCaptureAdapter
    {
        private const string Component = "spatial.pipelines_and_services";
        private readonly SpatialPipeline pipeline;

        private PipelineEventCaptureAdapter(SpatialPipeline pipeline)
        {
            this.pipeline = pipeline;
        }

        [SpecSetup("set_crs", Spec = Component)]
        [SpecSetup("begin_shape", Spec = Component)]
        [SpecSetup("begin_figure", Spec = Component)]
        [SpecSetup("line_to", Spec = Component)]
        [SpecSetup("end_figure", Spec = Component)]
        [SpecSetup("end_shape", Spec = Component)]
        [SpecSetup("reset", Spec = Component)]
        public static PipelineEventCaptureAdapter Create(bool validating)
            => new PipelineEventCaptureAdapter(validating ? SpatialValidator.Create() : SpatialBuilder.Create());

        [SpecOperation("set_crs", Spec = Component)]
        [SpecException(typeof(FormatException), typeof(ArgumentException), typeof(InvalidOperationException))]
        public void SetCoordinateSystem(bool geography, int srid)
        {
            if (geography)
            {
                this.pipeline.GeographyPipeline.SetCoordinateSystem(CoordinateSystem.Geography(srid));
            }
            else
            {
                this.pipeline.GeometryPipeline.SetCoordinateSystem(CoordinateSystem.Geometry(srid));
            }
        }

        [SpecOperation("begin_shape", Spec = Component)]
        [SpecException(typeof(FormatException), typeof(ArgumentException), typeof(InvalidOperationException))]
        public void BeginShape(bool geography, string kind)
        {
            var value = Enum.Parse<SpatialType>(kind);
            if (geography) { this.pipeline.GeographyPipeline.BeginGeography(value); }
            else { this.pipeline.GeometryPipeline.BeginGeometry(value); }
        }

        [SpecOperation("begin_figure", Spec = Component)]
        [SpecException(typeof(FormatException), typeof(ArgumentException), typeof(InvalidOperationException))]
        public void BeginFigure(bool geography, double first, double second, double? z, double? m)
        {
            if (geography) { this.pipeline.GeographyPipeline.BeginFigure(new GeographyPosition(first, second, z, m)); }
            else { this.pipeline.GeometryPipeline.BeginFigure(new GeometryPosition(first, second, z, m)); }
        }

        [SpecOperation("line_to", Spec = Component)]
        [SpecException(typeof(FormatException), typeof(ArgumentException), typeof(InvalidOperationException))]
        public void LineTo(bool geography, double first, double second, double? z, double? m)
        {
            if (geography) { this.pipeline.GeographyPipeline.LineTo(new GeographyPosition(first, second, z, m)); }
            else { this.pipeline.GeometryPipeline.LineTo(new GeometryPosition(first, second, z, m)); }
        }

        [SpecOperation("end_figure", Spec = Component)]
        [SpecException(typeof(FormatException), typeof(ArgumentException), typeof(InvalidOperationException))]
        public void EndFigure(bool geography)
        {
            if (geography) { this.pipeline.GeographyPipeline.EndFigure(); }
            else { this.pipeline.GeometryPipeline.EndFigure(); }
        }

        [SpecOperation("end_shape", Spec = Component)]
        [SpecException(typeof(FormatException), typeof(ArgumentException), typeof(InvalidOperationException))]
        public void EndShape(bool geography)
        {
            if (geography) { this.pipeline.GeographyPipeline.EndGeography(); }
            else { this.pipeline.GeometryPipeline.EndGeometry(); }
        }

        [SpecOperation("reset", Spec = Component)]
        public void Reset(bool geography)
        {
            if (geography) { this.pipeline.GeographyPipeline.Reset(); }
            else { this.pipeline.GeometryPipeline.Reset(); }
        }
    }
}
