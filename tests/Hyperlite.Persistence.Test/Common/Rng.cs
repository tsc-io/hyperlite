namespace Hyperlite.Persistence.Test.Common
{
    using System;


    /// <summary>
    /// The random number generator.
    /// </summary>
    internal static class Rng
    {
        /// <summary>
        /// The random number generator instance.
        /// </summary>
        private static readonly Random RngInstance = new Random();

        public static Random Instance
        {
            get
            {
                return RngInstance;
            }
        }
    }
}
