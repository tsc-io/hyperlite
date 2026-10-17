namespace Hyperlite.Explorer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;

    using Hyperlite.Explorer.Pages;
    using Hyperlite.Explorer.Properties;

    internal partial class MainWindow : IDisposable
    {
        private List<TextBlock> menuStripItems;

        private Control[] pages;

        public MainWindow()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
                {
                    var menuDockPanel = (DockPanel)this.menuStrip.Template.FindName("MenuStrip", this.menuStrip);
                    this.menuStripItems = menuDockPanel.Children.OfType<TextBlock>().ToList();
                    foreach (var menuStripItem in this.menuStripItems)
                    {
                        menuStripItem.PreviewMouseDown += this.HandleMenuStripPreviewMouseDown;
                    }

                    this.pages = new Control[] { new ConnectPage(), new ExplorePage(), new AboutPage() };
                    this.SelectMenuStripItem(this.menuStripItems[0]);
                };

            this.Closed += (s, e) =>
                {
                    try
                    {
                        Settings.Default.Save();
                        DatabaseSession.Disconnect();
                    }
                    catch
                    {
                    }
                };

            DatabaseSession.ConnectionStateChanged += (s, e) =>
                {
                    if (e.ConnectionState == ConnectionState.Connected)
                    {
                        this.SelectMenuStripItem(this.menuStripItems[1]);
                    }
                };
        }

        public void Dispose()
        {
            DatabaseSession.Disconnect();
        }

        internal void ShowExceptionPage(Exception e)
        {
            foreach (var menuStripItem in this.menuStripItems)
            {
                menuStripItem.IsEnabled = true;
            }

            this.currentPage.Tag = new ExceptionPage(e);
        }

        private void HandleMenuStripPreviewMouseDown(object sender, RoutedEventArgs e)
        {
            this.SelectMenuStripItem(sender as TextBlock);
        }

        private void SelectMenuStripItem(TextBlock selectedMenuStripItem)
        {
            foreach (var menuStripItem in this.menuStripItems)
            {
                menuStripItem.IsEnabled = menuStripItem != selectedMenuStripItem;
            }

            this.currentPage.Tag = this.pages[this.menuStripItems.IndexOf(selectedMenuStripItem)];
        }
    }
}
