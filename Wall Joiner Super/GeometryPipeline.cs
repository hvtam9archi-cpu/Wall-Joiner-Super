using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    internal static class GeometryPipeline
    {
        public static BoundaryBuildResult ProcessClusterToBoundaries(
            IReadOnlyList<Curve> cluster,
            double gapTolerance,
            double vertexTolerance,
            Action<string> logger = null)
        {
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));

            var result = new BoundaryBuildResult();
            List<Curve> workingCurves = CloneAndCleanCurves(cluster, logger);
            var bridges = new List<Curve>();
            try
            {
                if (workingCurves.Count == 0)
                {
                    result.Warnings.Add("Cụm curve không còn hình học hợp lệ sau bước clean.");
                    return result;
                }

                string regionError;
                TryBuildRegionBoundaries(
                    workingCurves,
                    vertexTolerance,
                    logger,
                    result.Boundaries,
                    out regionError);

                if (result.Boundaries.Count == 0)
                {
                    bridges = GeometryKernelAdapter.CreateSafeBridges(
                        workingCurves,
                        gapTolerance,
                        vertexTolerance,
                        logger);
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
                            result.Warnings.Add("Region sau safe bridge thất bại: " + bridgedRegionError);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(regionError))
                    {
                        result.Warnings.Add("Region ban đầu thất bại: " + regionError);
                    }
                }

                if (result.Boundaries.Count == 0)
                {
                    result.UsedFallback = true;
                    List<Polyline> topologyLoops = GeometryKernelAdapter.StitchClosedLoops(
                        workingCurves,
                        vertexTolerance,
                        logger);
                    result.Boundaries.AddRange(topologyLoops);

                    if (topologyLoops.Count == 0)
                    {
                        result.Warnings.Add(
                            "F# topology không tìm được loop kín; cụm được bỏ qua thay vì tự đóng cạnh dài hoặc nối mơ hồ.");
                    }
                    else
                    {
                        result.Warnings.Add(
                            "Đã dựng boundary bằng F# topology fallback sau khi Region không tạo được kết quả.");
                    }
                }

                return result;
            }
            catch
            {
                result.DisposeBoundaries();
                throw;
            }
            finally
            {
                foreach (Curve curve in workingCurves)
                {
                    if (curve != null && !curve.IsDisposed && curve.Database == null)
                    {
                        curve.Dispose();
                    }
                }
            }
        }

        private static List<Curve> CloneAndCleanCurves(
            IEnumerable<Curve> curves,
            Action<string> logger)
        {
            var result = new List<Curve>();
            foreach (Curve curve in curves)
            {
                if (curve == null) continue;

                try
                {
                    Curve clone;
                    if (curve is Polyline polyline)
                    {
                        clone = GeometryKernelAdapter.NormalizeAndCleanPolyline(
                            polyline,
                            reverseBulges: false,
                            logger: logger);
                    }
                    else
                    {
                        clone = curve.Clone() as Curve;
                    }

                    if (clone != null) result.Add(clone);
                }
                catch (System.Exception ex)
                {
                    logger?.Invoke("Không thể clean/clone curve trước topology: " + ex.Message);
                }
            }
            return result;
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
                    Region current = unmergedRegions[0];
                    unmergedRegions.RemoveAt(0);
                    MergeRegion(mergedRegions, current, logger);
                }

                foreach (Region region in mergedRegions)
                {
                    DBObjectCollection exploded = new DBObjectCollection();
                    try
                    {
                        region.Explode(exploded);
                        List<Curve> parts = exploded
                            .Cast<DBObject>()
                            .OfType<Curve>()
                            .ToList();
                        List<Polyline> loops = GeometryKernelAdapter.StitchClosedLoops(
                            parts,
                            vertexTolerance,
                            logger);
                        foreach (Polyline loop in loops) boundaries.Add(loop);
                    }
                    catch (System.Exception ex)
                    {
                        logger?.Invoke("Không thể dựng topology từ Region: " + ex.Message);
                    }
                    finally
                    {
                        GeometryProcessor.DisposeCollection(exploded, disposeItems: true);
                    }
                }

                return boundaries.Count > countBefore;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                logger?.Invoke("Region pipeline: " + ex);
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

        private static void MergeRegion(
            List<Region> mergedRegions,
            Region current,
            Action<string> logger)
        {
            for (int i = mergedRegions.Count - 1; i >= 0; i--)
            {
                Region existing = mergedRegions[i];
                bool mayIntersect = true;
                try
                {
                    Extents3d first = existing.GeometricExtents;
                    Extents3d second = current.GeometricExtents;
                    mayIntersect =
                        first.MinPoint.X <= second.MaxPoint.X &&
                        first.MaxPoint.X >= second.MinPoint.X &&
                        first.MinPoint.Y <= second.MaxPoint.Y &&
                        first.MaxPoint.Y >= second.MinPoint.Y;
                }
                catch (System.Exception ex)
                {
                    logger?.Invoke("Không đọc được Region extents; vẫn thử union: " + ex.Message);
                }

                if (!mayIntersect) continue;

                try
                {
                    current.BooleanOperation(BooleanOperationType.BoolUnite, existing);
                    existing.Dispose();
                    mergedRegions.RemoveAt(i);
                }
                catch (System.Exception ex)
                {
                    logger?.Invoke("Region union bị bỏ qua: " + ex.Message);
                }
            }

            mergedRegions.Add(current);
        }
    }
}
