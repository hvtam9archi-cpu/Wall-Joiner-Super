using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ProWallTools
{
    public static class GeometryProcessor
    {
        public static Action<string> Logger { get; set; }

        private static void LogMessage(string msg)
        {
            Logger?.Invoke(msg);
        }

        public static void DisposeCollection(DBObjectCollection coll)
        {
            if (coll != null)
            {
                foreach (DBObject obj in coll)
                {
                    if (obj != null && !obj.IsDisposed)
                        obj.Dispose();
                }
                coll.Dispose();
            }
        }

        // =======================================================================
        // 1. TỪ TÍNH BẮT ĐIỂM CHO TOÀN BỘ BOUNDING BOX TỔNG
        // =======================================================================
        public static Vector3d GetClusterSnapVector(Extents3d clusterExt, BlockTableRecord btr, Transaction tr, HashSet<ObjectId> ignoreIds)
        {
            double searchRadius = 10.0;
            Extents3d searchBox = new Extents3d(
                new Point3d(clusterExt.MinPoint.X - searchRadius, clusterExt.MinPoint.Y - searchRadius, 0),
                new Point3d(clusterExt.MaxPoint.X + searchRadius, clusterExt.MaxPoint.Y + searchRadius, 0)
            );

            double minDistance = searchRadius + 1e-4;
            Vector3d bestShift = Vector3d.Zero;

            foreach (ObjectId id in btr)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (ignoreIds != null && ignoreIds.Contains(id)) continue;

                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !(ent is Curve cv)) continue;

                try
                {
                    if (!IsIntersect(searchBox, cv.GeometricExtents)) continue;

                    List<Point3d> externalVerts = GetVertices(cv);

                    Point3d[] bboxCorners = {
                        new Point3d(clusterExt.MinPoint.X, clusterExt.MaxPoint.Y, 0), 
                        new Point3d(clusterExt.MaxPoint.X, clusterExt.MaxPoint.Y, 0), 
                        new Point3d(clusterExt.MinPoint.X, clusterExt.MinPoint.Y, 0), 
                        new Point3d(clusterExt.MaxPoint.X, clusterExt.MinPoint.Y, 0)  
                    };

                    foreach (Point3d corner in bboxCorners)
                    {
                        foreach (Point3d extPt in externalVerts)
                        {
                            double dist = new Point2d(corner.X, corner.Y).GetDistanceTo(new Point2d(extPt.X, extPt.Y));
                            if (dist > 1e-4 && dist <= minDistance)
                            {
                                minDistance = dist;
                                bestShift = new Vector3d(extPt.X - corner.X, extPt.Y - corner.Y, 0);
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    LogMessage($"Lỗi quét từ tính đối tượng {id.Handle}: {ex.Message}");
                }
            }
            return bestShift;
        }

        // =======================================================================
        // 2. LÀM TRÒN LƯỚI TOÀN CỤC (TRIỆT TIÊU THẬP PHÂN TUYỆT ĐỐI)
        // =======================================================================
        public static double CustomRound(double val)
        {
            int sign = Math.Sign(val);
            val = Math.Abs(val);
            double tens = Math.Floor(val / 10.0) * 10.0;
            double units = val - tens;

            double rounded;
            if (units < 5.0 - 1e-4) rounded = tens + 0.0;
            else if (units <= 5.0 + 1e-4) rounded = tens + 5.0;
            else rounded = tens + 10.0;

            return sign < 0 ? -rounded : rounded;
        }

        public static Polyline BeautifyPolyline(Polyline pl, Point2d origAnchor, Point2d finalRoundedAnchor)
        {
            if (pl.NumberOfVertices < 2) return pl;

            Polyline cleanPl = new Polyline();
            cleanPl.Elevation = 0;
            cleanPl.Normal = Vector3d.ZAxis;

            int validIdx = 0;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d pt = pl.GetPoint2dAt(i);
                double dx = pt.X - origAnchor.X;
                double dy = pt.Y - origAnchor.Y;

                Point2d snappedPt = new Point2d(
                    finalRoundedAnchor.X + CustomRound(dx),
                    finalRoundedAnchor.Y + CustomRound(dy)
                );

                if (validIdx > 0)
                {
                    if (snappedPt.GetDistanceTo(cleanPl.GetPoint2dAt(validIdx - 1)) > 1e-4)
                        cleanPl.AddVertexAt(validIdx++, snappedPt, 0, 0, 0);
                }
                else
                {
                    cleanPl.AddVertexAt(validIdx++, snappedPt, 0, 0, 0);
                }
            }

            if (cleanPl.NumberOfVertices > 1 && cleanPl.GetPoint2dAt(0).GetDistanceTo(cleanPl.GetPoint2dAt(cleanPl.NumberOfVertices - 1)) < 1e-4)
            {
                cleanPl.RemoveVertexAt(cleanPl.NumberOfVertices - 1);
            }
            return cleanPl;
        }

        // =======================================================================
        // CÁC HÀM TIỆN ÍCH BỔ TRỢ ĐÃ FIX DISPOSE MEMORY LEAK VÀ XÓA CATCH TRỐNG
        // =======================================================================
        private static List<Point3d> GetVertices(Curve cv)
        {
            List<Point3d> pts = new List<Point3d>();
            if (cv is Polyline pl) { for (int i = 0; i < pl.NumberOfVertices; i++) pts.Add(pl.GetPoint3dAt(i)); }
            else if (cv is Line ln) { pts.Add(ln.StartPoint); pts.Add(ln.EndPoint); }
            return pts;
        }

        public static List<Polyline> ProcessClusterToBoundaries(List<Curve> cluster)
        {
            List<Curve> bridges = CreateBridges(cluster);
            cluster.AddRange(bridges);

            List<Polyline> rawBoundaries = new List<Polyline>();
            using (DBObjectCollection input = new DBObjectCollection())
            {
                foreach (var c in cluster) input.Add(c);

                try
                {
                    using (DBObjectCollection regions = Region.CreateFromCurves(input))
                    {
                        if (regions.Count > 0)
                        {
                            Region uni = (Region)regions[0];
                            for (int i = 1; i < regions.Count; i++)
                            {
                                Region sec = (Region)regions[i];
                                try
                                {
                                    uni.BooleanOperation(BooleanOperationType.BoolUnite, sec);
                                }
                                catch (System.Exception ex)
                                {
                                    LogMessage($"Lỗi BooleanOperation gộp vùng: {ex.Message}");
                                }
                                finally
                                {
                                    // Đảm bảo Dispose Region hợp phần sau khi Unite
                                    if (sec != null && !sec.IsDisposed) sec.Dispose();
                                }
                            }

                            using (DBObjectCollection exploded = new DBObjectCollection())
                            {
                                uni.Explode(exploded);
                                rawBoundaries = JoinCurves(exploded.Cast<Curve>().ToList());
                                // Rất quan trọng: Dispose triệt để object sinh ra từ Explode
                                DisposeCollection(exploded);
                            }
                            if (uni != null && !uni.IsDisposed) uni.Dispose();
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    LogMessage($"Lỗi lúc tạo Region từ Curve: {ex.Message}");
                }
            }

            // Dọn dẹp curves nối rác
            foreach (var br in bridges) br.Dispose();

            if (rawBoundaries.Count == 0) rawBoundaries = JoinCurves(cluster);
            return rawBoundaries;
        }

        private static List<Curve> CreateBridges(List<Curve> curves)
        {
            List<Curve> bridges = new List<Curve>();
            // Tối ưu bằng Extents3d tránh O(N^2) quá nặng
            for (int i = 0; i < curves.Count; i++)
            {
                Point3d[] pts = { curves[i].StartPoint, curves[i].EndPoint };
                Extents3d ext1 = GetBufferedExtents(curves[i], WallConstants.GapTolerance);

                for (int j = i + 1; j < curves.Count; j++) 
                {
                    Extents3d ext2 = GetBufferedExtents(curves[j], WallConstants.GapTolerance);
                    if (!IsIntersect(ext1, ext2)) continue; // Lọc nhanh bằng BoundingBox Bounding Hashing

                    try
                    {
                        foreach (Point3d pt in pts)
                        {
                            Point3d closest = curves[j].GetClosestPointTo(pt, false);
                            double dist = pt.DistanceTo(closest);
                            if (dist > 1e-4 && dist <= WallConstants.GapTolerance) 
                            {
                                bridges.Add(new Line(pt, closest));
                            }
                        }

                        // Lặp ngược do đã cắt vòng lặp qua j = i + 1
                        Point3d[] jPts = { curves[j].StartPoint, curves[j].EndPoint };
                        foreach (Point3d jp in jPts)
                        {
                            Point3d jClosest = curves[i].GetClosestPointTo(jp, false);
                            double jDist = jp.DistanceTo(jClosest);
                            if (jDist > 1e-4 && jDist <= WallConstants.GapTolerance)
                            {
                                bridges.Add(new Line(jp, jClosest));
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        LogMessage($"Lỗi tính điểm nối khe hở: {ex.Message}");
                    }
                }
            }
            return bridges;
        }

        public static List<Curve> CollectCurves(Transaction tr, IEnumerable<ObjectId> ids)
        {
            List<Curve> results = new List<Curve>();
            foreach (ObjectId id in ids)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                ExtractRecursive(ent, results);
            }
            return results;
        }

        private static void ExtractRecursive(Entity ent, List<Curve> results)
        {
            if (ent is BlockReference br)
            {
                using (DBObjectCollection exploded = new DBObjectCollection())
                {
                    try 
                    { 
                        br.Explode(exploded); 
                        foreach (Entity sub in exploded)
                        {
                            ExtractRecursive(sub, results);
                        }
                    } 
                    catch (System.Exception ex) 
                    {
                        LogMessage($"Lỗi bung BlockReference: {ex.Message}");
                    }
                    finally
                    {
                        // Rất quan trọng: Dispose Entity rác bung ra từ Block để tránh Leak RAM
                        DisposeCollection(exploded);
                    }
                }
            }
            else if (ent is Curve cv) 
            { 
                Curve c = Sanitize(cv); 
                if (c != null) results.Add(c); 
            }
            
            // Dispose entity lẻ mà tự Explode sinh ra không nằm trong quản lý Database
            if (ent != null && ent.Database == null && !ent.IsDisposed) ent.Dispose();
        }

        private static Curve Sanitize(Curve cv)
        {
            try
            {
                if (cv.EndParam - cv.StartParam < 1e-6) return null;
                Curve clone = cv.Clone() as Curve;
                if (clone is Polyline pl) { pl.Normal = Vector3d.ZAxis; pl.Elevation = 0; }
                else if (clone is Line ln) { ln.StartPoint = new Point3d(ln.StartPoint.X, ln.StartPoint.Y, 0); ln.EndPoint = new Point3d(ln.EndPoint.X, ln.EndPoint.Y, 0); }
                return clone;
            }
            catch (System.Exception ex) 
            {
                LogMessage($"Cảnh báo làm sạch Curve: {ex.Message}");
                return null; 
            }
        }

        public static List<List<Curve>> ClusterCurves(List<Curve> allCurves)
        {
            List<List<Curve>> clusters = new List<List<Curve>>(); 
            List<Curve> pool = new List<Curve>(allCurves);

            while (pool.Count > 0)
            {
                List<Curve> currentCluster = new List<Curve>(); 
                Curve seed = pool[0]; 
                pool.RemoveAt(0); 
                currentCluster.Add(seed);

                for (int i = 0; i < currentCluster.Count; i++)
                {
                    Extents3d memberExt = GetBufferedExtents(currentCluster[i], WallConstants.GapTolerance);
                    for (int j = pool.Count - 1; j >= 0; j--)
                    {
                        if (IsIntersect(memberExt, pool[j].GeometricExtents)) 
                        { 
                            currentCluster.Add(pool[j]); 
                            pool.RemoveAt(j); 
                        }
                    }
                }
                clusters.Add(currentCluster);
            }
            return clusters;
        }

        private static Extents3d GetBufferedExtents(Curve cv, double buffer)
        {
            try
            {
                Extents3d e = cv.GeometricExtents;
                return new Extents3d(new Point3d(e.MinPoint.X - buffer, e.MinPoint.Y - buffer, 0), new Point3d(e.MaxPoint.X + buffer, e.MaxPoint.Y + buffer, 0));
            }
            catch
            {
                // Khá hiếm nhưng geometric bounds với object hư cấu có thể failed. Bỏ qua. Xử lý an toàn nhất.
                return new Extents3d();
            }
        }

        private static bool IsIntersect(Extents3d e1, Extents3d e2)
        {
            if (e1.MinPoint == e1.MaxPoint && e2.MinPoint == e2.MaxPoint) return false;
            return (e1.MinPoint.X <= e2.MaxPoint.X && e1.MaxPoint.X >= e2.MinPoint.X && e1.MinPoint.Y <= e2.MaxPoint.Y && e1.MaxPoint.Y >= e2.MinPoint.Y);
        }

        private static List<Polyline> JoinCurves(List<Curve> parts)
        {
            List<Polyline> res = new List<Polyline>();
            while (parts.Count > 0)
            {
                Curve c = parts[0]; parts.RemoveAt(0); 
                Polyline pl = (c is Polyline) ? (Polyline)c.Clone() : new Polyline();
                
                if (!(c is Polyline)) 
                { 
                    pl.AddVertexAt(0, new Point2d(c.StartPoint.X, c.StartPoint.Y), 0, 0, 0); 
                    pl.AddVertexAt(1, new Point2d(c.EndPoint.X, c.EndPoint.Y), 0, 0, 0); 
                }

                bool added = true;
                while (added) 
                { 
                    added = false; 
                    for (int i = parts.Count - 1; i >= 0; i--) 
                    {
                        try 
                        { 
                            pl.JoinEntity(parts[i]); 
                            parts.RemoveAt(i); 
                            added = true; 
                        } 
                        catch (System.Exception) 
                        {
                            // Hành vi có chủ đích: Phép vòng lặp là Try-Join. 
                            // Việc chối từ nối vào xảy ra rất tự nhiên nếu đường nét chưa tới lượt chạm cạnh.
                            // Không được phép log ở đây để tránh Spam Output của User.
                        } 
                    } 
                }
                
                pl.Closed = true; 
                if (pl.Area > 1e-4) res.Add(pl); 
                else pl.Dispose();
            }
            return res;
        }
    }
}