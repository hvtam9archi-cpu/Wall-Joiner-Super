using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ProWallTools
{
    public enum WallPromptOutcome
    {
        Ok,
        Cancelled,
        Error
    }

    public sealed class WallSelectionResult
    {
        public WallSelectionResult(WallPromptOutcome outcome, IReadOnlyList<ObjectId> objectIds)
        {
            Outcome = outcome;
            ObjectIds = objectIds ?? Array.Empty<ObjectId>();
        }

        public WallPromptOutcome Outcome { get; }
        public IReadOnlyList<ObjectId> ObjectIds { get; }
    }

    public static class WallInteraction
    {
        public static WallSelectionResult GetSelection(
            Editor editor,
            string message,
            string allowedDxfTypes)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Prompt message is required.", nameof(message));
            if (string.IsNullOrWhiteSpace(allowedDxfTypes)) throw new ArgumentException("DXF filter is required.", nameof(allowedDxfTypes));

            var options = new PromptSelectionOptions { MessageForAdding = message };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, allowedDxfTypes)
            });
            PromptSelectionResult promptResult = editor.GetSelection(options, filter);

            if (promptResult.Status == PromptStatus.OK && promptResult.Value != null)
            {
                ObjectId[] ids = promptResult.Value
                    .GetObjectIds()
                    .Where(id => id != ObjectId.Null && id.IsValid && !id.IsErased)
                    .Distinct()
                    .ToArray();
                return new WallSelectionResult(WallPromptOutcome.Ok, ids);
            }

            if (promptResult.Status == PromptStatus.Cancel || promptResult.Status == PromptStatus.None)
            {
                return new WallSelectionResult(WallPromptOutcome.Cancelled, Array.Empty<ObjectId>());
            }

            return new WallSelectionResult(WallPromptOutcome.Error, Array.Empty<ObjectId>());
        }

        public static IReadOnlyList<ObjectId> SelectCrossingWindow(
            Editor editor,
            Point3d firstCorner,
            Point3d secondCorner,
            string allowedDxfTypes)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));

            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, allowedDxfTypes)
            });
            PromptSelectionResult result = editor.SelectCrossingWindow(firstCorner, secondCorner, filter);
            if (result.Status != PromptStatus.OK || result.Value == null)
            {
                return Array.Empty<ObjectId>();
            }

            return result.Value
                .GetObjectIds()
                .Where(id => id != ObjectId.Null && id.IsValid && !id.IsErased)
                .Distinct()
                .ToArray();
        }
    }
}
