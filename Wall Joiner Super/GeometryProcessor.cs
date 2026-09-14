using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ProWallTools
{
    public static class GeometryProcessor
    {
        private sealed class CurveBounds
        {
            public int Index;
            public Extents3d Extents;
        }

        private sealed class BridgeCandidate
        {
            public Point3d From;
            public Point3d To;
            public double Distance;
        }

        private sealed class UnionFind
        {
            private readonly int[] _parents;
            private readonly byte[] _ranks;

            public UnionFind(int size)
            {
                _parents = new int[size];
                _ranks = new byte[size];
                for (int i = 0; i < size; i++) _parents[i] = i;
            }

            public int Find(int value)
            {
                while (_parents[value] != value)
                {
                    _parents[value] = _parents[_parents[value]];
                    value = _parents[value];
                }
                return value;
            }

            public void Union(int first, int second)
            {
                int firstRoot = Find(first);
                int secondRoot = Find(second);
                if (firstRoot == secondRoot) return;

                if (_ranks[firstRoot] < _ranks[secondRoot])
                {
                    _parents[firstRoot] = secondRoot;
                }
                else if (_ranks[firstRoot] > _ranks[secondRoot])
                {
                    _parents[secondRoot] = firstRoot;
                }
                else
                {
                    _parents[secondRoot] = firstRoot;
                    _ranks[firstRoot]++;
                }
            }
        }

        public static void DisposeCollection(DBObjectCollection collection, bool disposeItems)
        {
            if (collection == null) return;

            if (disposeItems)
            {
                foreach (DBObject item in collection)
                {
                    if (item != null && !item.IsDisposed)
                    {
                        item.Dispose();
                    }
                }
            }

            collection.Dispose();
        }

        public static CurveCollectionResult CollectCurves(
            Transaction transaction,
            IEnumerable<ObjectId> sourceIds,
            Action<string> logger = null)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (sourceIds == null) throw new ArgumentNullException(nameof(sourceIds));

            var result = new CurveCollectionResult();

            foreach (ObjectId sourceId in sourceIds.Distinct())
            {
                var state = new CurveSourceState { SourceId = sourceId };
                result.Sources.Add(state);

                try
                {
                    var entity = transaction.GetObject(sourceId, OpenMode.ForRead, false) as Entity;
                    if (entity == null)
                    {
                        state.ExtractionFailed = true;
                        result.Warnings.Add($"Không đọc được đối tượng {sourceId.Handle}.");
                        continue;
                    }

                    int countBefore = result.Curves.Count;
                    ExtractRecursive(entity, result.Curves, state, result.Warnings, logger);
                    state.CurveCount = result.Curves.Count - countBefore;

                    if (state.CurveCount == 0)
                    {
                        state.ExtractionFailed = true;
                        result.Warnings.Add($"Đối tượng {sourceId.Handle} không tạo được curve hợp lệ.");
                    }
                }
                catch (Exception ex)
                {
                    state.ExtractionFailed = true;
                    result.Warnings.Add($"Lỗi đọc đối tượng {sourceId.Handle}: {ex.Message}");
                    logger?.Invoke(ex.ToString());
                }
            }

            return result;
        }

        private static void ExtractRecursive(
            Entity entity,
            ICollection<Curve> curves,
            CurveSourceState sourceState,
            ICollection<string> warnings,
            Action<string> logger)
        {
            if (entity is BlockReference blockReference)
            {
                DBObjectCollection exploded = new DBObjectCollection();
                try
                {
                    blockReference.Explode(exploded);
                    if (exploded.Count == 0)
                    {
                        sourceState.ExtractionFailed = true;
                        warnings.Add($"Block {sourceState.SourceId.Handle} không bung được nội dung.");
                    }

                    foreach (DBObject item in exploded)
                    {
                        if (item is Entity child)
                        {
                            ExtractRecursive(child, curves, sourceState, warnings, logger);
                        }
                        else
                        {
                            sourceState.HasUnsupportedContent = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sourceState.ExtractionFailed = true;
                    warnings.Add($"Không thể bung block {sourceState.SourceId.Handle}: {ex.Message}");
                    logger?.Invoke(ex.ToString());
                }
                finally
                {
                    DisposeCollection(exploded, disposeItems: true);
                }
                return;
            }

            if (entity is Curve curve)
            {
                Curve clone = SanitizeCurve(curve, logger);
                if (clone != null)
                {
                    curves.Add(clone);
                }
                else
                {
                    sourceState.ExtractionFailed = true;
                }
                return;
            }

            sourceState.HasUnsupportedContent = true;
        }

        private static Curve SanitizeCurve(Curve curve, Action<string> logger)
        {
            Curve clone = null;
            try
            {
                if (curve == null || Math.Abs(curve.EndParam - curve.StartParam) < 1e-9)
                {
                    return null;
                }

                if (curve is Polyline sourcePolyline)
                {
                    if (!TryGetWorldZDirection(sourcePolyline.Normal, out bool reverseBulges))
                    {
                        logger?.Invoke("Bỏ qua LWPOLYLINE không nằm trên mặt phẳng song song WCS XY.");
                        return null;
                    }

                    Point3d[] worldVertices = Enumerable
                        .Range(0, sourcePolyline.NumberOfVertices)
                        .Select(sourcePolyline.GetPoint3dAt)
                        .ToArray();
                    var polyline = sourcePolyline.Clone() as Polyline;
                    clone = polyline;
                    polyline.Normal = Vector3d.ZAxis;
                    polyline.Elevation = 0;
                    for (int i = 0; i < worldVertices.Length; i++)
                    {
                        polyline.SetPointAt(i, ToPoint2d(worldVertices[i]));
                        if (reverseBulges)
                        {
                            polyline.SetBulgeAt(i, -sourcePolyline.GetBulgeAt(i));
                        }
                    }
                }
                else if (curve is Line sourceLine)
                {
                    var line = sourceLine.Clone() as Line;
                    clone = line;
                    line.StartPoint = new Point3d(sourceLine.StartPoint.X, sourceLine.StartPoint.Y, 0);
                    line.EndPoint = new Point3d(sourceLine.EndPoint.X, sourceLine.EndPoint.Y, 0);
                }
                else
                {
                    clone = curve.Clone() as Curve;
                    if (clone == null) return null;

                    Extents3d extents = clone.GeometricExtents;
                    double zRange = Math.Abs(extents.MaxPoint.Z - extents.MinPoint.Z);
                    if (zRange > 1e-6)
                    {
                        logger?.Invoke("Bỏ qua curve không nằm trên mặt phẳng song song WCS XY.");
                        clone.Dispose();
                        return null;
                    }

                    double elevation = (extents.MinPoint.Z + extents.MaxPoint.Z) / 2.0;
                    clone.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -elevation)));
                }

                Extents3d validationExtents = clone.GeometricExtents;
                return clone;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Không thể chuẩn hóa curve: " + ex.Message);
                if (clone != null && !clone.IsDisposed) clone.Dispose();
                return null;
            }
        }

        private static bool TryGetWorldZDirection(Vector3d normal, out bool reverseBulges)
        {
            reverseBulges = false;
            if (normal.Length <= 1e-9) return false;

            double dot = normal.GetNormal().DotProduct(Vector3d.ZAxis);
            if (Math.Abs(Math.Abs(dot) - 1.0) > 1e-9) return false;

            reverseBulges = dot < 0;
            return true;
        }

        public static List<List<Curve>> ClusterCurves(
            List<Curve> curves,
            double gapTolerance,
            Action<string> logger = null)
        {
            if (curves == null) throw new ArgumentNullException(nameof(curves));
            if (curves.Count == 0) return new List<List<Curve>>();

            List<CurveBounds> bounds = BuildBounds(curves, logger);
            var unionFind = new UnionFind(curves.Count);

            foreach (Tuple<int, int> pair in GetCandidatePairs(bounds, gapTolerance))
            {
                CurveBounds first = bounds[pair.Item1];
                CurveBounds second = bounds[pair.Item2];
                if (!AreExtentsNear(first.Extents, second.Extents, gapTolerance)) continue;

                Curve firstCurve = curves[first.Index];
                Curve secondCurve = curves[second.Index];
                if (AreCurvesNear(firstCurve, secondCurve, gapTolerance, logger))
                {
                    unionFind.Union(first.Index, second.Index);
                }
            }

            var groups = new Dictionary<int, List<Curve>>();
            for (int i = 0; i < curves.Count; i++)
            {
                int root = unionFind.Find(i);
                if (!groups.TryGetValue(root, out List<Curve> cluster))
                {
                    cluster = new List<Curve>();
                    groups[root] = cluster;
                }
                cluster.Add(curves[i]);
            }

            return groups.Values.ToList();
        }

        private static List<CurveBounds> BuildBounds(IReadOnlyList<Curve> curves, Action<string> logger)
        {
            var result = new List<CurveBounds>(curves.Count);
            for (int i = 0; i < curves.Count; i++)
            {
                try
                {
                    result.Add(new CurveBounds { Index = i, Extents = curves[i].GeometricExtents });
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"Không đọc được extents của curve {i}: {ex.Message}");
                    Point3d point = curves[i].StartPoint;
                    result.Add(new CurveBounds
                    {
                        Index = i,
                        Extents = new Extents3d(point, point)
                    });
                }
            }

            result.Sort((first, second) => first.Extents.MinPoint.X.CompareTo(second.Extents.MinPoint.X));
            return result;
        }

        private static IEnumerable<Tuple<int, int>> GetCandidatePairs(
            IReadOnlyList<CurveBounds> sortedBounds,
            double gapTolerance)
        {
            for (int first = 0; first < sortedBounds.Count; first++)
            {
                double maxX = sortedBounds[first].Extents.MaxPoint.X + gapTolerance;
                for (int second = first + 1; second < sortedBounds.Count; second++)
                {
                    if (sortedBounds[second].Extents.MinPoint.X > maxX) break;
                    yield return Tuple.Create(first, second);
                }
            }
        }

        private static bool AreExtentsNear(Extents3d first, Extents3d second, double gap)
        {
            return first.MinPoint.X <= second.MaxPoint.X + gap &&
                   first.MaxPoint.X + gap >= second.MinPoint.X &&
                   first.MinPoint.Y <= second.MaxPoint.Y + gap &&
                   first.MaxPoint.Y + gap >= second.MinPoint.Y;
        }

        private static bool AreCurvesNear(
            Curve first,
            Curve second,
            double tolerance,
            Action<string> logger)
        {
            if (first == null || second == null) return false;
            double effectiveTolerance = Math.Max(tolerance, 1e-9);

            try
            {
                var intersections = new Point3dCollection();
                first.IntersectWith(
                    second,
                    Intersect.OnBothOperands,
                    intersections,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (intersections.Count > 0) return true;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Không thể kiểm tra giao điểm curve khi gom cụm: " + ex.Message);
            }

            foreach (Point3d point in GetProbePoints(first))
            {
                if (IsPointNearCurve(point, second, effectiveTolerance, logger)) return true;
            }

            foreach (Point3d point in GetProbePoints(second))
            {
                if (IsPointNearCurve(point, first, effectiveTolerance, logger)) return true;
            }

            return false;
        }

        private static bool IsPointNearCurve(
            Point3d point,
            Curve curve,
            double tolerance,
            Action<string> logger)
        {
            try
            {
                Point3d closest = curve.GetClosestPointTo(point, false);
                return point.DistanceTo(closest) <= tolerance;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Không thể đo khoảng cách giữa các curve: " + ex.Message);
                return false;
            }
        }

        private static IEnumerable<Point3d> GetProbePoints(Curve curve)
        {
            if (curve == null) yield break;

            if (curve is Polyline polyline)
            {
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    yield return polyline.GetPoint3dAt(i);
                }
            }
            else
            {
                yield return curve.StartPoint;
                if (curve.EndPoint.DistanceTo(curve.StartPoint) > 1e-9)
                {
                    yield return curve.EndPoint;
                }
            }

            double parameterRange = curve.EndParam - curve.StartParam;
            if (Math.Abs(parameterRange) <= 1e-9) yield break;

            double[] fractions = { 0.25, 0.5, 0.75 };
            foreach (double fraction in fractions)
            {
                Point3d point;
                try
                {
                    point = curve.GetPointAtParameter(curve.StartParam + parameterRange * fraction);
                }
                catch (Exception)
                {
                    continue;
                }
                yield return point;
            }
        }

        public static BoundaryBuildResult ProcessClusterToBoundaries(
            List<Curve> cluster,
            double gapTolerance,
            double vertexTolerance,
            Action<string> logger = null)
        {
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));

            var result = new BoundaryBuildResult();
            var workingCurves = new List<Curve>(cluster);
            var bridges = new List<Curve>();

            try
            {
                string originalRegionError;
                TryBuildRegionBoundaries(
                    workingCurves,
                    vertexTolerance,
                    logger,
                    result.Boundaries,
                    out originalRegionError);

                if (result.Boundaries.Count == 0)
                {
                    bridges = CreateBridges(cluster, gapTolerance, vertexTolerance, logger);
                    if (bridges.Count > 0)
                    {
                        workingCurves.AddRange(bridges);
                        string bridgedRegionError;
                        TryBuildRegionBoundaries(
                            workingCurves,
                            vertexTolerance,
                            logger,
                            result.Boundaries,
                            out bridgedRegionError);

                        if (result.Boundaries.Count == 0 && !string.IsNullOrWhiteSpace(bridgedRegionError))
                        {
                            result.Warnings.Add("Không thể tạo Region sau khi nối khe: " + bridgedRegionError);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(originalRegionError))
                    {
                        result.Warnings.Add("Không thể tạo Region: " + originalRegionError);
                    }
                }

                if (result.Boundaries.Count == 0)
                {
                    result.UsedFallback = true;
                    List<Polyline> fallback = JoinClosedCurves(
                        workingCurves,
                        gapTolerance,
                        vertexTolerance,
                        logger);
                    result.Boundaries.AddRange(fallback);
                    if (fallback.Count == 0)
                    {
                        result.Warnings.Add("Cụm curve không tạo được đường bao kín an toàn.");
                    }
                    else
                    {
                        result.Warnings.Add("Đã dùng phương án nối curve dự phòng do Region không tạo được kết quả.");
                    }
                }

                return result;
            }
            catch (Exception)
            {
                result.DisposeBoundaries();
                throw;
            }
            finally
            {
                foreach (Curve bridge in bridges)
                {
                    if (bridge != null && !bridge.IsDisposed) bridge.Dispose();
                }
            }
        }

        private static bool TryBuildRegionBoundaries(
            IEnumerable<Curve> curves,
            double vertexTolerance,
            Action<string> logger,
            ICollection<Polyline> boundaries,
            out string error)
        {
            error = null;
            DBObjectCollection input = new DBObjectCollection();
            DBObjectCollection regions = null;
            var mergedRegions = new List<Region>();
            var unmergedRegions = new List<Region>();
            int countBefore = boundaries.Count;

            try
            {
                foreach (Curve curve in curves) input.Add(curve);
                regions = Region.CreateFromCurves(input);

                foreach (DBObject item in regions)
                {
                    if (item is Region region)
                    {
                        unmergedRegions.Add(region);
                    }
                    else if (item != null && !item.IsDisposed)
                    {
                        item.Dispose();
                    }
                }

                regions.Dispose();
                regions = null;

                while (unmergedRegions.Count > 0)
                {
                    Region region = unmergedRegions[0];
                    unmergedRegions.RemoveAt(0);
                    MergeRegion(mergedRegions, region, logger);
                }

                foreach (Region region in mergedRegions)
                {
                    DBObjectCollection exploded = new DBObjectCollection();
                    try
                    {
                        region.Explode(exploded);
                        List<Curve> parts = exploded.Cast<DBObject>().OfType<Curve>().ToList();
                        List<Polyline> loops = JoinClosedCurves(
                            parts,
                            vertexTolerance,
                            vertexTolerance,
                            logger);
                        foreach (Polyline loop in loops) boundaries.Add(loop);
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke("Không thể trích boundary từ Region: " + ex);
                    }
                    finally
                    {
                        DisposeCollection(exploded, disposeItems: true);
                    }
                }

                return boundaries.Count > countBefore;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger?.Invoke(ex.ToString());
                return false;
            }
            finally
            {
                if (regions != null)
                {
                    regions.Dispose();
                }

                foreach (Region region in mergedRegions)
                {
                    if (region != null && !region.IsDisposed) region.Dispose();
                }

                foreach (Region region in unmergedRegions)
                {
                    if (region != null && !region.IsDisposed) region.Dispose();
                }

                input.Dispose();
            }
        }

        private static void MergeRegion(List<Region> mergedRegions, Region current, Action<string> logger)
        {
            for (int i = mergedRegions.Count - 1; i >= 0; i--)
            {
                Region existing = mergedRegions[i];
                bool mayIntersect;
                try
                {
                    mayIntersect = AreExtentsNear(existing.GeometricExtents, current.GeometricExtents, 0);
                }
                catch (Exception ex)
                {
                    logger?.Invoke("Không thể đọc extents của Region; vẫn thử union: " + ex.Message);
                    mayIntersect = true;
                }

                if (!mayIntersect) continue;

                try
                {
                    current.BooleanOperation(BooleanOperationType.BoolUnite, existing);
                    existing.Dispose();
                    mergedRegions.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    logger?.Invoke("Không thể union hai Region giao nhau: " + ex.Message);
                }
            }

            mergedRegions.Add(current);
        }

        private static List<Curve> CreateBridges(
            IReadOnlyList<Curve> curves,
            double gapTolerance,
            double vertexTolerance,
            Action<string> logger)
        {
            var bridges = new List<Curve>();
            if (gapTolerance <= vertexTolerance || curves.Count < 2) return bridges;

            List<CurveBounds> bounds = BuildBounds(curves, logger);
            var bestByEndpoint = new Dictionary<string, BridgeCandidate>();

            foreach (Tuple<int, int> pair in GetCandidatePairs(bounds, gapTolerance))
            {
                CurveBounds firstBounds = bounds[pair.Item1];
                CurveBounds secondBounds = bounds[pair.Item2];
                if (!AreExtentsNear(firstBounds.Extents, secondBounds.Extents, gapTolerance)) continue;

                Curve first = curves[firstBounds.Index];
                Curve second = curves[secondBounds.Index];
                ConsiderEndpoint(firstBounds.Index, 0, first.StartPoint, second, bestByEndpoint, gapTolerance, vertexTolerance, logger);
                ConsiderEndpoint(firstBounds.Index, 1, first.EndPoint, second, bestByEndpoint, gapTolerance, vertexTolerance, logger);
                ConsiderEndpoint(secondBounds.Index, 0, second.StartPoint, first, bestByEndpoint, gapTolerance, vertexTolerance, logger);
                ConsiderEndpoint(secondBounds.Index, 1, second.EndPoint, first, bestByEndpoint, gapTolerance, vertexTolerance, logger);
            }

            foreach (BridgeCandidate candidate in bestByEndpoint.Values.OrderBy(value => value.Distance))
            {
                bool duplicate = bridges.OfType<Line>().Any(line =>
                    SameSegment(line.StartPoint, line.EndPoint, candidate.From, candidate.To, vertexTolerance));
                if (!duplicate)
                {
                    bridges.Add(new Line(candidate.From, candidate.To));
                }
            }

            return bridges;
        }

        private static void ConsiderEndpoint(
            int curveIndex,
            int endpointIndex,
            Point3d endpoint,
            Curve other,
            IDictionary<string, BridgeCandidate> bestByEndpoint,
            double gapTolerance,
            double vertexTolerance,
            Action<string> logger)
        {
            try
            {
                Point3d closest = other.GetClosestPointTo(endpoint, false);
                double distance = endpoint.DistanceTo(closest);
                if (distance <= vertexTolerance || distance > gapTolerance) return;

                string key = curveIndex + ":" + endpointIndex;
                if (!bestByEndpoint.TryGetValue(key, out BridgeCandidate current) || distance < current.Distance)
                {
                    bestByEndpoint[key] = new BridgeCandidate
                    {
                        From = endpoint,
                        To = closest,
                        Distance = distance
                    };
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("Không thể tìm bridge gần nhất: " + ex.Message);
            }
        }

        private static bool SameSegment(
            Point3d firstStart,
            Point3d firstEnd,
            Point3d secondStart,
            Point3d secondEnd,
            double tolerance)
        {
            return (firstStart.DistanceTo(secondStart) <= tolerance &&
                    firstEnd.DistanceTo(secondEnd) <= tolerance) ||
                   (firstStart.DistanceTo(secondEnd) <= tolerance &&
                    firstEnd.DistanceTo(secondStart) <= tolerance);
        }

        private static List<Polyline> JoinClosedCurves(
            IEnumerable<Curve> sourceParts,
            double closureTolerance,
            double areaTolerance,
            Action<string> logger)
        {
            var working = new List<Curve>();
            foreach (Curve source in sourceParts)
            {
                Curve clone = source?.Clone() as Curve;
                if (clone != null) working.Add(clone);
            }

            var result = new List<Polyline>();
            try
            {
                while (working.Count > 0)
                {
                    Curve seed = working[0];
                    working.RemoveAt(0);
                    Polyline polyline;
                    try
                    {
                        polyline = CurveToPolyline(seed, areaTolerance);
                    }
                    finally
                    {
                        seed.Dispose();
                    }

                    if (polyline == null) continue;

                    bool added;
                    do
                    {
                        added = false;
                        for (int i = working.Count - 1; i >= 0; i--)
                        {
                            if (!EndpointsCanJoin(polyline, working[i], closureTolerance, logger))
                            {
                                continue;
                            }

                            try
                            {
                                polyline.JoinEntity(working[i]);
                                working[i].Dispose();
                                working.RemoveAt(i);
                                added = true;
                            }
                            catch (Exception ex)
                            {
                                logger?.Invoke("Curve không thể join vào loop hiện tại: " + ex.Message);
                            }
                        }
                    }
                    while (added);

                    if (polyline.NumberOfVertices < 2)
                    {
                        polyline.Dispose();
                        continue;
                    }

                    if (!polyline.Closed)
                    {
                        double closingGap = polyline.GetPoint3dAt(0)
                            .DistanceTo(polyline.GetPoint3dAt(polyline.NumberOfVertices - 1));
                        if (closingGap <= closureTolerance)
                        {
                            polyline.Closed = true;
                        }
                        else
                        {
                            logger?.Invoke($"Bỏ chuỗi hở; khe đóng {closingGap:0.###} lớn hơn dung sai {closureTolerance:0.###}.");
                            polyline.Dispose();
                            continue;
                        }
                    }

                    try
                    {
                        if (polyline.Area > areaTolerance * areaTolerance)
                        {
                            result.Add(polyline);
                        }
                        else
                        {
                            polyline.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke("Không thể tính diện tích boundary; đã bỏ boundary lỗi: " + ex.Message);
                        polyline.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                foreach (Polyline polyline in result)
                {
                    if (polyline != null && !polyline.IsDisposed) polyline.Dispose();
                }
                result.Clear();
                throw;
            }
            finally
            {
                foreach (Curve curve in working)
                {
                    if (curve != null && !curve.IsDisposed) curve.Dispose();
                }
            }

            return result;
        }

        private static bool EndpointsCanJoin(
            Curve first,
            Curve second,
            double tolerance,
            Action<string> logger)
        {
            try
            {
                Point3d firstStart = first.StartPoint;
                Point3d firstEnd = first.EndPoint;
                Point3d secondStart = second.StartPoint;
                Point3d secondEnd = second.EndPoint;
                return firstStart.DistanceTo(secondStart) <= tolerance ||
                       firstStart.DistanceTo(secondEnd) <= tolerance ||
                       firstEnd.DistanceTo(secondStart) <= tolerance ||
                       firstEnd.DistanceTo(secondEnd) <= tolerance;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Không thể đọc endpoint để kiểm tra join: " + ex.Message);
                return false;
            }
        }

        private static Polyline CurveToPolyline(Curve curve, double tolerance)
        {
            if (curve is Polyline sourcePolyline)
            {
                return sourcePolyline.Clone() as Polyline;
            }

            var polyline = new Polyline();
            if (curve is Line line)
            {
                polyline.AddVertexAt(0, ToPoint2d(line.StartPoint), 0, 0, 0);
                polyline.AddVertexAt(1, ToPoint2d(line.EndPoint), 0, 0, 0);
                return polyline;
            }

            if (curve is Arc arc)
            {
                double bulge = Math.Tan(arc.TotalAngle / 4.0);
                if (arc.Normal.Z < 0) bulge = -bulge;
                polyline.AddVertexAt(0, ToPoint2d(arc.StartPoint), bulge, 0, 0);
                polyline.AddVertexAt(1, ToPoint2d(arc.EndPoint), 0, 0, 0);
                return polyline;
            }

            const int sampleSegments = 24;
            double parameterRange = curve.EndParam - curve.StartParam;
            int vertexIndex = 0;
            Point2d previous = default(Point2d);
            for (int i = 0; i <= sampleSegments; i++)
            {
                double parameter = curve.StartParam + parameterRange * i / sampleSegments;
                Point2d point = ToPoint2d(curve.GetPointAtParameter(parameter));
                if (vertexIndex == 0 || point.GetDistanceTo(previous) > tolerance)
                {
                    polyline.AddVertexAt(vertexIndex++, point, 0, 0, 0);
                    previous = point;
                }
            }

            if (vertexIndex < 2)
            {
                polyline.Dispose();
                return null;
            }
            return polyline;
        }

        public static bool TryGetClusterSnapVector(
            Extents3d clusterExtents,
            IEnumerable<Curve> candidates,
            double searchRadius,
            double vertexTolerance,
            out Vector3d bestShift)
        {
            bestShift = new Vector3d();
            double minimumDistance = searchRadius + vertexTolerance;
            bool found = false;

            Point3d[] corners =
            {
                new Point3d(clusterExtents.MinPoint.X, clusterExtents.MaxPoint.Y, 0),
                new Point3d(clusterExtents.MaxPoint.X, clusterExtents.MaxPoint.Y, 0),
                new Point3d(clusterExtents.MinPoint.X, clusterExtents.MinPoint.Y, 0),
                new Point3d(clusterExtents.MaxPoint.X, clusterExtents.MinPoint.Y, 0)
            };

            foreach (Curve candidate in candidates ?? Enumerable.Empty<Curve>())
            {
                foreach (Point3d externalPoint in GetVertices(candidate))
                {
                    foreach (Point3d corner in corners)
                    {
                        double distance = ToPoint2d(corner).GetDistanceTo(ToPoint2d(externalPoint));
                        if (distance > vertexTolerance && distance < minimumDistance)
                        {
                            minimumDistance = distance;
                            bestShift = externalPoint - corner;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private static IEnumerable<Point3d> GetVertices(Curve curve)
        {
            if (curve is Polyline polyline)
            {
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    yield return polyline.GetPoint3dAt(i);
                }
                yield break;
            }

            yield return curve.StartPoint;
            if (curve.EndPoint.DistanceTo(curve.StartPoint) > 1e-9)
            {
                yield return curve.EndPoint;
            }
        }

        public static Entity CreateBeautifiedClone(
            Entity entity,
            Point2d originalAnchor,
            Point2d targetAnchor,
            double snapStep,
            double vertexTolerance,
            out string rejectionReason)
        {
            rejectionReason = null;

            if (entity is Line sourceLine)
            {
                var line = sourceLine.Clone() as Line;
                Point2d start = SnapPoint(ToPoint2d(sourceLine.StartPoint), originalAnchor, targetAnchor, snapStep);
                Point2d end = SnapPoint(ToPoint2d(sourceLine.EndPoint), originalAnchor, targetAnchor, snapStep);
                if (start.GetDistanceTo(end) <= vertexTolerance)
                {
                    line.Dispose();
                    rejectionReason = "Line bị co về chiều dài bằng 0 sau khi làm đẹp.";
                    return null;
                }

                line.StartPoint = new Point3d(start.X, start.Y, sourceLine.StartPoint.Z);
                line.EndPoint = new Point3d(end.X, end.Y, sourceLine.EndPoint.Z);
                return line;
            }

            if (entity is Polyline sourcePolyline)
            {
                var polyline = sourcePolyline.Clone() as Polyline;
                for (int i = 0; i < sourcePolyline.NumberOfVertices; i++)
                {
                    Point2d snapped = SnapPoint(
                        sourcePolyline.GetPoint2dAt(i),
                        originalAnchor,
                        targetAnchor,
                        snapStep);
                    polyline.SetPointAt(i, snapped);
                }

                for (int i = 1; i < polyline.NumberOfVertices; i++)
                {
                    if (polyline.GetPoint2dAt(i - 1).GetDistanceTo(polyline.GetPoint2dAt(i)) <= vertexTolerance)
                    {
                        polyline.Dispose();
                        rejectionReason = "Hai đỉnh liên tiếp bị trùng sau khi làm đẹp.";
                        return null;
                    }
                }

                if (polyline.Closed &&
                    polyline.NumberOfVertices > 2 &&
                    polyline.GetPoint2dAt(0).GetDistanceTo(polyline.GetPoint2dAt(polyline.NumberOfVertices - 1)) <= vertexTolerance)
                {
                    polyline.Dispose();
                    rejectionReason = "Cạnh đóng bị co về chiều dài bằng 0 sau khi làm đẹp.";
                    return null;
                }

                return polyline;
            }

            rejectionReason = "Chỉ hỗ trợ LINE và LWPOLYLINE.";
            return null;
        }

        private static Point2d SnapPoint(
            Point2d point,
            Point2d originalAnchor,
            Point2d targetAnchor,
            double snapStep)
        {
            double deltaX = point.X - originalAnchor.X;
            double deltaY = point.Y - originalAnchor.Y;
            return new Point2d(
                targetAnchor.X + NumericGeometry.RoundToStep(deltaX, snapStep),
                targetAnchor.Y + NumericGeometry.RoundToStep(deltaY, snapStep));
        }

        public static List<Entity> CreateFinishOffsets(
            Polyline boundary,
            double distance,
            FinishOffsetMode mode,
            Action<string> logger = null)
        {
            if (boundary == null) throw new ArgumentNullException(nameof(boundary));
            if (distance <= 0 || double.IsNaN(distance) || double.IsInfinity(distance))
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }

            List<Entity> positive = GetOffsetEntities(boundary, distance, logger);
            List<Entity> negative = GetOffsetEntities(boundary, -distance, logger);
            var selected = new List<Entity>();
            try
            {
                double originalArea = SafeArea(boundary, logger);
                double positiveArea = positive.Sum(entity => SafeArea(entity, logger));
                double negativeArea = negative.Sum(entity => SafeArea(entity, logger));

                List<Entity> outside;
                List<Entity> inside;
                if (positiveArea >= negativeArea)
                {
                    outside = positive;
                    inside = negative;
                }
                else
                {
                    outside = negative;
                    inside = positive;
                }

                if (outside.Count == 0 &&
                    inside.Count > 0 &&
                    inside.Sum(entity => SafeArea(entity, logger)) > originalArea)
                {
                    outside = inside;
                    inside = new List<Entity>();
                }

                if (mode == FinishOffsetMode.Outside || mode == FinishOffsetMode.Both)
                {
                    selected.AddRange(outside);
                }
                if (mode == FinishOffsetMode.Inside || mode == FinishOffsetMode.Both)
                {
                    selected.AddRange(inside);
                }

                foreach (Entity entity in positive.Concat(negative).Except(selected).Distinct())
                {
                    if (entity != null && !entity.IsDisposed) entity.Dispose();
                }

                return selected;
            }
            catch (Exception)
            {
                foreach (Entity entity in positive.Concat(negative).Distinct())
                {
                    if (entity != null && !entity.IsDisposed) entity.Dispose();
                }
                selected.Clear();
                throw;
            }
        }

        private static List<Entity> GetOffsetEntities(Polyline boundary, double distance, Action<string> logger)
        {
            DBObjectCollection offsets = null;
            var entities = new List<Entity>();
            try
            {
                offsets = boundary.GetOffsetCurves(distance);
                foreach (DBObject item in offsets)
                {
                    if (item is Entity entity)
                    {
                        entities.Add(entity);
                    }
                    else if (item != null && !item.IsDisposed)
                    {
                        item.Dispose();
                    }
                }

                offsets.Dispose();
                offsets = null;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Offset {distance:0.###} thất bại: {ex.Message}");
                if (offsets != null)
                {
                    DisposeCollection(offsets, disposeItems: true);
                    offsets = null;
                }
                foreach (Entity entity in entities)
                {
                    if (entity != null && !entity.IsDisposed) entity.Dispose();
                }
                entities.Clear();
            }
            return entities;
        }

        private static double SafeArea(Entity entity, Action<string> logger)
        {
            try
            {
                return entity is Curve curve ? Math.Abs(curve.Area) : 0;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Không thể tính diện tích curve: " + ex.Message);
                return 0;
            }
        }

        private static Point2d ToPoint2d(Point3d point)
        {
            return new Point2d(point.X, point.Y);
        }
    }
}
