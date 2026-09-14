using System;
using System.Runtime.CompilerServices;
using ProWallTools.Geometry;

namespace ProWallTools.Tests
{
    internal static class BoundarySimplifierChecks
    {
        [ModuleInitializer]
        internal static void VerifyJoinedBoundarySimplification()
        {
            SegmentDto[] splitRectangle =
            {
                Segment(0, 0, 4, 0, 0),
                Segment(4, 0, 7, 0, 1),
                Segment(7, 0, 10, 0, 2),
                Segment(10, 0, 10, 10, 3),
                Segment(10, 10, 0, 10, 4),
                Segment(0, 10, 0, 0, 5)
            };

            SegmentDto[] simplified = GeometrySimplifier.SimplifyLoopSegments(splitRectangle, 1e-6);
            if (simplified.Length != 4)
            {
                throw new InvalidOperationException(
                    $"Joined rectangle should simplify to 4 segments, actual {simplified.Length}.");
            }

            SegmentDto[] withArc =
            {
                Segment(0, 0, 5, 0, 0),
                Segment(5, 0, 10, 0, 1),
                Segment(10, 0, 10, 10, 2, 0.5),
                Segment(10, 10, 0, 10, 3),
                Segment(0, 10, 0, 0, 4)
            };

            SegmentDto[] arcSimplified = GeometrySimplifier.SimplifyLoopSegments(withArc, 1e-6);
            if (arcSimplified.Length != 4)
            {
                throw new InvalidOperationException(
                    $"Straight split should merge while arc vertex remains; actual {arcSimplified.Length} segments.");
            }

            bool arcPreserved = false;
            foreach (SegmentDto segment in arcSimplified)
            {
                if (Math.Abs(segment.Bulge - 0.5) <= 1e-12)
                {
                    arcPreserved = true;
                    break;
                }
            }

            if (!arcPreserved)
            {
                throw new InvalidOperationException("Boundary simplification must preserve arc bulge segments.");
            }

            SegmentDto[] exactCircle =
            {
                Segment(10, 0, -10, 0, 0, 1.0),
                Segment(-10, 0, 10, 0, 0, 1.0)
            };

            LoopDto[] circleLoops = GeometryKernel.StitchClosedLoops(exactCircle, 1e-6);
            if (circleLoops.Length != 1 || circleLoops[0].Segments.Length != 2)
            {
                throw new InvalidOperationException(
                    "A circle represented by two exact semicircle bulges must remain a 2-segment closed loop.");
            }

            if (Math.Abs(circleLoops[0].Segments[0].Bulge) < 0.999999 ||
                Math.Abs(circleLoops[0].Segments[1].Bulge) < 0.999999)
            {
                throw new InvalidOperationException("Exact circle topology must preserve semicircle bulges.");
            }
        }

        private static SegmentDto Segment(
            double startX,
            double startY,
            double endX,
            double endY,
            int sourceIndex,
            double bulge = 0)
        {
            return new SegmentDto
            {
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY,
                Bulge = bulge,
                SourceIndex = sourceIndex
            };
        }
    }
}
