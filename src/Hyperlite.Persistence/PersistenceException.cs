namespace Hyperlite.Persistence
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class PersistenceException : Exception
    {
        public PersistenceException()
        {
        }

        /// <param name="message">
        ///     The exception message.
        /// </param>
        public PersistenceException(string message)
            : base(message)
        {
        }

        /// <param name="message">
        ///     The exception message.
        /// </param>
        public PersistenceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected PersistenceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
