using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace ProWallTools
{
    public class WallMarkers : IDisposable
    {
        private static readonly List<Drawable> _markers = new List<Drawable>();

        public static void AddMarker(Drawable drawable)
        {
            if (drawable == null) return;

            _markers.Add(drawable);
            try
            {
                TransientManager.CurrentTransientManager.AddTransient(
                    drawable,
                    TransientDrawingMode.DirectShortTerm,
                    128,
                    new IntegerCollection()
                );
            }
            catch (System.Exception)
            {
                // Tránh crash nếu AutoCAD ở trạng thái không vẽ được transient
            }
        }

        public static void Clear()
        {
            foreach (var drawable in _markers)
            {
                try
                {
                    TransientManager.CurrentTransientManager.EraseTransient(
                        drawable,
                        new IntegerCollection()
                    );
                    drawable.Dispose();
                }
                catch
                {
                    // Bỏ qua lỗi dọn dẹp khi đóng AutoCAD để tránh crash
                }
            }
            _markers.Clear();
        }

        public static void Initialize()
        {
            Application.DocumentManager.DocumentDestroyed += OnDocumentDestroyed;
        }

        public static void Terminate()
        {
            Application.DocumentManager.DocumentDestroyed -= OnDocumentDestroyed;
            Clear();
        }

        private static void OnDocumentDestroyed(object sender, DocumentDestroyedEventArgs e)
        {
            Clear();
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
