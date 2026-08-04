using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public static class LayerService
    {
        public static IReadOnlyList<string> GetLayerNames(Database database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));

            var names = new List<string>();
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)transaction.GetObject(
                    database.LayerTableId,
                    OpenMode.ForRead,
                    false);
                foreach (ObjectId id in layerTable)
                {
                    var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead, false);
                    names.Add(layer.Name);
                }
                transaction.Commit();
            }

            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        public static void EnsureLayerExists(
            Database database,
            Transaction transaction,
            string name,
            short color,
            LineWeight weight)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tên layer không được để trống.", nameof(name));
            }

            SymbolUtilityServices.ValidateSymbolName(name, false);
            var layerTable = (LayerTable)transaction.GetObject(
                database.LayerTableId,
                OpenMode.ForRead,
                false);
            if (layerTable.Has(name)) return;

            layerTable.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, color),
                LineWeight = weight
            };
            layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
        }

        public static bool IsEntityOnLockedLayer(Entity entity, Transaction transaction)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            var layer = transaction.GetObject(
                entity.LayerId,
                OpenMode.ForRead,
                false) as LayerTableRecord;
            return layer == null || layer.IsLocked;
        }
    }
}
