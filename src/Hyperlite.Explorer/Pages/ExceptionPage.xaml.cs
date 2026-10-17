namespace Hyperlite.Explorer.Pages
{
    using System;

    public partial class ExceptionPage
    {
        public ExceptionPage(Exception e)
        {
            this.InitializeComponent();
            this.exceptionText.Text = e != null ? e.ToString() : "Unknown exception occurred.";
        }
    }
}
