namespace Hyperlite.Explorer.Pages
{
    using System.Reflection;

    internal partial class AboutPage
    {
        public AboutPage()
        {
            this.InitializeComponent();
            this.description.Text = GetAssemblyAttribute<AssemblyDescriptionAttribute>().Description;
            this.version.Text = "Version " + GetAssemblyAttribute<AssemblyFileVersionAttribute>().Version;
            this.copyright.Text = GetAssemblyAttribute<AssemblyCopyrightAttribute>().Copyright;
        }

        /// <summary>
        /// The get assembly attribute.
        /// </summary>
        private static T GetAssemblyAttribute<T>()
        {
            return (T)Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(T), false)[0];
        }
    }
}
