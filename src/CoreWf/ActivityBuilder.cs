// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
#if NET45
    using System.Activities.Debugger;
#endif

    using System.Activities.Validation;
    using System.Activities.XamlIntegration;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Windows.Markup;
    using System.Xaml;
    using System;

    /// <summary>
    /// The ActivityBuilder class. This class cannot be inherited.
    /// </summary>
    [ContentProperty("Implementation")]
    public sealed partial class ActivityBuilder
#if NET45
        : IDebuggableWorkflowTree
#endif
    {
        // define attached properties that will identify PropertyReferenceExtension-based
        // object properties
        /// <summary>
        /// The property reference property identifier
        /// </summary>
        private static readonly AttachableMemberIdentifier propertyReferencePropertyID = new AttachableMemberIdentifier(typeof(ActivityBuilder), "PropertyReference");

        /// <summary>
        /// The property references property identifier
        /// </summary>
        private static readonly AttachableMemberIdentifier propertyReferencesPropertyID = new AttachableMemberIdentifier(typeof(ActivityBuilder), "PropertyReferences");

        /// <summary>
        /// The properties
        /// </summary>
        private KeyedCollection<string, DynamicActivityProperty> properties;

        /// <summary>
        /// The constraints
        /// </summary>
        private Collection<Constraint> constraints;

        /// <summary>
        /// The attributes
        /// </summary>
        private Collection<Attribute> attributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityBuilder"/> class.
        /// </summary>
        public ActivityBuilder()
        {
        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        /// <summary>
        /// Gets the attributes.
        /// </summary>
        /// <value>The attributes.</value>
        [DependsOn("Name")]
        public Collection<Attribute> Attributes
        {
            get
            {
                if (this.attributes == null)
                {
                    this.attributes = new Collection<Attribute>();
                }

                return this.attributes;
            }
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <value>The properties.</value>
        [Browsable(false)]
        [DependsOn("Attributes")]
        public KeyedCollection<string, DynamicActivityProperty> Properties
        {
            get
            {
                if (this.properties == null)
                {
                    this.properties = new ActivityPropertyCollection();
                }

                return this.properties;
            }
        }

        /// <summary>
        /// Gets the constraints.
        /// </summary>
        /// <value>The constraints.</value>
        [DependsOn("Properties")]
        [Browsable(false)]
        public Collection<Constraint> Constraints
        {
            get
            {
                if (this.constraints == null)
                {
                    this.constraints = new Collection<Constraint>();
                }

                return this.constraints;
            }
        }

        /// <summary>
        /// Gets or sets the implementation version.
        /// </summary>
        /// <value>The implementation version.</value>
        [TypeConverter(typeof(ImplementationVersionConverter))]
        [DefaultValue(null)]
        [DependsOn("Name")]
        public Version ImplementationVersion { get; set; }

        /// <summary>
        /// Gets or sets the implementation.
        /// </summary>
        /// <value>The implementation.</value>
        [DefaultValue(null)]
        [Browsable(false)]
        [DependsOn("Constraints")]
        public Activity Implementation { get; set; }

        // Back-compat workaround: PropertyReference shipped in 4.0. PropertyReferences is new in 4.5.
        //
        // Requirements:
        // - Runtime compat: Get/SetPropertyReference needs to continue to work, both when set programatically
        //   and when loading a doc which contains only one PropertyReference on an object.
        // - Serialization compat: If only one PropertyReference was set, we shouldn't serialize PropertyReferences.
        //   (Only affects when ActivityBuilder is used directly with XamlServices, since ActivityXamlServices
        //   will convert ActivityPropertyReference to PropertyReferenceExtension.)
        // - Usability: To avoid the designer needing to support two separate access methods, we want
        //   the value from SetPropertyReference to also appear in the PropertyReferences collection.

        // <ActivityBuilder.PropertyReference>activity property name</ActivityBuilder.PropertyReference>
        /// <summary>
        /// Gets the property reference.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>ActivityPropertyReference.</returns>
        public static ActivityPropertyReference GetPropertyReference(object target) => GetPropertyReferenceCollection(target).SingleItem;

        // <ActivityBuilder.PropertyReference>activity property name</ActivityBuilder.PropertyReference>
        /// <summary>
        /// Sets the property reference.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="value">The value.</param>
        public static void SetPropertyReference(object target, ActivityPropertyReference value) => GetPropertyReferenceCollection(target).SingleItem = value;

        /// <summary>
        /// Gets the property references.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>IList&lt;ActivityPropertyReference&gt;.</returns>
        public static IList<ActivityPropertyReference> GetPropertyReferences(object target) => GetPropertyReferenceCollection(target);

        /// <summary>
        /// Shoulds the serialize property reference.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public static bool ShouldSerializePropertyReference(object target)
        {
            var propertyReferences = GetPropertyReferenceCollection(target);
            return propertyReferences.Count == 1 && propertyReferences.SingleItem != null;
        }

        /// <summary>
        /// Shoulds the serialize property references.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public static bool ShouldSerializePropertyReferences(object target)
        {
            var propertyReferences = GetPropertyReferenceCollection(target);
            return propertyReferences.Count > 1 || propertyReferences.SingleItem == null;
        }

        /// <summary>
        /// Determines whether [has property references] [the specified target].
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns><c>true</c> if [has property references] [the specified target]; otherwise, <c>false</c>.</returns>
        internal static bool HasPropertyReferences(object target) =>
            AttachablePropertyServices.TryGetProperty(target, propertyReferencesPropertyID, out PropertyReferenceCollection propertyReferences)
                ? propertyReferences.Count > 0
                : false;

        /// <summary>
        /// Gets the property reference collection.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>PropertyReferenceCollection.</returns>
        private static PropertyReferenceCollection GetPropertyReferenceCollection(object target)
        {
            if (!AttachablePropertyServices.TryGetProperty(target, propertyReferencesPropertyID, out PropertyReferenceCollection propertyReferences))
            {
                propertyReferences = new PropertyReferenceCollection(target);
                AttachablePropertyServices.SetProperty(target, propertyReferencesPropertyID, propertyReferences);
            }

            return propertyReferences;
        }

#if NET45
        Activity IDebuggableWorkflowTree.GetWorkflowRoot()
        {
            return this.Implementation;
        }
#endif

        /// <summary>
        /// Creates the activity property collection.
        /// </summary>
        /// <returns>KeyedCollection&lt;System.String, DynamicActivityProperty&gt;.</returns>
        internal static KeyedCollection<string, DynamicActivityProperty> CreateActivityPropertyCollection() => new ActivityPropertyCollection();
    }

    /// <summary>
    /// The ActivityBuilder class. This class cannot be inherited.
    /// </summary>
    /// <typeparam name="TResult">The type of the t result.</typeparam>
    [ContentProperty("Implementation")]
    public sealed class ActivityBuilder<TResult>
#if NET45
        : IDebuggableWorkflowTree
#endif
    {
        /// <summary>
        /// The properties
        /// </summary>
        private KeyedCollection<string, DynamicActivityProperty> properties;

        /// <summary>
        /// The constraints
        /// </summary>
        private Collection<Constraint> constraints;

        /// <summary>
        /// The attributes
        /// </summary>
        private Collection<Attribute> attributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityBuilder{TResult}"/> class.
        /// </summary>
        public ActivityBuilder()
        {
        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        /// <summary>
        /// Gets the attributes.
        /// </summary>
        /// <value>The attributes.</value>
        [DependsOn("Name")]
        public Collection<Attribute> Attributes
        {
            get
            {
                if (this.attributes == null)
                {
                    this.attributes = new Collection<Attribute>();
                }

                return this.attributes;
            }
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <value>The properties.</value>
        [Browsable(false)]
        [DependsOn("Attributes")]
        public KeyedCollection<string, DynamicActivityProperty> Properties
        {
            get
            {
                if (this.properties == null)
                {
                    this.properties = ActivityBuilder.CreateActivityPropertyCollection();
                }

                return this.properties;
            }
        }

        /// <summary>
        /// Gets the constraints.
        /// </summary>
        /// <value>The constraints.</value>
        [DependsOn("Properties")]
        [Browsable(false)]
        public Collection<Constraint> Constraints
        {
            get
            {
                if (this.constraints == null)
                {
                    this.constraints = new Collection<Constraint>();
                }

                return this.constraints;
            }
        }

        /// <summary>
        /// Gets or sets the implementation version.
        /// </summary>
        /// <value>The implementation version.</value>
        [TypeConverter(typeof(ImplementationVersionConverter))]
        [DefaultValue(null)]
        [DependsOn("Name")]
        public Version ImplementationVersion { get; set; }

        /// <summary>
        /// Gets or sets the implementation.
        /// </summary>
        /// <value>The implementation.</value>
        [DefaultValue(null)]
        [Browsable(false)]
        [DependsOn("Constraints")]
        public Activity Implementation { get; set; }

#if NET45
        Activity IDebuggableWorkflowTree.GetWorkflowRoot()
        {
            return this.Implementation;
        }
#endif
    }
}