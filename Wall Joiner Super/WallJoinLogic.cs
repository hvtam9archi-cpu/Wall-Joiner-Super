using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ProWallTools
{
    public static class WallJoinLogic
    {
        private const string NearbyCurveDxfTypes = "LINE,LWPOLYLINE,POLYLINE,ARC,CIRCLE,ELLIPSE,SPLINE";

        private sealed class BeautifySourceSnapshot : IDisposable
        {
            public ObjectId SourceId { get; set; }
            public Entity Entity { get; set; }

            public void Dispose()
            {
                if (Entity != null && !Entity.IsDisposed)
                {
                    Entity.Dispose();
                }
                Entity = null;
            }
        }

        private sealed class BeautifyReadSnapshot : IDisposable
        {
            public List<BeautifySourceSnapshot> Sources { get; } = new List<BeautifySourceSnapshot>();
            public Extents3d Extents { get; set; }
            public bool HasExtents { get; set; }

            public void Dispose()
            {
                foreach (BeautifySourceSnapshot source in Sources)
                {
                    source.Dispose();
                }
                Sources.Clear();
            }
        }

        private sealed class Replacement
        {
            public ObjectId SourceId { get; set; }
            public Entity NewEntity { get; set; }
        }

        public static void ExecuteBeautifyWalls(
            Document document,
            IReadOnlyList<ObjectId> selectedIds)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            Editor editor = document.Editor;
            Database database = document.Database;
            WallSettings settings = WallSettingsService.Current;
            ValidateSettings(settings);

            var result = new WallOperationResult(WallConstants.BeautifyCommandName)
            {
                SelectedCount = selectedIds.Count
            };
            List<ObjectId> validIds = ValidateObjectIds(database, selectedIds, result);
            if (validIds.Count == 0 ||
                (settings.StrictMode && validIds.Count != selectedIds.Count))
            {
                result.AddWarning("Không có tập ObjectId hợp lệ thuộc document hiện tại.");
                AbortWithoutChanges(result, editor, strictModeFailed: false, null);
                return;
            }

            var replacements = new List<Replacement>();
            try
            {
                using (DocumentLock documentLock = document.LockDocument())
                using (BeautifyReadSnapshot snapshot = ReadBeautifySnapshot(database, validIds, result))
                {
                    if (!snapshot.HasExtents || snapshot.Sources.Count == 0)
                    {
                        result.AddWarning("Không tạo được snapshot hình học hợp lệ.");
                        result.SkippedCount = selectedIds.Count;
                        result.WriteSummary(editor);
                        return;
                    }

                    Point2d originalAnchor = new Point2d(
                        snapshot.Extents.MinPoint.X,
                        snapshot.Extents.MaxPoint.Y);
                    IReadOnlyList<ObjectId> nearbyIds = CollectNearbyObjectIds(
                        editor,
                        snapshot.Extents,
                        settings.SnapRadius,
                        validIds);
                    List<Curve> nearbyCurves = ReadCurveClones(database, nearbyIds, result);
                    Point2d targetAnchor;
                    try
                    {
                        bool snappedToExisting = GeometryProcessor.TryGetClusterSnapVector(
                            snapshot.Extents,
                            nearbyCurves,
                            settings.SnapRadius,
                            settings.VertexTolerance,
                            out Vector3d snapVector);
                        targetAnchor = snappedToExisting
                            ? new Point2d(originalAnchor.X + snapVector.X, originalAnchor.Y + snapVector.Y)
                            : new Point2d(
                                NumericGeometry.RoundToStep(originalAnchor.X, settings.SnapStep),
                                NumericGeometry.RoundToStep(originalAnchor.Y, settings.SnapStep));
                    }
                    finally
                    {
                        DisposeEntities(nearbyCurves);
                    }

                    foreach (BeautifySourceSnapshot source in snapshot.Sources)
                    {
                        Entity replacement = GeometryProcessor.CreateBeautifiedClone(
                            source.Entity,
                            originalAnchor,
                            targetAnchor,
                            settings.SnapStep,
                            settings.VertexTolerance,
                            out string rejectionReason);
                        if (replacement == null)
                        {
                            result.SkippedCount++;
                            result.AddWarning($"Bỏ qua {source.SourceId.Handle}: {rejectionReason}");
                            continue;
                        }

                        replacements.Add(new Replacement
                        {
                            SourceId = source.SourceId,
                            NewEntity = replacement
                        });
                    }

                    bool beautifyIncomplete = settings.StrictMode && replacements.Count != validIds.Count;
                    if (replacements.Count == 0 || beautifyIncomplete)
                    {
                        AbortWithoutChanges(
                            result,
                            editor,
                            beautifyIncomplete,
                            "Strict Mode: rollback vì không xử lý được toàn bộ selection.");
                        return;
                    }

                    WriteBeautifyResults(database, replacements, settings, result);
                    result.WriteSummary(editor);
                }
            }
            finally
            {
                DisposeReplacements(replacements);
            }
        }

        public static void ExecuteWallJoin(
            Document document,
            IReadOnlyList<ObjectId> selectedIds,
            bool isFinishing)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            Editor editor = document.Editor;
            Database database = document.Database;
            WallSettings settings = WallSettingsService.Current;
            ValidateSettings(settings);

            string commandName = isFinishing
                ? WallConstants.FinishWallCommandName
                : WallConstants.WallJoinCommandName;
            var result = new WallOperationResult(commandName)
            {
                SelectedCount = selectedIds.Count
            };
            List<ObjectId> validIds = ValidateObjectIds(database, selectedIds, result);
            if (validIds.Count == 0 ||
                (settings.StrictMode && validIds.Count != selectedIds.Count))
            {
                result.AddWarning("Không có tập ObjectId hợp lệ thuộc document hiện tại.");
                AbortWithoutChanges(result, editor, strictModeFailed: false, null);
                return;
            }

            CurveCollectionResult extracted = null;
            var pendingEntities = new List<Entity>();
            using (DocumentLock documentLock = document.LockDocument())
            {
                try
                {
                    extracted = ReadWallCurveSnapshot(database, validIds, result);
                    foreach (string warning in extracted.Warnings) result.AddWarning(warning);

                    bool extractionIncomplete = extracted.Sources.Any(source => source.ExtractionFailed);
                    bool strictExtractionRollback = settings.StrictMode && extractionIncomplete;
                    if (extracted.Curves.Count == 0 || strictExtractionRollback)
                    {
                        AbortWithoutChanges(
                            result,
                            editor,
                            strictExtractionRollback,
                            "Strict Mode: rollback vì có đầu vào không trích xuất được.");
                        return;
                    }

                    BuildWallEntitiesInMemory(
                        extracted,
                        pendingEntities,
                        settings,
                        isFinishing,
                        result,
                        out int failedClusters,
                        out int failedOffsets);

                    bool buildIncomplete = settings.StrictMode && (failedClusters > 0 || failedOffsets > 0);
                    if (pendingEntities.Count == 0 || buildIncomplete)
                    {
                        AbortWithoutChanges(
                            result,
                            editor,
                            buildIncomplete,
                            $"Strict Mode: rollback vì {failedClusters} cụm lỗi boundary và {failedOffsets} offset lỗi.");
                        return;
                    }

                    WriteWallEntities(database, extracted, pendingEntities, settings, isFinishing, result);
                    result.WriteSummary(editor);
                }
                finally
                {
                    extracted?.DisposeCurves();
                    DisposeEntities(pendingEntities);
                }
            }
        }

        private static BeautifyReadSnapshot ReadBeautifySnapshot(
            Database database,
            IReadOnlyList<ObjectId> sourceIds,
            WallOperationResult result)
        {
            var snapshot = new BeautifyReadSnapshot();
            try
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in sourceIds)
                    {
                        var entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (!(entity is Line) && !(entity is Polyline))
                        {
                            result.SkippedCount++;
                            result.AddWarning($"Bỏ qua {id.Handle}: chỉ hỗ trợ LINE và LWPOLYLINE.");
                            continue;
                        }
                        if (LayerService.IsEntityOnLockedLayer(entity, transaction))
                        {
                            result.SkippedCount++;
                            result.AddWarning($"Bỏ qua {id.Handle}: layer đang khóa.");
                            continue;
                        }

                        try
                        {
                            Extents3d entityExtents = entity.GeometricExtents;
                            if (!snapshot.HasExtents)
                            {
                                snapshot.Extents = entityExtents;
                                snapshot.HasExtents = true;
                            }
                            else
                            {
                                Extents3d combined = snapshot.Extents;
                                combined.AddExtents(entityExtents);
                                snapshot.Extents = combined;
                            }

                            Entity clone = entity.Clone() as Entity;
                            if (clone == null)
                            {
                                result.SkippedCount++;
                                result.AddWarning($"Không clone được đối tượng {id.Handle}.");
                                continue;
                            }
                            snapshot.Sources.Add(new BeautifySourceSnapshot
                            {
                                SourceId = id,
                                Entity = clone
                            });
                        }
                        catch (System.Exception ex)
                        {
                            result.SkippedCount++;
                            result.AddWarning($"Không snapshot được {id.Handle}: {ex.Message}");
                        }
                    }
                    transaction.Commit();
                }
                return snapshot;
            }
            catch (System.Exception)
            {
                snapshot.Dispose();
                throw;
            }
        }

        private static CurveCollectionResult ReadWallCurveSnapshot(
            Database database,
            IReadOnlyList<ObjectId> sourceIds,
            WallOperationResult result)
        {
            CurveCollectionResult extracted = null;
            try
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    extracted = GeometryProcessor.CollectCurves(
                        transaction,
                        sourceIds,
                        message => result.AddWarning("Geometry snapshot: " + message));
                    foreach (CurveSourceState source in extracted.Sources)
                    {
                        var entity = transaction.GetObject(source.SourceId, OpenMode.ForRead, false) as Entity;
                        if (entity != null)
                        {
                            source.IsOnLockedLayer = LayerService.IsEntityOnLockedLayer(entity, transaction);
                        }
                    }
                    transaction.Commit();
                    return extracted;
                }
            }
            catch (System.Exception)
            {
                extracted?.DisposeCurves();
                throw;
            }
        }

        private static List<Curve> ReadCurveClones(
            Database database,
            IReadOnlyList<ObjectId> ids,
            WallOperationResult result)
        {
            var curves = new List<Curve>();
            if (ids.Count == 0) return curves;

            try
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        try
                        {
                            var curve = transaction.GetObject(id, OpenMode.ForRead, false) as Curve;
                            Curve clone = curve?.Clone() as Curve;
                            if (clone != null) curves.Add(clone);
                        }
                        catch (System.Exception ex)
                        {
                            result.AddWarning($"Không snapshot được curve lân cận {id.Handle}: {ex.Message}");
                        }
                    }
                    transaction.Commit();
                }
                return curves;
            }
            catch (System.Exception)
            {
                DisposeEntities(curves);
                throw;
            }
        }

        private static void BuildWallEntitiesInMemory(
            CurveCollectionResult extracted,
            ICollection<Entity> pendingEntities,
            WallSettings settings,
            bool isFinishing,
            WallOperationResult result,
            out int failedClusters,
            out int failedOffsets)
        {
            Action<string> logger = message => result.AddWarning("Geometry: " + message);
            List<List<Curve>> clusters = GeometryProcessor.ClusterCurves(
                extracted.Curves,
                settings.GapTolerance,
                logger);

            failedClusters = 0;
            failedOffsets = 0;
            foreach (List<Curve> cluster in clusters)
            {
                BoundaryBuildResult boundaryResult = GeometryProcessor.ProcessClusterToBoundaries(
                    cluster,
                    settings.GapTolerance,
                    settings.VertexTolerance,
                    logger);
                try
                {
                    foreach (string warning in boundaryResult.Warnings) result.AddWarning(warning);

                    if (boundaryResult.Boundaries.Count == 0)
                    {
                        failedClusters++;
                        continue;
                    }

                    if (!isFinishing)
                    {
                        foreach (Polyline boundary in boundaryResult.Boundaries)
                        {
                            pendingEntities.Add(boundary);
                        }
                        boundaryResult.Boundaries.Clear();
                        continue;
                    }

                    foreach (Polyline boundary in boundaryResult.Boundaries)
                    {
                        List<Entity> offsets = GeometryProcessor.CreateFinishOffsets(
                            boundary,
                            settings.FinishOffset,
                            settings.FinishOffsetMode,
                            logger);
                        if (offsets.Count == 0)
                        {
                            failedOffsets++;
                            result.AddWarning("Một boundary không tạo được offset hoàn thiện.");
                        }
                        else
                        {
                            foreach (Entity offset in offsets) pendingEntities.Add(offset);
                        }
                    }
                }
                finally
                {
                    boundaryResult.DisposeBoundaries();
                }
            }
        }

        private static void WriteBeautifyResults(
            Database database,
            IReadOnlyList<Replacement> replacements,
            WallSettings settings,
            WallOperationResult result)
        {
            var approved = new List<Replacement>();
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (Replacement replacement in replacements)
                {
                    if (!IsUsableObjectId(database, replacement.SourceId))
                    {
                        result.AddWarning("Nguồn BW không còn hợp lệ trước khi ghi.");
                        continue;
                    }

                    var source = transaction.GetObject(replacement.SourceId, OpenMode.ForRead, false) as Entity;
                    if (source == null || LayerService.IsEntityOnLockedLayer(source, transaction))
                    {
                        result.AddWarning($"Không thể thay thế {replacement.SourceId.Handle}: layer khóa hoặc entity không tồn tại.");
                        continue;
                    }
                    approved.Add(replacement);
                }

                if (settings.StrictMode && approved.Count != replacements.Count)
                {
                    result.AddWarning("Strict Mode: rollback vì nguồn BW thay đổi trước khi ghi.");
                    result.SkippedCount = result.SelectedCount;
                    return;
                }

                WallDatabaseWriter.AppendToCurrentSpace(
                    database,
                    transaction,
                    approved.Select(item => item.NewEntity));
                foreach (Replacement replacement in approved)
                {
                    var source = (Entity)transaction.GetObject(
                        replacement.SourceId,
                        OpenMode.ForWrite,
                        false);
                    source.Erase();
                }

                transaction.Commit();
                result.ProcessedCount = approved.Count;
                result.CreatedCount = approved.Count;
                result.ErasedCount = approved.Count;
                result.SkippedCount = result.SelectedCount - approved.Count;
                result.Committed = true;
            }
        }

        private static void WriteWallEntities(
            Database database,
            CurveCollectionResult extracted,
            IReadOnlyList<Entity> pendingEntities,
            WallSettings settings,
            bool isFinishing,
            WallOperationResult result)
        {
            string targetLayer = isFinishing ? settings.FinishLayer : settings.WallLayer;
            short targetColor = isFinishing ? WallConstants.FinishColor : WallConstants.WallColor;
            LineWeight targetWeight = isFinishing ? WallConstants.FinishWeight : WallConstants.WallWeight;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                LayerService.EnsureLayerExists(
                    database,
                    transaction,
                    targetLayer,
                    targetColor,
                    targetWeight);

                foreach (Entity entity in pendingEntities)
                {
                    entity.Layer = targetLayer;
                    entity.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                }

                var sourcesToErase = new List<Entity>();
                int expectedEraseCount = 0;
                if (!isFinishing && !settings.KeepOriginals)
                {
                    foreach (CurveSourceState state in extracted.Sources)
                    {
                        if (!state.CanEraseSource)
                        {
                            string reason = state.IsOnLockedLayer
                                ? "layer đang khóa"
                                : "block có nội dung không hỗ trợ hoặc snapshot chưa đầy đủ";
                            result.AddWarning($"Giữ lại {state.SourceId.Handle}: {reason}.");
                            continue;
                        }
                        expectedEraseCount++;

                        if (!IsUsableObjectId(database, state.SourceId))
                        {
                            result.AddWarning($"Giữ lại nguồn {state.SourceId.Handle}: ObjectId không còn hợp lệ.");
                            continue;
                        }

                        var source = transaction.GetObject(state.SourceId, OpenMode.ForRead, false) as Entity;
                        if (source == null || LayerService.IsEntityOnLockedLayer(source, transaction))
                        {
                            result.AddWarning($"Giữ lại {state.SourceId.Handle}: entity không tồn tại hoặc layer đang khóa.");
                            continue;
                        }
                        sourcesToErase.Add(source);
                    }
                }

                if (settings.StrictMode && sourcesToErase.Count != expectedEraseCount)
                {
                    result.AddWarning("Strict Mode: rollback vì nguồn WJ thay đổi trước khi ghi.");
                    result.SkippedCount = result.SelectedCount;
                    return;
                }

                int createdCount = WallDatabaseWriter.AppendToCurrentSpace(
                    database,
                    transaction,
                    pendingEntities);
                foreach (Entity source in sourcesToErase)
                {
                    source.UpgradeOpen();
                    source.Erase();
                }

                transaction.Commit();
                result.ProcessedCount = extracted.Sources.Count(source => !source.ExtractionFailed);
                result.CreatedCount = createdCount;
                result.ErasedCount = sourcesToErase.Count;
                result.SkippedCount = result.SelectedCount - result.ProcessedCount;
                result.Committed = true;
            }
        }

        private static IReadOnlyList<ObjectId> CollectNearbyObjectIds(
            Editor editor,
            Extents3d worldExtents,
            double radius,
            ICollection<ObjectId> ignoredIds)
        {
            if (radius <= 0) return Array.Empty<ObjectId>();

            Matrix3d worldToUcs = editor.CurrentUserCoordinateSystem.Inverse();
            Point3d[] worldCorners =
            {
                new Point3d(worldExtents.MinPoint.X - radius, worldExtents.MinPoint.Y - radius, 0),
                new Point3d(worldExtents.MinPoint.X - radius, worldExtents.MaxPoint.Y + radius, 0),
                new Point3d(worldExtents.MaxPoint.X + radius, worldExtents.MinPoint.Y - radius, 0),
                new Point3d(worldExtents.MaxPoint.X + radius, worldExtents.MaxPoint.Y + radius, 0)
            };
            Point3d[] ucsCorners = worldCorners
                .Select(point => point.TransformBy(worldToUcs))
                .ToArray();
            Point3d first = new Point3d(
                ucsCorners.Min(point => point.X),
                ucsCorners.Min(point => point.Y),
                0);
            Point3d second = new Point3d(
                ucsCorners.Max(point => point.X),
                ucsCorners.Max(point => point.Y),
                0);

            var ignored = new HashSet<ObjectId>(ignoredIds);
            return WallInteraction
                .SelectCrossingWindow(editor, first, second, NearbyCurveDxfTypes)
                .Where(id => !ignored.Contains(id))
                .ToArray();
        }

        private static List<ObjectId> ValidateObjectIds(
            Database database,
            IEnumerable<ObjectId> sourceIds,
            WallOperationResult result)
        {
            var valid = new List<ObjectId>();
            foreach (ObjectId id in sourceIds.Distinct())
            {
                if (IsUsableObjectId(database, id))
                {
                    valid.Add(id);
                }
                else
                {
                    result.AddWarning("Bỏ qua ObjectId null, erased hoặc thuộc database khác.");
                }
            }
            return valid;
        }

        private static bool IsUsableObjectId(Database database, ObjectId id)
        {
            return id != ObjectId.Null &&
                   id.IsValid &&
                   !id.IsErased &&
                   id.Database == database;
        }

        private static void AbortWithoutChanges(
            WallOperationResult result,
            Editor editor,
            bool strictModeFailed,
            string strictModeMessage)
        {
            if (strictModeFailed)
            {
                result.AddWarning(strictModeMessage);
            }

            result.SkippedCount = result.SelectedCount;
            result.WriteSummary(editor);
        }

        private static void DisposeReplacements(IEnumerable<Replacement> replacements)
        {
            foreach (Replacement replacement in replacements)
            {
                Entity entity = replacement.NewEntity;
                if (entity != null && !entity.IsDisposed && entity.Database == null)
                {
                    entity.Dispose();
                }
            }
        }

        private static void DisposeEntities<T>(IEnumerable<T> entities)
            where T : Entity
        {
            foreach (T entity in entities)
            {
                if (entity != null && !entity.IsDisposed && entity.Database == null)
                {
                    entity.Dispose();
                }
            }
        }

        private static void ValidateSettings(WallSettings settings)
        {
            IReadOnlyList<string> errors = settings.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            SymbolUtilityServices.ValidateSymbolName(settings.WallLayer, false);
            SymbolUtilityServices.ValidateSymbolName(settings.FinishLayer, false);
        }
    }
}
