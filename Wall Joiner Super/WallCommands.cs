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
        private void PrepareLogger(Editor ed)
        {
            GeometryProcessor.Logger = msg => 
            {
                try { ed.WriteMessage($"\n[Geometry-Log]: {msg}"); } catch { }
            };
        }

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

            PrepareLogger(ed);

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    HashSet<ObjectId> selectedIdSet = new HashSet<ObjectId>();

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
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\n[WARN] Bỏ qua Extents của đối tượng bị lỗi: {ex.Message}");
                            }
                        }
                    }

                    if (!hasExt) return;

                    Point2d origAnchor = new Point2d(globalExt.MinPoint.X, globalExt.MaxPoint.Y);
                    Vector3d clusterShift = GeometryProcessor.GetClusterSnapVector(globalExt, btr, tr, selectedIdSet);
                    Point2d snappedAnchor = new Point2d(origAnchor.X + clusterShift.X, origAnchor.Y + clusterShift.Y);
                    Point2d finalRoundedAnchor = new Point2d(Math.Round(snappedAnchor.X), Math.Round(snappedAnchor.Y));

                    int processedCount = 0;

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
                            Polyline beautifulPl = GeometryProcessor.BeautifyPolyline(targetPl, origAnchor, finalRoundedAnchor);

                            if (!beautifulPl.Closed) beautifulPl.Closed = true;

                            beautifulPl.Layer = ent.Layer;
                            beautifulPl.Color = ent.Color;

                            btr.AppendEntity(beautifulPl);
                            tr.AddNewlyCreatedDBObject(beautifulPl, true);
                            ent.Erase();

                            // Chỉ thủ công dispose đối tượng trung gian chưa vào DB
                            if (targetPl != ent) targetPl.Dispose(); 

                            processedCount++;
                        }
                    }
                    tr.Commit();
                    ed.WriteMessage($"\n[INFO] Lệnh BW: Thành công. Căn chỉnh Lưới toàn cục và triệt tiêu thập phân cho {processedCount} đối tượng.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ERROR] Sự cố kết thúc lệnh BW (Catch toàn cục bảo vệ máy): {ex.Message}\n{ex.StackTrace}");
            }
        }

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

            PrepareLogger(ed);

            try
            {
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
                                        
                                        bool isOutward = areaOff > pl.Area;
                                        DBObjectCollection finalOffsets = null;

                                        if (isOutward)
                                        {
                                            finalOffsets = offsets;
                                        }
                                        else
                                        {
                                            finalOffsets = pl.GetOffsetCurves(-dist);
                                            // QUAN TRỌNG: finalOffsets giờ là danh sách xài chính, 
                                            // 'offsets' (isOutward=false) chệch hướng, cần dọn dẹp các rác bộ nhớ bên trong nó!
                                            GeometryProcessor.DisposeCollection(offsets);
                                        }

                                        foreach (Entity off in finalOffsets)
                                        {
                                            off.Layer = targetLayer;
                                            off.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                                            btr.AppendEntity(off);
                                            tr.AddNewlyCreatedDBObject(off, true); // TR quản lý, không Dispose
                                        }

                                        if (!isOutward && finalOffsets != null)
                                        {
                                            finalOffsets.Dispose(); // Dọn vỏ biến DBObjectCollection (bên trong đã được db nắm)
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    ed.WriteMessage($"\n[ERROR] Lỗi không thể Offset tạo tường FW: {ex.Message}");
                                }
                                finally
                                {
                                    pl.Dispose(); // Polyline vỏ bọc giải phóng
                                }
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

                    // Giải phóng toàn vẹn rác Cloning trong Pool Cache
                    foreach (var cv in rawCurves)
                    {
                        if (cv != null && !cv.IsDisposed) cv.Dispose();
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[INFO] Lệnh {(isFinishing ? "FW" : "WJ")}: Đã xử lý thành công {clusters.Count} cụm tường.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[CRITICAL ERROR] Sự cố Lệnh tổng (Catch bảo vệ): {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}