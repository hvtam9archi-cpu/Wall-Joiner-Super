using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public sealed class CurveSourceState
    {
        public ObjectId SourceId { get; set; }
        public int CurveCount { get; set; }
        public bool HasUnsupportedContent { get; set; }
        public bool ExtractionFailed { get; set; }
        public bool IsOnLockedLayer { get; set; }

        public bool CanEraseSource =>
            CurveCount > 0 &&
            !HasUnsupportedContent &&
            !ExtractionFailed &&
            !IsOnLockedLayer;
    }

    public sealed class CurveCollectionResult
    {
        public List<Curve> Curves { get; } = new List<Curve>();
        public List<CurveSourceState> Sources { get; } = new List<CurveSourceState>();
        public List<string> Warnings { get; } = new List<string>();

        public void DisposeCurves()
        {
            foreach (Curve curve in Curves)
            {
                if (curve != null && !curve.IsDisposed)
                {
                    curve.Dispose();
                }
            }
            Curves.Clear();
        }
    }

    public sealed class BoundaryBuildResult
    {
        public List<Polyline> Boundaries { get; } = new List<Polyline>();
        public List<string> Warnings { get; } = new List<string>();
        public bool UsedFallback { get; set; }

        public void DisposeBoundaries()
        {
            foreach (Polyline boundary in Boundaries)
            {
                if (boundary != null && !boundary.IsDisposed)
                {
                    boundary.Dispose();
                }
            }
            Boundaries.Clear();
        }
    }
}
