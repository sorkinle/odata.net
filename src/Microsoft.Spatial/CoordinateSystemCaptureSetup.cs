//---------------------------------------------------------------------
// <copyright file="CoordinateSystemCaptureSetup.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using SpecGate.Annotations;

namespace Microsoft.Spatial
{
    /// <summary>
    /// SpecGate setup producer for CoordinateSystem instance-method receivers.
    /// CoordinateSystem.Geography cannot also be a setup because it is already a
    /// SpecGate operation, and the SpecGate weaver rejects a method that is both.
    /// </summary>
    internal static class CoordinateSystemCaptureSetup
    {
        [SpecSetup("to_string", Spec = "spatial.coordinate_system")]
        [SpecSetup("to_wkt_id", Spec = "spatial.coordinate_system")]
        internal static CoordinateSystem Geography([SpecInput("epsg_id")] int? epsgId)
        {
            return CoordinateSystem.Geography(epsgId);
        }
    }
}
