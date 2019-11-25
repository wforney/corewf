// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Activities.Internals;

    /// <summary>
    /// The WorkflowDataContext class. This class cannot be inherited.
    /// Implements the <see cref="System.ComponentModel.CustomTypeDescriptor" />
    /// Implements the <see cref="System.ComponentModel.INotifyPropertyChanged" />
    /// Implements the <see cref="System.IDisposable" />
    /// </summary>
    /// <seealso cref="System.ComponentModel.CustomTypeDescriptor" />
    /// <seealso cref="System.ComponentModel.INotifyPropertyChanged" />
    /// <seealso cref="System.IDisposable" />
    [Fx.Tag.XamlVisible(false)]
    public sealed class WorkflowDataContext : CustomTypeDescriptor, INotifyPropertyChanged, IDisposable
    {
        /// <summary>
        /// The executor
        /// </summary>
        private readonly ActivityExecutor executor;
        /// <summary>
        /// The activity instance
        /// </summary>
        private ActivityInstance activityInstance;
        /// <summary>
        /// The location mapping
        /// </summary>
        private IDictionary<Location, PropertyDescriptorImpl> locationMapping;
        /// <summary>
        /// The property changed event handler
        /// </summary>
        private PropertyChangedEventHandler propertyChangedEventHandler;
        /// <summary>
        /// The properties
        /// </summary>
        private readonly PropertyDescriptorCollection properties;
        /// <summary>
        /// The cached resolution context
        /// </summary>
        private ActivityContext cachedResolutionContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowDataContext"/> class.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="activityInstance">The activity instance.</param>
        /// <param name="includeLocalVariables">if set to <c>true</c> [include local variables].</param>
        internal WorkflowDataContext(ActivityExecutor executor, ActivityInstance activityInstance, bool includeLocalVariables)
        {
            this.executor = executor;
            this.activityInstance = activityInstance;
            this.IncludesLocalVariables = includeLocalVariables;
            this.properties = this.CreateProperties();
        }

        /// <summary>
        /// Gets or sets a value indicating whether [includes local variables].
        /// </summary>
        /// <value><c>true</c> if [includes local variables]; otherwise, <c>false</c>.</value>
        internal bool IncludesLocalVariables
        {
            get;
            set;
        }

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the resolution context.
        /// </summary>
        /// <value>The resolution context.</value>
        /// <remarks>We want our own cached ActivityContext rather than using this.executor.GetResolutionContext
        /// because there is no synchronization of access to the executor's cached object and access thru
        /// this WorkflowDataContext will not be done on the workflow runtime thread.</remarks>
        private ActivityContext ResolutionContext
        {
            get
            {
                this.ThrowIfEnvironmentDisposed();
                if (this.cachedResolutionContext == null)
                {
                    this.cachedResolutionContext = new ActivityContext(this.activityInstance, this.executor)
                    {
                        AllowChainedEnvironmentAccess = true
                    };
                }
                else
                {
                    this.cachedResolutionContext.Reinitialize(this.activityInstance, this.executor);
                }
                return this.cachedResolutionContext;
            }
        }

        /// <summary>
        /// Gets the property changed event handler.
        /// </summary>
        /// <value>The property changed event handler.</value>
        private PropertyChangedEventHandler PropertyChangedEventHandler
        {
            get
            {
                if (this.propertyChangedEventHandler == null)
                {
                    this.propertyChangedEventHandler = new PropertyChangedEventHandler(this.OnLocationChanged);
                }
                return this.propertyChangedEventHandler;
            }
        }

        /// <summary>
        /// Creates the properties.
        /// </summary>
        /// <returns>PropertyDescriptorCollection.</returns>
        private PropertyDescriptorCollection CreateProperties()
        {
            // The name in child Activity will shadow the name in parent.
            var names = new Dictionary<string, object>();

            var propertyList = new List<PropertyDescriptorImpl>();

            var environment = this.activityInstance.Activity.PublicEnvironment;
            var isLocalEnvironment = true;
            while (environment != null)
            {
                foreach (var locRef in environment.GetLocationReferences())
                {
                    if (this.IncludesLocalVariables || !isLocalEnvironment || !(locRef is Variable))
                    {
                        this.AddProperty(locRef, names, propertyList);
                    }
                }

                environment = environment.Parent;
                isLocalEnvironment = false;
            }

            return new PropertyDescriptorCollection(propertyList.ToArray(), true);
        }

        /// <summary>
        /// Adds the property.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="names">The names.</param>
        /// <param name="propertyList">The property list.</param>
        private void AddProperty(LocationReference reference, Dictionary<string, object> names,
            List<PropertyDescriptorImpl> propertyList)
        {
            if (!string.IsNullOrEmpty(reference.Name) &&
                !names.ContainsKey(reference.Name))
            {
                names.Add(reference.Name, reference);
                var property = new PropertyDescriptorImpl(reference);
                propertyList.Add(property);
                this.AddNotifyHandler(property);
            }
        }

        /// <summary>
        /// Adds the notify handler.
        /// </summary>
        /// <param name="property">The property.</param>
        private void AddNotifyHandler(PropertyDescriptorImpl property)
        {
            var activityContext = this.ResolutionContext;
            try
            {
                var location = property.LocationReference.GetLocation(activityContext);
                if (location is INotifyPropertyChanged notify)
                {
                    notify.PropertyChanged += this.PropertyChangedEventHandler;

                    if (this.locationMapping == null)
                    {
                        this.locationMapping = new Dictionary<Location, PropertyDescriptorImpl>();
                    }
                    this.locationMapping.Add(location, property);
                }
            }
            finally
            {
                activityContext.Dispose();
            }
        }

        /// <summary>
        /// Handles the <see cref="LocationChanged" /> event.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/> instance containing the event data.</param>
        private void OnLocationChanged(object sender, PropertyChangedEventArgs e)
        {
            var handler = this.PropertyChanged;
            if (handler != null)
            {
                var location = (Location)sender;

                Fx.Assert(this.locationMapping != null, "Location mapping must not be null.");
                if (this.locationMapping.TryGetValue(location, out var property))
                {
                    if (e.PropertyName == "Value")
                    {
                        handler(this, new PropertyChangedEventArgs(property.Name));
                    }
                    else
                    {
                        handler(this, new PropertyChangedEventArgs(property.Name + "." + e.PropertyName));
                    }
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (this.locationMapping != null)
            {
                foreach (var pair in this.locationMapping)
                {
                    if (pair.Key is INotifyPropertyChanged notify)
                    {
                        notify.PropertyChanged -= this.PropertyChangedEventHandler;
                    }
                }
            }
        }

        /// <summary>
        /// Disposes the environment.
        /// </summary>
        /// <remarks>We need a separate method here from Dispose(), because Dispose currently
        /// doesn't make the WDC uncallable, it just unhooks it from notifications.</remarks>
        internal void DisposeEnvironment() => this.activityInstance = null;

        /// <summary>
        /// Throws if environment disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfEnvironmentDisposed()
        {
            if (this.activityInstance == null)
            {
                throw FxTrace.Exception.AsError(
                    new ObjectDisposedException(this.GetType().FullName, SR.WDCDisposed));
            }
        }

        /// <summary>
        /// Returns a collection of property descriptors for the object represented by this type descriptor.
        /// </summary>
        /// <returns>A <see cref="PropertyDescriptorCollection" /> containing the property descriptions for the object represented by this type descriptor. The default is <see cref="PropertyDescriptorCollection.Empty" />.</returns>
        public override PropertyDescriptorCollection GetProperties() => this.properties;

        /// <summary>
        /// The PropertyDescriptorImpl class.
        /// Implements the <see cref="System.ComponentModel.PropertyDescriptor" />
        /// </summary>
        /// <seealso cref="System.ComponentModel.PropertyDescriptor" />
        private class PropertyDescriptorImpl : PropertyDescriptor
        {
            // TODO 131998, We should support readonly LocationReferences.
            // bool isReadOnly;

            /// <summary>
            /// Initializes a new instance of the <see cref="PropertyDescriptorImpl"/> class.
            /// </summary>
            /// <param name="reference">The reference.</param>
            public PropertyDescriptorImpl(LocationReference reference)
                : base(reference.Name, Array.Empty<Attribute>()) => this.LocationReference = reference;

            /// <summary>
            /// When overridden in a derived class, gets the type of the component this property is bound to.
            /// </summary>
            /// <value>The type of the component.</value>
            public override Type ComponentType => typeof(WorkflowDataContext);

            /// <summary>
            /// When overridden in a derived class, gets a value indicating whether this property is read-only.
            /// </summary>
            /// <value><c>true</c> if this instance is read only; otherwise, <c>false</c>.</value>
            public override bool IsReadOnly =>
                    // TODO 131998, We should support readonly LocationReferences.
                    // return this.isReadOnly;
                    false;

            /// <summary>
            /// When overridden in a derived class, gets the type of the property.
            /// </summary>
            /// <value>The type of the property.</value>
            public override Type PropertyType => this.LocationReference.Type;

            /// <summary>
            /// Gets the location reference.
            /// </summary>
            /// <value>The location reference.</value>
            public LocationReference LocationReference { get; }

            /// <summary>
            /// When overridden in a derived class, returns whether resetting an object changes its value.
            /// </summary>
            /// <param name="component">The component to test for reset capability.</param>
            /// <returns><see langword="true" /> if resetting the component changes its value; otherwise, <see langword="false" />.</returns>
            public override bool CanResetValue(object component) => false;

            /// <summary>
            /// When overridden in a derived class, gets the current value of the property on a component.
            /// </summary>
            /// <param name="component">The component with the property for which to retrieve the value.</param>
            /// <returns>The value of a property for a given component.</returns>
            public override object GetValue(object component)
            {
                var dataContext = (WorkflowDataContext)component;

                var activityContext = dataContext.ResolutionContext;
                try
                {
                    return this.LocationReference.GetLocation(activityContext).Value;
                }
                finally
                {
                    activityContext.Dispose();
                }
            }

            /// <summary>
            /// When overridden in a derived class, resets the value for this property of the component to the default value.
            /// </summary>
            /// <param name="component">The component with the property value that is to be reset to the default value.</param>
            /// <exception cref="NotSupportedException"></exception>
            public override void ResetValue(object component) => throw FxTrace.Exception.AsError(new NotSupportedException(SR.CannotResetPropertyInDataContext));

            /// <summary>
            /// When overridden in a derived class, sets the value of the component to a different value.
            /// </summary>
            /// <param name="component">The component with the property value that is to be set.</param>
            /// <param name="value">The new value.</param>
            /// <exception cref="NotSupportedException"></exception>
            public override void SetValue(object component, object value)
            {
                if (this.IsReadOnly)
                {
                    throw FxTrace.Exception.AsError(new NotSupportedException(SR.PropertyReadOnlyInWorkflowDataContext(this.Name)));
                }

                var dataContext = (WorkflowDataContext)component;

                var activityContext = dataContext.ResolutionContext;
                try
                {
                    var location = this.LocationReference.GetLocation(activityContext);
                    location.Value = value;
                }
                finally
                {
                    activityContext.Dispose();
                }
            }

            /// <summary>
            /// When overridden in a derived class, determines a value indicating whether the value of this property needs to be persisted.
            /// </summary>
            /// <param name="component">The component with the property to be examined for persistence.</param>
            /// <returns><see langword="true" /> if the property should be persisted; otherwise, <see langword="false" />.</returns>
            public override bool ShouldSerializeValue(object component) => true;
        }
    }
}
