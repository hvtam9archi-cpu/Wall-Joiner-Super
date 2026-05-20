using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ProWallTools
{
    public partial class WallJoinWindow : Window
    {
        public WallJoinWindow()
        {
            InitializeComponent();

            // Chặn gọi API AutoCAD khi đang hiển thị trong Designer của Visual Studio
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }

            // Khởi tạo các giá trị cấu hình mặc định từ hằng số
            txtGap.Text = WallConstants.GapTolerance.ToString();
            txtVertex.Text = WallConstants.VertexTolerance.ToString();

            // Load danh sách Layer hiện có trong bản vẽ
            LoadLayers();
        }

        private void LoadLayers()
        {
            List<string> layerNames = new List<string>();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                try
                {
                    using (DocumentLock docLock = doc.LockDocument())
                    {
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            LayerTable lt = tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead) as LayerTable;
                            if (lt != null)
                            {
                                foreach (ObjectId id in lt)
                                {
                                    LayerTableRecord ltr = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                                    if (ltr != null)
                                    {
                                        layerNames.Add(ltr.Name);
                                    }
                                }
                            }
                            tr.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi nạp Layers: " + ex.Message);
                }
            }

            cmbWallLayer.ItemsSource = layerNames;
            cmbFinishLayer.ItemsSource = layerNames;

            // Chọn các layer mặc định
            cmbWallLayer.Text = WallConstants.CurrentWallLayer;
            cmbFinishLayer.Text = WallConstants.CurrentFinishLayer;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private bool SaveSettings()
        {
            if (!double.TryParse(txtGap.Text, out double gap) || gap < 0)
            {
                MessageBox.Show(this, "Gap Tolerance không hợp lệ. Vui lòng nhập số dương.", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!double.TryParse(txtVertex.Text, out double vertex) || vertex < 0)
            {
                MessageBox.Show(this, "Vertex Tolerance không hợp lệ. Vui lòng nhập số dương.", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            WallConstants.GapTolerance = gap;
            WallConstants.VertexTolerance = vertex;
            WallConstants.CurrentWallLayer = cmbWallLayer.Text;
            WallConstants.CurrentFinishLayer = cmbFinishLayer.Text;

            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (SaveSettings())
            {
                this.Close();
            }
        }
    }
}
