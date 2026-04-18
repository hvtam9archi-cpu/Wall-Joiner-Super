using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Geometry;

[assembly: CommandClass(typeof(ProWallTools.WallCommands))]

namespace ProWallTools
{
    public class WallCommands
    {
        // =======================================================================
        // LỆNH 1: BW - NẮN THẲNG, LÀM TRÒN VÀ KHÉP KÍN TRÊN LƯỚI ẢO
        // =======================================================================
        [CommandMethod("BW", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void BeautifyWallsCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var sel = ed.GetSelection(new SelectionFilter(new[] { new TypedValue(0, "LINE,LWPOLYLINE,POLYLINE") }));
            if (sel.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                HashSet<ObjectId> selectedIdSet = new HashSet<ObjectId>();

                // Bước 1: Tính toán Bounding Box tổng của toàn bộ vùng chọn
                Extents3d globalExt = new Extents3d();
                bool hasExt = false;

                foreach (SelectedObject so in sel.Value)
                {
                    selectedIdSet.Add(so.ObjectId);
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        try
                        {
                            if (!hasExt) { globalExt = ent.GeometricExtents; hasExt = true; }
                            else { globalExt.AddExtents(ent.GeometricExtents); }
                        }
                        catch { }
                    }
                }

                if (!hasExt) return;

                // Gốc tọa độ nguyên bản: Góc Trên - Bên Trái của Bounding Box
                Point2d origAnchor = new Point2d(globalExt.MinPoint.X, globalExt.MaxPoint.Y);

                // Quét từ tính cho toàn bộ khối: Có đối tượng (cột, tường cũ) nào sát góc Bounding Box không?
                Vector3d clusterShift = GeometryProcessor.GetClusterSnapVector(globalExt, btr, tr, selectedIdSet);

                // Gốc tọa độ sau khi bị hút từ tính
                Point2d snappedAnchor = new Point2d(origAnchor.X + clusterShift.X, origAnchor.Y + clusterShift.Y);

                // BƯỚC QUAN TRỌNG NHẤT: Ép Gốc tọa độ về SỐ NGUYÊN để triệt tiêu vĩnh viễn số lẻ thập phân
                Point2d finalRoundedAnchor = new Point2d(Math.Round(snappedAnchor.X), Math.Round(snappedAnchor.Y));

                int processedCount = 0;

                // Bước 2: Duyệt và xử lý từng đối tượng dựa trên Lưới Toàn Cục (FinalRoundedAnchor)
                foreach (ObjectId id in selectedIdSet)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    Polyline targetPl = null;

                    if (ent is Polyline pl)
                    {
                        targetPl = pl;
                    }
                    else if (ent is Line ln)
                    {
                        targetPl = new Polyline();
                        targetPl.AddVertexAt(0, new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0, 0, 0);
                        targetPl.AddVertexAt(1, new Point2d(ln.EndPoint.X, ln.EndPoint.Y), 0, 0, 0);
                    }

                    if (targetPl != null)
                    {
                        // Truyền origAnchor (để tính khoảng cách cũ) và finalRoundedAnchor (để xuất tọa độ mới chẵn 100%)
                        Polyline beautifulPl = GeometryProcessor.BeautifyPolyline(targetPl, origAnchor, finalRoundedAnchor);

                        if (!beautifulPl.Closed) beautifulPl.Closed = true;

                        beautifulPl.Layer = ent.Layer;
                        beautifulPl.Color = ent.Color;

                        btr.AppendEntity(beautifulPl);
                        tr.AddNewlyCreatedDBObject(beautifulPl, true);
                        ent.Erase();

                        processedCount++;
                    }
                }
                tr.Commit();
                ed.WriteMessage($"\n[INFO] Lệnh BW: Căn chỉnh Lưới toàn cục và khép kín {processedCount} đối tượng (Triệt tiêu thập phân).");
            }
        }

        // Lệnh SW, WJ, FW giữ nguyên... (Để tiết kiệm không gian và tránh nhầm lẫn, các lệnh WJ/FW/SW giữ hệt như phiên bản trước)

        [CommandMethod("WJ", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void WallJoin() => RunBooleanLogic(false);

        [CommandMethod("FW", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void FinishingWall() => RunBooleanLogic(true);

        private void RunBooleanLogic(bool isFinishing)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var sel = ed.GetSelection(new SelectionFilter(new[] { new TypedValue(0, "LINE,LWPOLYLINE,POLYLINE,INSERT") }));
            if (sel.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                LayerService.EnsureLayersExist(db, tr);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                List<ObjectId> selectedIds = new List<ObjectId>();
                foreach (SelectedObject so in sel.Value) selectedIds.Add(so.ObjectId);

                List<Curve> rawCurves = GeometryProcessor.CollectCurves(tr, selectedIds);
                List<List<Curve>> clusters = GeometryProcessor.ClusterCurves(rawCurves);

                foreach (var cluster in clusters)
                {
                    List<Polyline> boundaries = GeometryProcessor.ProcessClusterToBoundaries(cluster);
                    string targetLayer = isFinishing ? WallConstants.CurrentFinishLayer : WallConstants.CurrentWallLayer;

                    foreach (var pl in boundaries)
                    {
                        if (isFinishing)
                        {
                            try
                            {
                                double dist = WallConstants.DefaultOffset;
                                using (DBObjectCollection offsets = pl.GetOffsetCurves(dist))
                                {
                                    double areaOff = 0;
                                    foreach (Entity ent in offsets) if (ent is Curve c) areaOff += c.Area;
                                    DBObjectCollection finalOffsets = (areaOff > pl.Area) ? offsets : pl.GetOffsetCurves(-dist);
                                    foreach (Entity off in finalOffsets)
                                    {
                                        off.Layer = targetLayer;
                                        off.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                                        btr.AppendEntity(off);
                                        tr.AddNewlyCreatedDBObject(off, true);
                                    }
                                    if (areaOff <= pl.Area) finalOffsets.Dispose();
                                }
                            }
                            catch { }
                            pl.Dispose();
                        }
                        else
                        {
                            pl.Layer = targetLayer;
                            pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                            btr.AppendEntity(pl);
                            tr.AddNewlyCreatedDBObject(pl, true);
                        }
                    }
                }

                if (!isFinishing)
                {
                    foreach (var id in selectedIds)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null && !ent.IsErased) ent.Erase();
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\n[INFO] Đã gộp và xử lý {clusters.Count} cụm tường.");
            }
        }
    }
}