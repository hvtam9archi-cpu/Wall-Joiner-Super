using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(ProWallTools.Commands))]

namespace ProWallTools
{
    public class Commands
    {
        [CommandMethod("BW", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void BeautifyWallsCommand()
        {
            try
            {
                WallJoinLogic.ExecuteBeautifyWalls();
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[LỖI] " + ex.Message);
            }
        }

        [CommandMethod("WJ", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void WallJoinCommand()
        {
            try
            {
                WallJoinLogic.ExecuteWallJoin(isFinishing: false);
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[LỖI] " + ex.Message);
            }
        }

        [CommandMethod("FW", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void FinishingWallCommand()
        {
            try
            {
                WallJoinLogic.ExecuteWallJoin(isFinishing: true);
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[LỖI] " + ex.Message);
            }
        }

        [CommandMethod("WJ_UI", CommandFlags.Modal)]
        public void ShowWallJoinUICommand()
        {
            try
            {
                var window = new WallJoinWindow();
                Application.ShowModalWindow(window);
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[LỖI] Không thể hiển thị giao diện: " + ex.Message);
            }
        }
    }
}
