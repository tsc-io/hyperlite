namespace Hyperlite.Persistence.Reflection
{
    using System;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.Serialization;

    internal sealed class InspectedSerializable
    {
        /// <param name="type">
        ///     The type to inspect.
        /// </param>
        internal InspectedSerializable(Type type)
        {
            this.InspectedType = type;
            try
            {
                this.CreateInstance = CreateCreateInstanceMethod(type);
            }
            catch (Exception exception)
            {
                Logging.TraceException(exception);
            }
        }

        /// <value>
        ///     The CreateInstance function.
        /// </value>
        internal Func<SerializationInfo, StreamingContext, object> CreateInstance { get; }

        internal Type InspectedType { get; }

        /// <summary>
        ///     Create the CreateInstance function for the type specified.
        /// </summary>
        /// <returns>
        ///     The newly created CreateInstance function or null.
        /// </returns>
        private static Func<SerializationInfo, StreamingContext, object> CreateCreateInstanceMethod(Type type)
        {
            var constructorInfo = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(SerializationInfo), typeof(StreamingContext) }, null);

            if (constructorInfo == null)
            {
                return null;
            }

            var method = new DynamicMethod(
                "hyperlite_Ctor" + constructorInfo.Name, typeof(object),
                new[] { typeof(SerializationInfo), typeof(StreamingContext) }, constructorInfo.Module, true)
            { InitLocals = false };
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Newobj, constructorInfo);
            generator.Emit(OpCodes.Ret);

            return (Func<SerializationInfo, StreamingContext, object>)method.CreateDelegate(
                typeof(Func<SerializationInfo, StreamingContext, object>));
        }
    }
}
