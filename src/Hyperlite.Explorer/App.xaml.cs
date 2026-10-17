namespace Hyperlite.Explorer
{
    using System;
    using System.Windows.Threading;

    internal partial class App
    {
        /// <summary>
        /// Prevents a default instance of the <see cref="App"/> class from being created.
        /// </summary>
        private App()
        {
            this.Startup += (s, e) => { AppDomain.CurrentDomain.UnhandledException += this.HandleAppDomainUnhandledException; };
        }

        private void HandleAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            this.HandleException(e.ExceptionObject as Exception);
        }

        private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            this.HandleException(e.Exception);
            e.Handled = true;
        }

        private void HandleException(Exception e)
        {
            var mainWindow = this.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                try
                {
                    mainWindow.ShowExceptionPage(e);
                }
                catch
                {
                }
            }
        }
    }
}
