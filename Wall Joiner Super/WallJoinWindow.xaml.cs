using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;

namespace ProWallTools
{
    public partial class WallJoinWindow : Window
    {
        public WallJoinWindow()
            : this(Array.Empty<string>(), new WallSettings())
        {
        }

        public WallJoinWindow(IReadOnlyList<string> layerNames, WallSettings settings)
        {
            InitializeComponent();
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) return;

            if (layerNames == null) throw new ArgumentNullException(nameof(layerNames));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            // WJ now always replaces source geometry with the joined result.
            chkKeepOriginals.Visibility = Visibility.Collapsed;

            cmbWallLayer.ItemsSource = layerNames;
            cmbFinishLayer.ItemsSource = layerNames;
            cmbOffsetMode.ItemsSource = Enum.GetValues(typeof(FinishOffsetMode));
            LoadSettings(settings);
        }

        public WallSettings SavedSettings { get; private set; }

        private void LoadSettings(WallSettings settings)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            txtGap.Text = settings.GapTolerance.ToString("0.####", culture);
            txtVertex.Text = settings.VertexTolerance.ToString("0.########", culture);
            txtFinishOffset.Text = settings.FinishOffset.ToString("0.####", culture);
            txtSnapRadius.Text = settings.SnapRadius.ToString("0.####", culture);
            txtSnapStep.Text = settings.SnapStep.ToString("0.####", culture);
            cmbWallLayer.Text = settings.WallLayer;
            cmbFinishLayer.Text = settings.FinishLayer;
            cmbOffsetMode.SelectedItem = settings.FinishOffsetMode;
            chkKeepOriginals.IsChecked = false;
            chkStrictMode.IsChecked = settings.StrictMode;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool TryBuildSettings(out WallSettings settings)
        {
            settings = null;
            if (!TryParseNumber(txtGap.Text, out double gap) ||
                !TryParseNumber(txtVertex.Text, out double vertex) ||
                !TryParseNumber(txtFinishOffset.Text, out double finishOffset) ||
                !TryParseNumber(txtSnapRadius.Text, out double snapRadius) ||
                !TryParseNumber(txtSnapStep.Text, out double snapStep))
            {
                ShowValidationError("Các trường dung sai, offset và bước lưới phải là số hợp lệ.");
                return false;
            }

            var candidate = new WallSettings
            {
                GapTolerance = gap,
                VertexTolerance = vertex,
                FinishOffset = finishOffset,
                SnapRadius = snapRadius,
                SnapStep = snapStep,
                WallLayer = (cmbWallLayer.Text ?? string.Empty).Trim(),
                FinishLayer = (cmbFinishLayer.Text ?? string.Empty).Trim(),
                FinishOffsetMode = cmbOffsetMode.SelectedItem is FinishOffsetMode mode
                    ? mode
                    : FinishOffsetMode.Outside,
                KeepOriginals = false,
                StrictMode = chkStrictMode.IsChecked == true
            };

            IReadOnlyList<string> errors = candidate.Validate();
            if (errors.Count > 0)
            {
                ShowValidationError(string.Join(Environment.NewLine, errors));
                return false;
            }

            try
            {
                SymbolUtilityServices.ValidateSymbolName(candidate.WallLayer, false);
                SymbolUtilityServices.ValidateSymbolName(candidate.FinishLayer, false);
            }
            catch (Exception ex)
            {
                ShowValidationError("Tên layer không hợp lệ: " + ex.Message);
                return false;
            }

            settings = candidate;
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildSettings(out WallSettings settings)) return;

            SavedSettings = settings;
            DialogResult = true;
            Close();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings(new WallSettings());
        }

        private static bool TryParseNumber(string text, out double value)
        {
            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            return double.TryParse(text, styles, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        private void ShowValidationError(string message)
        {
            MessageBox.Show(
                this,
                message,
                "Cấu hình không hợp lệ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
