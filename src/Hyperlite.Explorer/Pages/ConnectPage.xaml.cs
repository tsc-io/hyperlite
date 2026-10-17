namespace Hyperlite.Explorer.Pages
{
    using System;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Windows;
    using System.Windows.Input;
    using System.Windows.Navigation;
    using System.Windows.Threading;

    using Hyperlite.Explorer.Properties;

    internal partial class ConnectPage
    {
        private DispatcherTimer timer;

        public ConnectPage()
        {
            this.InitializeComponent();
            this.RecentConnections = new ObservableStringCollectionDecorator(Settings.Default.RecentConnections ?? (Settings.Default.RecentConnections = new StringCollection()));
            this.recentConnections.Visibility = this.RecentConnections.Count > 0 ? Visibility.Visible : Visibility.Hidden;

            this.Loaded += (s, e) => this.connectionString.Focus();

            this.connectionString.GotKeyboardFocus += (s, e) => this.CollapseConnectionErrorMessage();

            this.connect.PreviewMouseDown += (s, e) => this.CollapseConnectionErrorMessage();

            this.connect.Click += (s, e) => this.Connect(this.connectionString.Text);

            DatabaseSession.ConnectionStateChanged += (s, e) =>
                {
                    if (e.ConnectionState == ConnectionState.Connected)
                    {
                        this.AddToRecentConnections(e.ConnectionString);
                    }
                };
        }

        public ObservableStringCollectionDecorator RecentConnections { get; private set; }

        private void AddToRecentConnections(string connectionString)
        {
            var rc = this.RecentConnections.ToList();
            rc.Insert(0, connectionString);
            rc = rc.Distinct().ToList();
            while (rc.Count > Settings.Default.MaxRecentConnections)
            {
                rc.RemoveAt(rc.Count - 1);
            }

            this.RecentConnections.Clear();
            this.RecentConnections.AddRange(rc);
            this.recentConnections.Visibility = this.RecentConnections.Count > 0 ? Visibility.Visible : Visibility.Hidden;
        }

        private void CollapseConnectionErrorMessage()
        {
            this.connectionError.Visibility = Visibility.Collapsed;
            if (this.timer != null)
            {
                this.timer.Stop();
                this.timer = null;
            }
        }

        private void Connect(string connectionString)
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    DatabaseSession.Connect(connectionString);
                }
                catch
                {
                    this.connectionError.Visibility = Visibility.Visible;
                    this.timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                    this.timer.Tick += (t, tick) => this.CollapseConnectionErrorMessage();
                    this.timer.Start();
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void HandleRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            this.Connect(e.Uri.ToString());
            e.Handled = true;
        }
    }
}
