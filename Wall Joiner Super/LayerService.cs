using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public static class LayerService
    {
        public static void EnsureLayersExist(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            CreateLayer(tr, lt, WallConstants.CurrentWallLayer, WallConstants.WallColor, WallConstants.WallWeight);
            CreateLayer(tr, lt, WallConstants.CurrentFinishLayer, WallConstants.FinishColor, WallConstants.FinishWeight);
        }

        private static void CreateLayer(Transaction tr, LayerTable lt, string name, short color, LineWeight weight)
        {
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, color),
                    LineWeight = weight
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}