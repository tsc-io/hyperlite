namespace Hyperlite.Explorer.Pages
{
    using System.Globalization;
    using System.Windows;
    using System.Windows.Controls;

    internal partial class ExplorePage
    {
        public ExplorePage()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) => this.ExpandRoot();

            DatabaseSession.ConnectionStateChanged += (s, e) =>
                {
                    if (e.ConnectionState == ConnectionState.Connecting)
                    {
                        this.connectionText.Text = e.ConnectionString;
                        this.header.Visibility = Visibility.Visible;
                    }
                    else if (e.ConnectionState == ConnectionState.Connected)
                    {
                        this.directoryView.ItemsSource = new ObservableList<DatabaseDirectoryInfo> { DatabaseSession.Instance.GetDirectoryInfo("/") };
                        this.ExpandRoot();
                        DatabaseScanner.ScannerStateChanged += this.HandleScannerStateChanged;
                    }
                    else if (e.ConnectionState == ConnectionState.Disconnecting)
                    {
                        this.directoryView.ItemsSource = null;
                        this.header.Visibility = Visibility.Collapsed;
                        DatabaseScanner.ScannerStateChanged -= this.HandleScannerStateChanged;
                    }
                };

            this.directoryView.SelectedItemChanged += (s, e) =>
                {
                    this.cellsScannedText.Text = string.Empty;
                    this.bytesScannedText.Text = string.Empty;
                };
        }

        private void ExpandRoot()
        {
            if (this.directoryView.ItemsSource != null)
            {
                var treeItem = this.directoryView.ItemContainerGenerator.ContainerFromItem(this.directoryView.Items[0]) as TreeViewItem;
                if (treeItem != null)
                {
                    treeItem.IsExpanded = true;
                }
            }
        }

        private void HandleScannerStateChanged(object sender, NotifyScannerStateChangedEventArgs e)
        {
            switch (e.ScannerState)
            {
                case ScannerState.Begin:
                    this.cellsScannedText.Text = "scanning...";
                    this.bytesScannedText.Text = string.Empty;
                    this.cellsView.CanUserSortColumns = false;
                    break;
                case ScannerState.Executing:
                    this.cellsScannedText.Text = string.Format(CultureInfo.InvariantCulture, "scanning cells {0}", e.CellsScanned);
                    this.bytesScannedText.Text = string.Format(CultureInfo.InvariantCulture, "{0} bytes", e.BytesScanned);
                    break;
                case ScannerState.Completed:
                    this.cellsScannedText.Text = string.Format(CultureInfo.InvariantCulture, "{0} cells", e.CellsScanned);
                    this.bytesScannedText.Text = string.Format(CultureInfo.InvariantCulture, "{0} bytes", e.BytesScanned);
                    this.cellsView.CanUserSortColumns = true;
                    break;
            }
        }
    }
}
