using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

[assembly: ExtensionApplication(typeof(ProWallTools.RibbonSetup))]

namespace ProWallTools
{
    public class RibbonSetup : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                // Đăng ký sự kiện Idle để chờ ComponentManager khởi tạo Ribbon xong
                Application.Idle += OnApplicationIdle;
                // Lắng nghe biến hệ thống thay đổi để render lại Ribbon khi chuyển Workspace
                Application.SystemVariableChanged += OnSystemVariableChanged;

                // Khởi tạo hệ thống markers
                WallMarkers.Initialize();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi khởi tạo Plugin: " + ex.Message);
            }
        }

        public void Terminate()
        {
            try
            {
                Application.Idle -= OnApplicationIdle;
                Application.SystemVariableChanged -= OnSystemVariableChanged;

                // Dọn dẹp markers
                WallMarkers.Terminate();
            }
            catch
            {
                // Bỏ qua lỗi trong quá trình Terminate
            }
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            // Hủy sự kiện ngay lập tức để chỉ chạy một lần lúc khởi động
            Application.Idle -= OnApplicationIdle;
            CreateRibbon();
        }

        private void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
        {
            // Kiểm tra sự thay đổi của workspace
            if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase))
            {
                CreateRibbon();
            }
        }

        private void CreateRibbon()
        {
            try
            {
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;

                string tabTitle = "TH Tools";
                string panelTitle = "Wall Joiner";
                RibbonTab tab = null;

                // Tìm xem tab "TH Tools" đã tồn tại chưa
                foreach (RibbonTab t in ribbon.Tabs)
                {
                    if (t.Title.Equals(tabTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        tab = t;
                        break;
                    }
                }

                // Nếu chưa tồn tại, tạo mới
                if (tab == null)
                {
                    tab = new RibbonTab
                    {
                        Title = tabTitle,
                        Id = "TH_TOOLS_TAB"
                    };
                    ribbon.Tabs.Add(tab);
                    // Bắt buộc gọi tab.IsActive = true ngay sau khi ribbon.Tabs.Add(tab) để ép Tab hiển thị
                    tab.IsActive = true;
                }
                else
                {
                    tab.IsActive = true;
                }

                // Xóa panel cũ của Wall Joiner nếu đã tồn tại trong tab (khi chuyển Workspace vẽ lại)
                RibbonPanel existingPanel = null;
                foreach (RibbonPanel p in tab.Panels)
                {
                    if (p.Source != null && p.Source.Title.Equals(panelTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        existingPanel = p;
                        break;
                    }
                }
                if (existingPanel != null)
                {
                    tab.Panels.Remove(existingPanel);
                }

                // Tạo Panel
                RibbonPanelSource panelSource = new RibbonPanelSource
                {
                    Title = panelTitle
                };
                RibbonPanel panel = new RibbonPanel
                {
                    Source = panelSource
                };
                tab.Panels.Add(panel);

                // Nút 1: Mở giao diện Cài đặt (WJ_UI)
                RibbonButton btnUI = new RibbonButton
                {
                    Text = "Wall Joiner\nSettings",
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    CommandHandler = new RibbonCommandHandler("WJ_UI"),
                    LargeImage = GetImage("pack://application:,,,/WallJoinerSuper;component/Resources/settings.png")
                };

                // Nút 2: Thực thi nhanh WJ
                RibbonButton btnWJ = new RibbonButton
                {
                    Text = "Join Walls (WJ)",
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Standard,
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    CommandHandler = new RibbonCommandHandler("WJ"),
                    Image = GetImage("pack://application:,,,/WallJoinerSuper;component/Resources/join.png")
                };

                // Nút 3: Thực thi nhanh FW
                RibbonButton btnFW = new RibbonButton
                {
                    Text = "Finish Wall (FW)",
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Standard,
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    CommandHandler = new RibbonCommandHandler("FW"),
                    Image = GetImage("pack://application:,,,/WallJoinerSuper;component/Resources/finish.png")
                };

                // Nút 4: Thực thi nhanh BW
                RibbonButton btnBW = new RibbonButton
                {
                    Text = "Beautify Walls (BW)",
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Standard,
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    CommandHandler = new RibbonCommandHandler("BW"),
                    Image = GetImage("pack://application:,,,/WallJoinerSuper;component/Resources/join.png")
                };

                panelSource.Items.Add(btnUI);
                panelSource.Items.Add(new RibbonSeparator());

                // Stack panel để xếp 3 nút nhỏ nằm cạnh nhau theo chiều dọc
                RibbonRowPanel rowPanel = new RibbonRowPanel();
                rowPanel.Items.Add(btnWJ);
                rowPanel.Items.Add(new RibbonRowBreak());
                rowPanel.Items.Add(btnFW);
                rowPanel.Items.Add(new RibbonRowBreak());
                rowPanel.Items.Add(btnBW);
                panelSource.Items.Add(rowPanel);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi tạo Ribbon: " + ex.Message);
            }
        }

        private static System.Windows.Media.ImageSource GetImage(string uriString)
        {
            try
            {
                return new System.Windows.Media.Imaging.BitmapImage(new Uri(uriString));
            }
            catch
            {
                return null;
            }
        }
    }

    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        private readonly string _command;

        public RibbonCommandHandler(string command)
        {
            _command = command;
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.SendStringToExecute(_command + " ", true, false, false);
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
