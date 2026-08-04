using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public static class WallDatabaseWriter
    {
        public static int AppendToCurrentSpace(
            Database database,
            Transaction transaction,
            IEnumerable<Entity> entities)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            var currentSpace = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId,
                OpenMode.ForWrite,
                false);
            int count = 0;
            foreach (Entity entity in entities)
            {
                if (entity == null) continue;
                if (entity.Database != null)
                {
                    throw new InvalidOperationException(
                        "Only in-memory entities that are not database-resident can be appended.");
                }
                currentSpace.AppendEntity(entity);
                transaction.AddNewlyCreatedDBObject(entity, true);
                count++;
            }
            return count;
        }
    }
}
