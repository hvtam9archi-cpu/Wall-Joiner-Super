using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public static class WallConstants
    {
        // Dung sai hình học
        public const double GapTolerance = 10.0;
        public const double VertexTolerance = 1e-4;
        public const double DefaultOffset = 15.0;

        // Cấu hình Layer mặc định
        public static string CurrentWallLayer = "ABC_A_Nettuong";
        public static string CurrentFinishLayer = "ABC_A_Netmanh";

        // Định dạng màu sắc và nét vẽ
        public const short WallColor = 4; // Cyan
        public static readonly LineWeight WallWeight = LineWeight.LineWeight025;

        public const short FinishColor = 9; // Màu số 9
        public static readonly LineWeight FinishWeight = LineWeight.LineWeight009;
    }
}