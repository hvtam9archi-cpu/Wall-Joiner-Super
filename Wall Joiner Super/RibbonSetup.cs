using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

[assembly: ExtensionApplication(typeof(ProWallTools.RibbonSetup))]

namespace ProWallTools
{
    public sealed class RibbonSetup : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                Application.Idle += OnApplicationIdle;
                Application.SystemVariableChanged += OnSystemVariableChanged;

                foreach (string warning in WallSettingsService.Load())
                {
                    Debug.WriteLine("[Wall Joiner Settings] " + warning);
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("[Wall Joiner Initialize]" + Environment.NewLine + ex);
            }
        }

        public void Terminate()
        {
            try
            {
                Application.Idle -= OnApplicationIdle;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("[Wall Joiner Terminate Idle]" + Environment.NewLine + ex);
            }

            try
            {
                Application.SystemVariableChanged -= OnSystemVariableChanged;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("[Wall Joiner Terminate SystemVariableChanged]" + Environment.NewLine + ex);
            }
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            try
            {
                if (EnsureRibbon())
                {
                    Application.Idle -= OnApplicationIdle;
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("[Wall Joiner Ribbon Idle]" + Environment.NewLine + ex);
            }
        }

        private void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
        {
            try
            {
                if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!EnsureRibbon())
                    {
                        Application.Idle -= OnApplicationIdle;
                        Application.Idle += OnApplicationIdle;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("[Wall Joiner Workspace Changed]" + Environment.NewLine + ex);
            }
        }

        private static bool EnsureRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;

            RibbonTab tab = ribbon.Tabs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, WallConstants.RibbonTabId, StringComparison.OrdinalIgnoreCase));
            if (tab == null)
            {
                tab = ribbon.Tabs.FirstOrDefault(candidate =>
                    string.Equals(candidate.Title, WallConstants.RibbonTabTitle, StringComparison.OrdinalIgnoreCase));
            }

            if (tab == null)
            {
                tab = new RibbonTab
                {
                    Id = WallConstants.RibbonTabId,
                    Title = WallConstants.RibbonTabTitle
                };
                ribbon.Tabs.Add(tab);
                tab.IsActive = true;
            }

            RibbonPanel panel = tab.Panels.FirstOrDefault(candidate =>
                candidate.Source != null &&
                string.Equals(candidate.Source.Id, WallConstants.RibbonPanelId, StringComparison.OrdinalIgnoreCase));
            if (panel == null)
            {
                panel = tab.Panels.FirstOrDefault(candidate =>
                    candidate.Source != null &&
                    string.Equals(candidate.Source.Title, WallConstants.RibbonPanelTitle, StringComparison.OrdinalIgnoreCase));
            }

            if (panel == null)
            {
                panel = new RibbonPanel
                {
                    Source = new RibbonPanelSource
                    {
                        Id = WallConstants.RibbonPanelId,
                        Title = WallConstants.RibbonPanelTitle
                    }
                };
                tab.Panels.Add(panel);
            }

            EnsureButton(
                panel.Source,
                WallConstants.RibbonWallJoinButtonId,
                "Join Walls\n(WJ)",
                WallConstants.WallJoinCommandName,
                WallConstants.WallJoinIconResource);
            EnsureButton(
                panel.Source,
                WallConstants.RibbonFinishButtonId,
                "Wall Finisher\n(FW)",
                WallConstants.FinishWallCommandName,
                WallConstants.FinishIconResource);
            EnsureButton(
                panel.Source,
                WallConstants.RibbonBeautifyButtonId,
                "Beautify\n(BW)",
                WallConstants.BeautifyCommandName,
                WallConstants.BeautifyIconResource);
            EnsureButton(
                panel.Source,
                WallConstants.RibbonSettingsButtonId,
                "Settings",
                WallConstants.SettingsCommandName,
                WallConstants.SettingsIconResource);
            return true;
        }

        private static void EnsureButton(
            RibbonPanelSource panelSource,
            string buttonId,
            string text,
            string commandName,
            string iconResource)
        {
            RibbonButton button = panelSource.Items
                .OfType<RibbonButton>()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, buttonId, StringComparison.OrdinalIgnoreCase));
            if (button == null)
            {
                button = new RibbonButton { Id = buttonId };
                panelSource.Items.Add(button);
            }

            ImageSource icon = LoadRibbonIcon(iconResource);
            button.Text = text;
            button.ShowText = true;
            button.ShowImage = true;
            button.Size = RibbonItemSize.Large;
            button.Orientation = Orientation.Vertical;
            button.CommandHandler = new RibbonCommandHandler(commandName);
            button.LargeImage = SizeRibbonIcon(icon, 32);
            button.Image = SizeRibbonIcon(icon, 16);
        }

        private static ImageSource SizeRibbonIcon(ImageSource image, double size)
        {
            // Explicit WPF bounds prevent PNG DPI metadata from enlarging the ribbon icon.
            var drawing = new ImageDrawing(image, new System.Windows.Rect(0, 0, size, size));
            var sizedImage = new DrawingImage(drawing);
            sizedImage.Freeze();
            return sizedImage;
        }

        private static ImageSource LoadRibbonIcon(string resourceName)
        {
            var uri = new Uri(
                "pack://application:,,,/WallJoinerSuper;component/Resources/" + resourceName,
                UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = uri;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    public sealed class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        private readonly string commandName;

        public RibbonCommandHandler(string commandName)
        {
            this.commandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;

                // Ribbon ICommand cannot invoke an AutoCAD CommandMethod through the managed API.
                document.SendStringToExecute(commandName + " ", true, false, false);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"[Wall Joiner Ribbon Command: {commandName}]{Environment.NewLine}{ex}");
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
