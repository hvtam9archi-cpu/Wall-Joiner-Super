using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ProWallTools.Geometry;

namespace ProWallTools
{
    internal static class GeometryKernelAdapter
    {
        private const double CleanPolylineTolerance = 1e-9;
        private const int GenericCurveSegments = 24;
        private const int SnapCurveSamples = 8;

        public static Polyline NormalizeAndCleanPolyline(
            Polyline source,
            bool reverseBulges,
            Action<string> logger)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var vertices = new VertexDto[source.NumberOfVertices];
            for (int i = 0; i < source.NumberOfVertices; i++)
            {
                Point3d worldPoint = source.GetPoint3dAt(i);
                vertices[i] = new VertexDto
                {
                    X = worldPoint.X,
                    Y = worldPoint.Y,
                    Bulge = reverseBulges ? -source.GetBulgeAt(i) : source.GetBulgeAt(i),
                    StartWidth = source.GetStartWidthAt(i),
                    EndWidth = source.GetEndWidthAt(i)
                };
            }

            VertexDto[] cleaned = GeometryKernel.CleanVertices(
                vertices,
                source.Closed,
                CleanPolylineTolerance);
            if (cleaned.Length < 2)
            {
                logger?.Invoke("LWPOLYLINE không còn đủ đỉnh hợp lệ sau khi loại đỉnh trùng.");
                return null;
            }

            var result = new Polyline(cleaned.Length)
            {
                Normal = Vector3d.ZAxis,
                Elevation = 0,
                Closed = source.Closed
            };
            for (int i = 0; i < cleaned.Length; i++)
            {
                VertexDto vertex = cleaned[i];
                result.AddVertexAt(
                    i,
                    new Point2d(vertex.X, vertex.Y),
                    vertex.Bulge,
                    vertex.StartWidth,
                    vertex.EndWidth);
            }
            return result;
        }

        public static List<Polyline> StitchClosedLoops(
            IEnumerable<Curve> curves,
            double vertexTolerance,
            Action<string> logger)
        {
            SegmentDto[] segments = ToSegments(curves).ToArray();
            if (segments.Length == 0) return new List<Polyline>();

            LoopDto[] loops = GeometryKernel.StitchClosedLoops(segments, vertexTolerance);
            var result = new List<Polyline>(loops.Length);
            foreach (LoopDto loop in loops)
            {
                if (loop?.Segments == null || loop.Segments.Length < 2) continue;

                SegmentDto[] simplifiedSegments = GeometrySimplifier.SimplifyLoopSegments(
                    loop.Segments,
                    vertexTolerance);
                if (simplifiedSegments.Length < 2) continue;

                var polyline = new Polyline(simplifiedSegments.Length)
                {
                    Normal = Vector3d.ZAxis,
                    Elevation = 0,
                    Closed = true
                };

                try
                {
                    for (int i = 0; i < simplifiedSegments.Length; i++)
                    {
                        SegmentDto segment = simplifiedSegments[i];
                        polyline.AddVertexAt(
                            i,
                            new Point2d(segment.StartX, segment.StartY),
                            segment.Bulge,
                            0,
                            0);
                    }

                    if (polyline.NumberOfVertices >= 2 &&
                        Math.Abs(polyline.Area) > vertexTolerance * vertexTolerance)
                    {
                        if (simplifiedSegments.Length < loop.Segments.Length)
                        {
                            logger?.Invoke(
                                $"Đã rút gọn boundary từ {loop.Segments.Length} xuống {simplifiedSegments.Length} vertex/segment cần thiết.");
                        }
                        result.Add(polyline);
                    }
                    else
                    {
                        polyline.Dispose();
                    }
                }
                catch (System.Exception ex)
                {
                    logger?.Invoke("F# topology không dựng được polyline: " + ex.Message);
                    polyline.Dispose();
                }
            }
            return result;
        }

        public static List<Curve> CreateSafeBridges(
            IReadOnlyList<Curve> curves,
            double gapTolerance,
            double vertexTolerance,
            Action<string> logger)
        {
            SegmentDto[] segments = ToSegments(curves).ToArray();
            if (segments.Length < 2) return new List<Curve>();

            BridgeDto[] bridges = GeometryKernel.FindBridges(
                segments,
                gapTolerance,
                vertexTolerance);
            var result = new List<Curve>(bridges.Length);
            foreach (BridgeDto bridge in bridges)
            {
                if (bridge.Distance <= vertexTolerance || bridge.Distance > gapTolerance) continue;
                result.Add(new Line(
                    new Point3d(bridge.FromX, bridge.FromY, 0),
                    new Point3d(bridge.ToX, bridge.ToY, 0)));
            }

            if (result.Count > 0)
            {
                logger?.Invoke($"F# topology đã tạo {result.Count} bridge giữa các dangling endpoint.");
            }
            return result;
        }

