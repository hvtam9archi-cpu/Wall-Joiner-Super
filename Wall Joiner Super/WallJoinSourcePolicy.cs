using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    internal static class WallJoinSourcePolicy
    {
        public static void ExecuteReplacingSources(
            Document document,
            IReadOnlyList<ObjectId> selectedIds)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            if (!CanReplaceAllSources(document, selectedIds, out string rejectionReason))
            {
                document.Editor.WriteMessage(
                    "\n[WJ] Không tạo geometry mới vì source không thể được thay thế hoàn toàn: " +
                    rejectionReason);
                return;
            }

            WallJoinWorkflow.ExecuteWallJoin(document, selectedIds, isFinishing: false);
        }

        private static bool CanReplaceAllSources(
            Document document,
            IReadOnlyList<ObjectId> selectedIds,
            out string rejectionReason)
        {
            rejectionReason = null;
            CurveCollectionResult extracted = null;

            try
            {
                using (DocumentLock documentLock = document.LockDocument())
                using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
                {
                    extracted = GeometryProcessor.CollectCurves(
                        transaction,
                        selectedIds.Distinct(),
                        null);

                    foreach (CurveSourceState source in extracted.Sources)
                    {
                        var entity = transaction.GetObject(
                            source.SourceId,
                            OpenMode.ForRead,
                            false) as Entity;
                        if (entity == null)
                        {
                            rejectionReason = $"{source.SourceId.Handle}: entity không còn tồn tại.";
                            return false;
                        }

                        source.IsOnLockedLayer = LayerService.IsEntityOnLockedLayer(entity, transaction);

                        if (source.IsOnLockedLayer)
                        {
                            rejectionReason = $"{source.SourceId.Handle}: layer đang khóa.";
                            return false;
                        }

                        if (source.ExtractionFailed || source.CurveCount <= 0)
                        {
                            rejectionReason = $"{source.SourceId.Handle}: không trích xuất đầy đủ geometry.";
                            return false;
                        }

                        if (source.HasUnsupportedContent)
                        {
                            rejectionReason =
                                $"{source.SourceId.Handle}: block chứa nội dung ngoài geometry được hỗ trợ.";
                            return false;
                        }
                    }

                    if (extracted.Sources.Count != selectedIds.Distinct().Count())
                    {
                        rejectionReason = "không snapshot được toàn bộ selection.";
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                extracted?.DisposeCurves();
            }
        }
    }
}
