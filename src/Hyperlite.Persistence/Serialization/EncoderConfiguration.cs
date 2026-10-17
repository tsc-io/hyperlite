namespace Hyperlite.Persistence.Serialization
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization;

    /// <summary>
    ///     Defines how to serialize DateTime.
    /// </summary>
    public enum DateTimeBehavior
    {
        /// <summary>
        ///     Serializes the date time as it is, local or utc.
        /// </summary>
        Default,

        /// <summary>
        ///     Converts local date time to utc on serialization and vice versa on deseralization.
        /// </summary>
        /// <remarks>
        ///     Has no effect on utc data times.
        /// </remarks>
        Utc
    }

    public sealed class EncoderConfiguration
    {
        // The default configuration, shared rather than copied per serializer.
        //
        // CreateFrom(null) used to hand every serializer its own copy of
        // Encoder.Configuration, and a serializer is constructed per serialized value. The
        // copy is what insulates a serializer from a later change to the global default,
        // which is worth keeping - so the copy is still taken, once, and reused until
        // something is reconfigured.
        //
        // Any setter on any instance drops the snapshot. That over-invalidates when a
        // caller configures an instance of their own, which is a startup-time act, and it
        // errs in the safe direction: an extra copy is slow, a stale one is wrong.
        //
        // Reconfiguring the global default while another thread is serializing races
        // exactly as it did before - on the fields, not on the snapshot.

        /// <summary>
        ///     The shared copy of the default configuration, or null if it has to be retaken.
        /// </summary>
        private static EncoderConfiguration defaultSnapshot;

        private SerializationBinder binder;

        private Action<BinaryWriter, Type, object, EncoderConfiguration> typeWriter;

        private DateTimeBehavior dateTimeBehavior;

        /// <summary>
        ///     Whether to strictly use explicit type codes.
        /// </summary>
        private bool strictExplicitTypeCodes;

        private bool useBaseTypeCode;

        /// <summary>
        ///     Whether this instance is the shared default snapshot.
        /// </summary>
        private readonly bool isDefaultSnapshot;

        private bool strictTypes = true;

        private bool enableSerializationCallbacks;

        /// <summary>
        ///     The types reading may create while strict types are on. Shared by a
        ///     configuration and its copies, so a snapshot of the default sees what is
        ///     allowed later.
        /// </summary>
        private readonly TypeAllowList typeAllowList;

        public EncoderConfiguration()
        {
            this.typeAllowList = new TypeAllowList();
        }

        /// <param name="configuration">
        ///     The encoder configuration to take over.
        /// </param>
        internal EncoderConfiguration(EncoderConfiguration configuration)
        {
            this.typeAllowList = configuration?.typeAllowList ?? new TypeAllowList();
            if (configuration != null)
            {
                this.strictTypes = configuration.strictTypes;
                this.enableSerializationCallbacks = configuration.enableSerializationCallbacks;
                this.binder = configuration.binder;
                this.typeWriter = configuration.typeWriter;

                this.strictExplicitTypeCodes = configuration.strictExplicitTypeCodes;
                this.dateTimeBehavior = configuration.dateTimeBehavior;
                this.useBaseTypeCode = configuration.useBaseTypeCode;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="EncoderConfiguration" /> class as
        ///     the shared default snapshot.
        /// </summary>
        /// <param name="configuration">
        ///     The encoder configuration to take over.
        /// </param>
        /// <param name="isDefaultSnapshot">
        ///     Marks the instance as the shared snapshot, so that handing it out through a
        ///     public property can be made to copy it first.
        /// </param>
        private EncoderConfiguration(EncoderConfiguration configuration, bool isDefaultSnapshot)
            : this(configuration)
        {
            this.isDefaultSnapshot = isDefaultSnapshot;
        }

        /// <param name="configuration">
        ///     The encoder configuration to take over.
        /// </param>
        private EncoderConfiguration(EncoderConfiguration configuration, EncoderConfiguration defaultConfiguration)
            : this(configuration)
        {
            if (defaultConfiguration != null)
            {
                if (this.binder == null)
                {
                    this.binder = defaultConfiguration.binder;
                }

                if (this.typeWriter == null)
                {
                    this.typeWriter = defaultConfiguration.typeWriter;
                }
            }
        }

        public SerializationBinder Binder
        {
            get { return this.binder; }

            set
            {
                if (value != null)
                {
                    this.binder = value;
                    defaultSnapshot = null;
                }
            }
        }

        public DateTimeBehavior DateTimeBehavior
        {
            get { return this.dateTimeBehavior; }

            set
            {
                this.dateTimeBehavior = value;
                defaultSnapshot = null;
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether to strictly use explicit type codes.
        /// </summary>
        public bool StrictExplicitTypeCodes
        {
            get { return this.strictExplicitTypeCodes; }

            set
            {
                this.strictExplicitTypeCodes = value;
                defaultSnapshot = null;
            }
        }

        /// <summary>
        ///     Gets or sets whether reading creates only the types the application declared
        ///     or allowed. On by default.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The serialized form records the type of what was written, and reading
        ///         creates the type the bytes name - running its constructor and its setters.
        ///         On, reading refuses with a <see cref="SerializationException" />, before
        ///         anything is created, every type that is not:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             declared - the type asked for, <c>FromByteArray&lt;T&gt;</c> or an
        ///             entity type, and what its members, elements and type arguments
        ///             declare;
        ///         </item>
        ///         <item>
        ///             allowed with <see cref="AllowType" /> or <see cref="AllowAssembly" />;
        ///         </item>
        ///         <item>
        ///             a primitive, an enum, or one of the framework value and collection
        ///             types the serializer supports.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         A value stored as a more specific type than its member declares - a
        ///         member typed <c>object</c>, an interface or a base class - needs that type
        ///         allowed. An array is checked by its element type, a generic type by its
        ///         definition and each argument. Off, reading creates any type the process
        ///         can load, so only data the application wrote itself should be read then.
        ///         Reading always uses <see cref="Encoder.Configuration" />, so this is set
        ///         there. The format does not change.
        ///     </para>
        /// </remarks>
        public bool StrictTypes
        {
            get { return this.strictTypes; }

            set
            {
                this.strictTypes = value;
                defaultSnapshot = null;
            }
        }

        /// <summary>
        ///     Gets or sets whether the serializer calls the code a type runs around its own
        ///     serialization. Off by default.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         On, writing calls the methods a type marks <c>[OnSerializing]</c> and
        ///         <c>[OnSerialized]</c>, and reading those marked <c>[OnDeserializing]</c> and
        ///         <c>[OnDeserialized]</c> and <c>IDeserializationCallback.OnDeserialization</c>.
        ///         Off, none of them is called, and reading runs no method of a type beyond its
        ///         constructor and its setters - with <see cref="StrictTypes" />, of a declared
        ///         or allowed type only.
        ///     </para>
        ///     <para>
        ///         A type that relies on a callback to restore what it does not store - a
        ///         cache, an index, a derived field - needs this on. Writing honours the
        ///         serializer's configuration; reading always uses
        ///         <see cref="Encoder.Configuration" />. The format does not change.
        ///     </para>
        /// </remarks>
        public bool EnableSerializationCallbacks
        {
            get { return this.enableSerializationCallbacks; }

            set
            {
                this.enableSerializationCallbacks = value;
                defaultSnapshot = null;
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether to use base type code.
        /// </summary>
        public bool UseBaseTypeCode
        {
            get { return this.useBaseTypeCode; }

            set
            {
                this.useBaseTypeCode = value;
                defaultSnapshot = null;
            }
        }

        public Action<BinaryWriter, Type, object, EncoderConfiguration> TypeWriter
        {
            get { return this.typeWriter; }

            set
            {
                if (value != null)
                {
                    this.typeWriter = value;
                    defaultSnapshot = null;
                }
            }
        }

        /// <summary>
        ///     Allows reading to create the type, and what its members declare, while
        ///     <see cref="StrictTypes" /> is on.
        /// </summary>
        /// <param name="type">
        ///     The type to allow.
        /// </param>
        public void AllowType(Type type)
        {
            this.typeAllowList.Allow(type);
        }

        /// <summary>
        ///     Allows reading to create every type of the assembly while
        ///     <see cref="StrictTypes" /> is on.
        /// </summary>
        /// <param name="assembly">
        ///     The assembly to allow.
        /// </param>
        public void AllowAssembly(Assembly assembly)
        {
            this.typeAllowList.Allow(assembly);
        }

        /// <summary>
        ///     Gets the types reading may create while strict types are on.
        /// </summary>
        internal TypeAllowList TypeAllowList => this.typeAllowList;

        /// <summary>
        ///     Creates a new instance of the <see cref="EncoderConfiguration" /> class.
        /// </summary>
        /// <param name="configuration">
        ///     The encoder configuration to take over.
        /// </param>
        internal static EncoderConfiguration CreateFrom(EncoderConfiguration configuration)
        {
            if (configuration != null)
            {
                return new EncoderConfiguration(configuration, Encoder.Configuration);
            }

            // a lost race just means two threads each take a faithful snapshot and one of
            // them is kept, so neither the read nor the write needs an interlock
            return defaultSnapshot
                   ?? (defaultSnapshot = new EncoderConfiguration(Encoder.Configuration, true));
        }

        /// <summary>
        ///     Gets a value indicating whether this is the shared default snapshot, and so
        ///     has to be copied before it is handed to a caller who may write to it.
        /// </summary>
        internal bool IsDefaultSnapshot => this.isDefaultSnapshot;
    }
}
