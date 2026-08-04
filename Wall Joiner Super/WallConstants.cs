using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public static class WallConstants
    {
        public const string BeautifyCommandName = "BW";
        public const string WallJoinCommandName = "WJ";
        public const string FinishWallCommandName = "FW";
        public const string SettingsCommandName = "WJ_UI";

        public const string RibbonTabId = "TH_TOOLS_TAB";
        public const string RibbonTabTitle = "TH Tools";
        public const string RibbonPanelId = "TH_TOOLS_WALL_JOINER_PANEL";
        public const string RibbonPanelTitle = "Wall Joiner";
        public const string RibbonWallJoinButtonId = "TH_TOOLS_WJ_BUTTON";
        public const string RibbonFinishButtonId = "TH_TOOLS_FW_BUTTON";
        public const string RibbonBeautifyButtonId = "TH_TOOLS_BW_BUTTON";
        public const string RibbonSettingsButtonId = "TH_TOOLS_WJ_SETTINGS_BUTTON";

        public const string SettingsIconResource = "IconRibbon_Settings_32px.png";
        public const string WallJoinIconResource = "IconRibbon_Wall-Joiner_32px.png";
        public const string FinishIconResource = "IconRibbon_Wall-Finisher_32px.png";
        public const string BeautifyIconResource = "IconRibbon_Beautify_32px.png";

        public const short WallColor = 4; // Cyan
        public static readonly LineWeight WallWeight = LineWeight.LineWeight025;

        public const short FinishColor = 9;
        public static readonly LineWeight FinishWeight = LineWeight.LineWeight009;
    }
}
