// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Diagnostics;
    using System.Runtime.Serialization;

    /// <summary>
    /// The Location class.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("{Value}")]
    public abstract partial class Location
    {
        private TemporaryResolutionData temporaryResolutionData;

        /// <summary>
        /// Initializes a new instance of the <see cref="Location" /> class.
        /// </summary>
        protected Location()
        {
        }

        /// <summary>
        /// Gets a value indicating whether [buffer gets on collapse].
        /// </summary>
        /// <value><c>true</c> if [buffer gets on collapse]; otherwise, <c>false</c>.</value>
        internal bool BufferGetsOnCollapse => this.temporaryResolutionData.BufferGetsOnCollapse;

        /// <summary>
        /// Gets the type of the location.
        /// </summary>
        /// <value>The type of the location.</value>
        public abstract Type LocationType { get; }

        /// <summary>
        /// Gets the temporary resolution environment.
        /// </summary>
        /// <value>The temporary resolution environment.</value>
        /// <remarks>
        /// When we are resolving an expression that resolves to a reference to a location we need
        /// some way of notifying the LocationEnvironment that it should extract the inner location
        /// and throw away the outer one. OutArgument and InOutArgument create these
        /// TemporaryResolutionLocations if their expression resolution goes async and
        /// LocationEnvironment gets rid of them in CollapseTemporaryResolutionLocations().
        /// </remarks>
        internal LocationEnvironment TemporaryResolutionEnvironment => this.temporaryResolutionData.TemporaryResolutionEnvironment;

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public object? Value
        {
            get => this.ValueCore;
            set => this.ValueCore = value;
        }

        /// <summary>
        /// Gets a value indicating whether this instance can be mapped.
        /// </summary>
        /// <value><c>true</c> if this instance can be mapped; otherwise, <c>false</c>.</value>
        internal virtual bool CanBeMapped => false;

        /// <summary>
        /// Gets or sets the serialized temporary resolution data.
        /// </summary>
        /// <value>The serialized temporary resolution data.</value>
        [DataMember(EmitDefaultValue = false, Name = "temporaryResolutionData")]
        internal TemporaryResolutionData SerializedTemporaryResolutionData
        {
            get => this.temporaryResolutionData;
            set => this.temporaryResolutionData = value;
        }

        /// <summary>
        /// Gets or sets the value core.
        /// </summary>
        /// <value>The value core.</value>
        protected abstract object? ValueCore { get; set; }

        /// <summary>
        /// Creates the default value.
        /// </summary>
        /// <returns>System.Object.</returns>
        internal virtual object? CreateDefaultValue()
        {
            Fx.Assert("We should only call this on Location<T>");
            return null;
        }

        /// <summary>
        /// Creates the reference.
        /// </summary>
        /// <param name="bufferGets">if set to <c>true</c> [buffer gets].</param>
        /// <returns>Location.</returns>
        internal virtual Location CreateReference(bool bufferGets)
        {
            return this.CanBeMapped || bufferGets ? new ReferenceLocation(this, bufferGets) : (this);
        }

        /// <summary>
        /// Sets the temporary resolution data.
        /// </summary>
        /// <param name="resolutionEnvironment">The resolution environment.</param>
        /// <param name="bufferGetsOnCollapse">if set to <c>true</c> [buffer gets on collapse].</param>
        internal void SetTemporaryResolutionData(LocationEnvironment resolutionEnvironment, bool bufferGetsOnCollapse) =>
            this.temporaryResolutionData = new TemporaryResolutionData
            {
                TemporaryResolutionEnvironment = resolutionEnvironment,
                BufferGetsOnCollapse = bufferGetsOnCollapse
            };
    }

    /// <summary>
    /// The Location class. Implements the <see cref="System.Activities.Location" />
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <seealso cref="System.Activities.Location" />
    [DataContract]
    public partial class Location<T> : Location
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Location{T}" /> class.
        /// </summary>
        public Location()
            : base()
        {
        }

        /// <summary>
        /// Gets the type of the location.
        /// </summary>
        /// <value>The type of the location.</value>
        public override Type LocationType => typeof(T);

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public virtual new T Value
        {
            get => this.SerializedValue;
            set => this.SerializedValue = value;
        }

        [DataMember(EmitDefaultValue = false, Name = "value")]
        internal T SerializedValue { get; set; }

        /// <summary>
        /// Gets or sets the typed value.
        /// </summary>
        /// <value>The typed value.</value>
        internal T TypedValue
        {
            get => this.Value;
            set => this.Value = value;
        }

        /// <summary>
        /// Gets or sets the value core.
        /// </summary>
        /// <value>The value core.</value>
        protected override sealed object? ValueCore
        {
            get => this.Value;
            set => this.Value = TypeHelper.Convert<T>(value);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString() => this.SerializedValue != null ? this.SerializedValue.ToString() : "<null>";

        internal override object? CreateDefaultValue()
        {
            Fx.Assert(typeof(T).GetGenericTypeDefinition() == typeof(Location<>), "We should only be calling this with location subclasses.");

            return Activator.CreateInstance<T>();
        }

        internal override Location CreateReference(bool bufferGets)
        {
            if (this.CanBeMapped || bufferGets)
            {
                return new ReferenceLocation(this, bufferGets);
            }

            return this;
        }
    }
}
