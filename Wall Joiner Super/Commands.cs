using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(ProWallTools.Commands))]

namespace ProWallTools
{
    public class Commands
    {
        [CommandMethod(WallConstants.BeautifyCommandName, CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void BeautifyWallsCommand()
        {
            ExecuteInActiveDocument(WallConstants.BeautifyCommandName, document =>
            {
                WallSelectionResult selection = WallInteraction.GetSelection(
                    document.Editor,
                    "\nChọn LINE hoặc LWPOLYLINE cần làm đẹp: ",
                    "LINE,LWPOLYLINE");
                if (!CanContinue(selection, document)) return;

                WallJoinWorkflow.ExecuteBeautifyWalls(document, selection.ObjectIds);
            });
        }

        [CommandMethod(WallConstants.WallJoinCommandName, CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void WallJoinCommand()
        {
            ExecuteWallJoinCommand(isFinishing: false);
        }

        [CommandMethod(WallConstants.FinishWallCommandName, CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void FinishingWallCommand()
        {
            ExecuteWallJoinCommand(isFinishing: true);
        }

        [CommandMethod(WallConstants.SettingsCommandName, CommandFlags.Modal)]
        public void ShowWallJoinUICommand()
        {
            ExecuteInActiveDocument(WallConstants.SettingsCommandName, document =>
            {
                IReadOnlyList<string> layerNames;
                using (DocumentLock documentLock = document.LockDocument())
                {
                    layerNames = LayerService.GetLayerNames(document.Database);
                }

                var window = new WallJoinWindow(layerNames, WallSettingsService.Current);
                Application.ShowModalWindow(window);
                if (window.DialogResult == true && window.SavedSettings != null)
                {
                    WallSettingsService.Save(window.SavedSettings);
                }
            });
        }

        private static void ExecuteWallJoinCommand(bool isFinishing)
        {
            string commandName = isFinishing
                ? WallConstants.FinishWallCommandName
                : WallConstants.WallJoinCommandName;
            ExecuteInActiveDocument(commandName, document =>
            {
                WallSelectionResult selection = WallInteraction.GetSelection(
                    document.Editor,
                    "\nChọn LINE, POLYLINE hoặc block đường bao tường: ",
                    "LINE,LWPOLYLINE,POLYLINE,INSERT");
                if (!CanContinue(selection, document)) return;

                if (isFinishing)
                {
                    WallJoinWorkflow.ExecuteWallJoin(document, selection.ObjectIds, isFinishing: true);
                }
                else
                {
                    WallJoinSourcePolicy.ExecuteReplacingSources(document, selection.ObjectIds);
                }
            });
        }

        private static bool CanContinue(WallSelectionResult selection, Document document)
        {
            if (selection.Outcome == WallPromptOutcome.Cancelled) return false;
            if (selection.Outcome == WallPromptOutcome.Error)
            {
                document.Editor.WriteMessage("\n[Wall Joiner] Không thể đọc selection.");
                return false;
            }
            if (selection.ObjectIds.Count > 0) return true;

            document.Editor.WriteMessage("\n[Wall Joiner] Selection không chứa đối tượng hợp lệ.");
            return false;
        }

        private static void ExecuteInActiveDocument(string commandName, Action<Document> action)
        {
            Document document = null;
            try
            {
                document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;
                action(document);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ReportError(commandName, document, ex);
            }
            catch (System.Exception ex)
            {
                ReportError(commandName, document, ex);
            }
        }

        private static void ReportError(string commandName, Document document, System.Exception exception)
        {
            if (document == null)
            {
                Debug.WriteLine($"[{commandName}] No active document{Environment.NewLine}{exception}");
                return;
            }

            string documentName;
            try
            {
                documentName = document.Name;
            }
            catch (System.Exception nameException)
            {
                documentName = "<unavailable: " + nameException.Message + ">";
            }

            Debug.WriteLine($"[{commandName}] Document='{documentName}'{Environment.NewLine}{exception}");
            try
            {
                document.Editor.WriteMessage($"\n[{commandName}] Lỗi: {exception.Message}");
            }
            catch (System.Exception writeException)
            {
                Debug.WriteLine($"[{commandName}] Cannot write command error{Environment.NewLine}{writeException}");
            }
        }
    }
}