        public static bool TryGetConsensusSnapVector(
            IEnumerable<Entity> selectedEntities,
            IEnumerable<Curve> nearbyCurves,
            double searchRadius,
            double vertexTolerance,
            out Vector3d shift,
            out int supportCount)
        {
            shift = new Vector3d();
            supportCount = 0;

            PointDto[] selectedPoints = (selectedEntities ?? Enumerable.Empty<Entity>())
                .SelectMany(GetEntityVertices)
                .Select(ToPointDto)
                .ToArray();
            PointDto[] externalPoints = (nearbyCurves ?? Enumerable.Empty<Curve>())
                .SelectMany(GetCurveVertices)
                .Select(ToPointDto)
                .ToArray();
            if (selectedPoints.Length == 0 || externalPoints.Length == 0 || searchRadius <= 0)
            {
                return false;
            }

            double consensusTolerance = Math.Max(
                vertexTolerance * 4.0,
                Math.Max(searchRadius * 0.01, 1e-6));
            SnapVectorDto snap = GeometryKernel.TryFindSnapVector(
                selectedPoints,
                externalPoints,
                searchRadius,
                vertexTolerance,
                consensusTolerance);
            supportCount = snap.SupportCount;
            if (!snap.Found) return false;

            shift = new Vector3d(snap.X, snap.Y, 0);
            return true;
        }

        private static IEnumerable<SegmentDto> ToSegments(IEnumerable<Curve> sourceCurves)
        {
            int sourceIndex = 0;
            foreach (Curve curve in sourceCurves ?? Enumerable.Empty<Curve>())
            {
                if (curve == null)
                {
                    sourceIndex++;
                    continue;
                }

                if (curve is Polyline polyline)
                {
                    int segmentCount = polyline.Closed
                        ? polyline.NumberOfVertices
                        : Math.Max(0, polyline.NumberOfVertices - 1);
                    for (int i = 0; i < segmentCount; i++)
                    {
                        int next = (i + 1) % polyline.NumberOfVertices;
                        Point3d start = polyline.GetPoint3dAt(i);
                        Point3d end = polyline.GetPoint3dAt(next);
                        if (start.DistanceTo(end) > 1e-9 || Math.Abs(polyline.GetBulgeAt(i)) > 1e-12)
                        {
                            yield return CreateSegment(start, end, polyline.GetBulgeAt(i), sourceIndex);
                        }
                    }
                    sourceIndex++;
                    continue;
                }

                if (curve is Arc arc)
                {
                    double bulge = Math.Tan(arc.TotalAngle / 4.0);
                    if (arc.Normal.Z < 0) bulge = -bulge;
                    yield return CreateSegment(arc.StartPoint, arc.EndPoint, bulge, sourceIndex++);
                    continue;
                }

                IReadOnlyList<Point3d> samples = SampleCurvePoints(curve, GenericCurveSegments);
                for (int i = 1; i < samples.Count; i++)
                {
                    Point3d start = samples[i - 1];
                    Point3d end = samples[i];
                    if (start.DistanceTo(end) > 1e-9)
                    {
                        yield return CreateSegment(start, end, 0, sourceIndex);
                    }
                }
                sourceIndex++;
            }
        }

        private static SegmentDto CreateSegment(
            Point3d start,
            Point3d end,
            double bulge,
            int sourceIndex)
        {
            return new SegmentDto
            {
                StartX = start.X,
                StartY = start.Y,
                EndX = end.X,
                EndY = end.Y,
                Bulge = bulge,
                SourceIndex = sourceIndex
            };
        }

        private static IEnumerable<Point3d> GetEntityVertices(Entity entity)
        {
            if (entity is Polyline polyline)
            {
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    yield return polyline.GetPoint3dAt(i);
                }
                yield break;
            }

            if (entity is Line line)
            {
                yield return line.StartPoint;
                yield return line.EndPoint;
                yield break;
            }

            if (entity is Curve curve)
            {
                foreach (Point3d point in GetCurveVertices(curve)) yield return point;
            }
        }

        private static IEnumerable<Point3d> GetCurveVertices(Curve curve)
        {
            if (curve is Polyline polyline)
            {
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    yield return polyline.GetPoint3dAt(i);
                }
                yield break;
            }

            if (curve is Line line)
            {
                yield return line.StartPoint;
                yield return line.EndPoint;
                yield break;
            }

            if (curve is Arc arc)
            {
                yield return arc.StartPoint;
                yield return arc.EndPoint;
                yield break;
            }

            foreach (Point3d point in SampleCurvePoints(curve, SnapCurveSamples))
            {
                yield return point;
            }
        }

        private static IReadOnlyList<Point3d> SampleCurvePoints(Curve curve, int segmentCount)
        {
            var points = new List<Point3d>();
            try
            {
                double startParameter = curve.StartParam;
                double endParameter = curve.EndParam;
                double range = endParameter - startParameter;
                if (Math.Abs(range) > 1e-12)
                {
                    for (int i = 0; i <= segmentCount; i++)
                    {
                        double parameter = startParameter + range * i / segmentCount;
                        Point3d point = curve.GetPointAtParameter(parameter);
                        if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-9)
                        {
                            points.Add(point);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                points.Clear();
            }

            if (points.Count == 0)
            {
                try
                {
                    points.Add(curve.StartPoint);
                    Point3d end = curve.EndPoint;
                    if (points[0].DistanceTo(end) > 1e-9) points.Add(end);
                }
                catch (System.Exception)
                {
                    // Caller treats an empty sample set as unsupported geometry.
                }
            }

            return points;
        }

        private static PointDto ToPointDto(Point3d point)
        {
            return new PointDto { X = point.X, Y = point.Y };
        }
    }
}
