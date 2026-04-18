using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ProWallTools
{
    public static class GeometryProcessor
    {
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

            // Quét các đối tượng bên ngoài
            foreach (ObjectId id in btr)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (ignoreIds != null && ignoreIds.Contains(id)) continue;

                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !(ent is Curve cv)) continue;

                try
                {
                    if (!IsIntersect(searchBox, cv.GeometricExtents)) continue;

                    // Lấy đỉnh ngoại vi
                    List<Point3d> externalVerts = GetVertices(cv);

                    // So sánh với 4 góc của Bounding Box
                    Point3d[] bboxCorners = {
                        new Point3d(clusterExt.MinPoint.X, clusterExt.MaxPoint.Y, 0), // Top-Left
                        new Point3d(clusterExt.MaxPoint.X, clusterExt.MaxPoint.Y, 0), // Top-Right
                        new Point3d(clusterExt.MinPoint.X, clusterExt.MinPoint.Y, 0), // Bottom-Left
                        new Point3d(clusterExt.MaxPoint.X, clusterExt.MinPoint.Y, 0)  // Bottom-Right
                    };

                    foreach (Point3d corner in bboxCorners)
                    {
                        foreach (Point3d extPt in externalVerts)
                        {
                            double dist = new Point2d(corner.X, corner.Y).GetDistanceTo(new Point2d(extPt.X, extPt.Y));
                            if (dist > 1e-4 && dist <= minDistance)
                            {
                                minDistance = dist;
                                // Vector dịch chuyển cụm
                                bestShift = new Vector3d(extPt.X - corner.X, extPt.Y - corner.Y, 0);
                            }
                        }
                    }
                }
                catch { }
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

        /// <summary>
        /// Nắn thẳng và định vị đối tượng trên Lưới Toàn Cục ảo
        /// </summary>
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

                // Khoảng cách ban đầu so với Gốc Bounding Box (chưa làm tròn)
                double dx = pt.X - origAnchor.X;
                double dy = pt.Y - origAnchor.Y;

                // CÔNG THỨC VÀNG: Gốc Số Nguyên + Khoảng Cách Chẵn = Tuyệt đối KHÔNG thập phân
                // Mọi đối tượng ở gần viền Bounding Box (dx ~ 0) sẽ có CustomRound(0) = 0 -> Trùng sát viền
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
        // CÁC HÀM TIỆN ÍCH BỔ TRỢ (Giữ nguyên như phiên bản trước)
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
            cluster.AddRange(CreateBridges(cluster));
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
                            for (int i = 1; i < regions.Count; i++) uni.BooleanOperation(BooleanOperationType.BoolUnite, (Region)regions[i]);
                            using (DBObjectCollection exploded = new DBObjectCollection())
                            {
                                uni.Explode(exploded);
                                rawBoundaries = JoinCurves(exploded.Cast<Curve>().ToList());
                            }
                        }
                    }
                }
                catch { }
            }
            if (rawBoundaries.Count == 0) rawBoundaries = JoinCurves(cluster);
            return rawBoundaries;
        }

        private static List<Curve> CreateBridges(List<Curve> curves)
        {
            List<Curve> bridges = new List<Curve>();
            for (int i = 0; i < curves.Count; i++)
            {
                Point3d[] pts = { curves[i].StartPoint, curves[i].EndPoint };
                foreach (Point3d pt in pts)
                {
                    for (int j = 0; j < curves.Count; j++)
                    {
                        if (i == j) continue;
                        try
                        {
                            Point3d closest = curves[j].GetClosestPointTo(pt, false);
                            double dist = pt.DistanceTo(closest);
                            if (dist > 1e-4 && dist <= WallConstants.GapTolerance) bridges.Add(new Line(pt, closest));
                        }
                        catch { }
                    }
                }
            }
            return bridges;
        }

        public static List<Curve> CollectCurves(Transaction tr, IEnumerable<ObjectId> ids)
        {
            List<Curve> results = new List<Curve>();
            foreach (ObjectId id in ids) { Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity; ExtractRecursive(ent, results); }
            return results;
        }
        private static void ExtractRecursive(Entity ent, List<Curve> results)
        {
            if (ent is BlockReference br)
            {
                using (DBObjectCollection exploded = new DBObjectCollection())
                {
                    try { br.Explode(exploded); } catch { return; }
                    foreach (Entity sub in exploded) ExtractRecursive(sub, results);
                }
            }
            else if (ent is Curve cv) { Curve c = Sanitize(cv); if (c != null) results.Add(c); }
            if (ent != null && ent.Database == null) ent.Dispose();
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
            catch { return null; }
        }
        public static List<List<Curve>> ClusterCurves(List<Curve> allCurves)
        {
            List<List<Curve>> clusters = new List<List<Curve>>(); List<Curve> pool = new List<Curve>(allCurves);
            while (pool.Count > 0)
            {
                List<Curve> currentCluster = new List<Curve>(); Curve seed = pool[0]; pool.RemoveAt(0); currentCluster.Add(seed);
                for (int i = 0; i < currentCluster.Count; i++)
                {
                    Extents3d memberExt = GetBufferedExtents(currentCluster[i], WallConstants.GapTolerance);
                    for (int j = pool.Count - 1; j >= 0; j--)
                    {
                        if (IsIntersect(memberExt, pool[j].GeometricExtents)) { currentCluster.Add(pool[j]); pool.RemoveAt(j); }
                    }
                }
                clusters.Add(currentCluster);
            }
            return clusters;
        }
        private static Extents3d GetBufferedExtents(Curve cv, double buffer)
        {
            Extents3d e = cv.GeometricExtents;
            return new Extents3d(new Point3d(e.MinPoint.X - buffer, e.MinPoint.Y - buffer, 0), new Point3d(e.MaxPoint.X + buffer, e.MaxPoint.Y + buffer, 0));
        }
        private static bool IsIntersect(Extents3d e1, Extents3d e2)
        {
            return (e1.MinPoint.X <= e2.MaxPoint.X && e1.MaxPoint.X >= e2.MinPoint.X && e1.MinPoint.Y <= e2.MaxPoint.Y && e1.MaxPoint.Y >= e2.MinPoint.Y);
        }
        private static List<Polyline> JoinCurves(List<Curve> parts)
        {
            List<Polyline> res = new List<Polyline>();
            while (parts.Count > 0)
            {
                Curve c = parts[0]; parts.RemoveAt(0); Polyline pl = (c is Polyline) ? (Polyline)c.Clone() : new Polyline();
                if (!(c is Polyline)) { pl.AddVertexAt(0, new Point2d(c.StartPoint.X, c.StartPoint.Y), 0, 0, 0); pl.AddVertexAt(1, new Point2d(c.EndPoint.X, c.EndPoint.Y), 0, 0, 0); }
                bool added = true;
                while (added) { added = false; for (int i = parts.Count - 1; i >= 0; i--) try { pl.JoinEntity(parts[i]); parts.RemoveAt(i); added = true; } catch { } }
                pl.Closed = true; if (pl.Area > 1e-4) res.Add(pl); else pl.Dispose();
            }
            return res;
        }
    }
}