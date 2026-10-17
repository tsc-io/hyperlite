namespace Hyperlite.Persistence.Serialization
{
    public class SerializationBase
    {
        /// <summary>
        ///     The default capacity for the memory stream.
        /// </summary>
        private static int defaultCapacity = 1024;

        protected SerializationBase()
        {
        }

        /// <summary>
        ///     Gets or sets the memory stream default capacity.
        /// </summary>
        public static int DefaultCapacity
        {
            get { return defaultCapacity; }

            set
            {
                if (value > 0)
                {
                    defaultCapacity = value;
                }
            }
        }
    }
}
