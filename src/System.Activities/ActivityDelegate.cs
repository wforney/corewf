// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Windows.Markup;

    //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotHaveIncorrectSuffix,
    //    Justification = "Part of the sanctioned, public WF OM")]
    /// <summary>
    /// The ActivityDelegate class.
    /// </summary>
    [ContentProperty("Handler")]
    public abstract class ActivityDelegate
    {
        /// <summary>
        /// The argument10 name
        /// </summary>
        internal static string Argument10Name = "Argument10";

        /// <summary>
        /// The argument11 name
        /// </summary>
        internal static string Argument11Name = "Argument11";

        /// <summary>
        /// The argument12 name
        /// </summary>
        internal static string Argument12Name = "Argument12";

        /// <summary>
        /// The argument13 name
        /// </summary>
        internal static string Argument13Name = "Argument13";

        /// <summary>
        /// The argument14 name
        /// </summary>
        internal static string Argument14Name = "Argument14";

        /// <summary>
        /// The argument15 name
        /// </summary>
        internal static string Argument15Name = "Argument15";

        /// <summary>
        /// The argument16 name
        /// </summary>
        internal static string Argument16Name = "Argument16";

        /// <summary>
        /// The argument1 name
        /// </summary>
        internal static string Argument1Name = "Argument1";

        /// <summary>
        /// The argument2 name
        /// </summary>
        internal static string Argument2Name = "Argument2";

        /// <summary>
        /// The argument3 name
        /// </summary>
        internal static string Argument3Name = "Argument3";

        /// <summary>
        /// The argument4 name
        /// </summary>
        internal static string Argument4Name = "Argument4";

        /// <summary>
        /// The argument5 name
        /// </summary>
        internal static string Argument5Name = "Argument5";

        /// <summary>
        /// The argument6 name
        /// </summary>
        internal static string Argument6Name = "Argument6";

        /// <summary>
        /// The argument7 name
        /// </summary>
        internal static string Argument7Name = "Argument7";

        /// <summary>
        /// The argument8 name
        /// </summary>
        internal static string Argument8Name = "Argument8";

        /// <summary>
        /// The argument9 name
        /// </summary>
        internal static string Argument9Name = "Argument9";

        /// <summary>
        /// The argument name
        /// </summary>
        internal static string ArgumentName = "Argument";

        /// <summary>
        /// The result argument name
        /// </summary>
        internal static string ResultArgumentName = "Result";

        /// <summary>
        /// The cache identifier
        /// </summary>
        private int cacheId;

        /// <summary>
        /// The delegate parameters
        /// </summary>
        private IList<RuntimeDelegateArgument> delegateParameters;

        /// <summary>
        /// The display name
        /// </summary>
        private string displayName;

        /// <summary>
        /// The is display name set
        /// </summary>
        private bool isDisplayNameSet;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityDelegate" /> class.
        /// </summary>
        protected ActivityDelegate()
        {
        }

        /// <summary>
        /// Gets or sets the display name.
        /// </summary>
        /// <value>The display name.</value>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(this.displayName))
                {
                    this.displayName = this.GetType().Name;
                }

                return this.displayName;
            }
            set
            {
                this.isDisplayNameSet = true;
                this.displayName = value;
            }
        }

        /// <summary>
        /// Gets or sets the handler.
        /// </summary>
        /// <value>The handler.</value>
        [DefaultValue(null)]
        public Activity Handler { get; set; }

        /// <summary>
        /// Gets or sets the environment.
        /// </summary>
        /// <value>The environment.</value>
        internal LocationReferenceEnvironment Environment { get; set; }

        /// <summary>
        /// Gets the owner.
        /// </summary>
        /// <value>The owner.</value>
        internal Activity Owner { get; private set; }

        /// <summary>
        /// Gets the type of the parent collection.
        /// </summary>
        /// <value>The type of the parent collection.</value>
        internal ActivityCollectionType ParentCollectionType { get; private set; }

        /// <summary>
        /// Gets the runtime delegate arguments.
        /// </summary>
        /// <value>The runtime delegate arguments.</value>
        internal IList<RuntimeDelegateArgument> RuntimeDelegateArguments =>
            this.delegateParameters == null
                    ? new ReadOnlyCollection<RuntimeDelegateArgument>(this.InternalGetRuntimeDelegateArguments())
                    : this.delegateParameters;

        /// <summary>
        /// Shoulds the display name of the serialize.
        /// </summary>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool ShouldSerializeDisplayName() => this.isDisplayNameSet;

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString() => this.DisplayName;

        /// <summary>
        /// Determines whether this instance [can be scheduled by] the specified parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>
        /// <c>true</c> if this instance [can be scheduled by] the specified parent; otherwise, <c>false</c>.
        /// </returns>
        internal bool CanBeScheduledBy(Activity parent) =>
            // fast path if we're the sole (or first) child
            object.ReferenceEquals(parent, this.Owner)
                ? this.ParentCollectionType != ActivityCollectionType.Imports
                : parent.Delegates.Contains(this) || parent.ImplementationDelegates.Contains(this);

        /// <summary>
        /// Initializes the relationship.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="collectionType">Type of the collection.</param>
        /// <param name="validationErrors">The validation errors.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        internal bool InitializeRelationship(Activity parent, ActivityCollectionType collectionType, ref IList<ValidationError> validationErrors)
        {
            if (this.cacheId == parent.CacheId)
            {
                Fx.Assert(this.Owner != null, "We must have set the owner when we set the cache ID");

                // This means that we've already encountered a parent in the tree

                // Validate that it is visible.

                // In order to see the activity the new parent must be in the implementation IdSpace
                // of an activity which has a public reference to it.
                var referenceTarget = parent.MemberOf.Owner;

                if (referenceTarget == null)
                {
                    var handler = this.Handler;

                    if (handler == null)
                    {
                        ActivityUtilities.Add(
                            ref validationErrors,
                            new ValidationError(SR.ActivityDelegateCannotBeReferencedWithoutTargetNoHandler(parent.DisplayName, this.Owner.DisplayName),
                            false,
                            parent));
                    }
                    else
                    {
                        ActivityUtilities.Add(
                            ref validationErrors,
                            new ValidationError(SR.ActivityDelegateCannotBeReferencedWithoutTarget(handler.DisplayName, parent.DisplayName, this.Owner.DisplayName),
                            false,
                            parent));
                    }

                    return false;
                }
                else if (!referenceTarget.Delegates.Contains(this) && !referenceTarget.ImportedDelegates.Contains(this))
                {
                    var handler = this.Handler;

                    if (handler == null)
                    {
                        ActivityUtilities.Add(
                            ref validationErrors,
                            new ValidationError(SR.ActivityDelegateCannotBeReferencedNoHandler(parent.DisplayName, referenceTarget.DisplayName, this.Owner.DisplayName),
                            false,
                            parent));
                    }
                    else
                    {
                        ActivityUtilities.Add(
                            ref validationErrors,
                            new ValidationError(SR.ActivityDelegateCannotBeReferenced(handler.DisplayName, parent.DisplayName, referenceTarget.DisplayName, this.Owner.DisplayName),
                            false,
                            parent));
                    }

                    return false;
                }

                // This is a valid reference so we want to allow normal processing to proceed.
                return true;
            }

            this.Owner = parent;
            this.cacheId = parent.CacheId;
            this.ParentCollectionType = collectionType;
            this.InternalCacheMetadata();

            // We need to setup the delegate environment so that it is available when we process the Handler.
            var delegateEnvironment = collectionType == ActivityCollectionType.Implementation ? parent.ImplementationEnvironment : parent.PublicEnvironment;
            if (this.RuntimeDelegateArguments.Count > 0)
            {
                var newEnvironment = new ActivityLocationReferenceEnvironment(delegateEnvironment);
                delegateEnvironment = newEnvironment;

                for (var argumentIndex = 0; argumentIndex < this.RuntimeDelegateArguments.Count; argumentIndex++)
                {
                    var runtimeDelegateArgument = this.RuntimeDelegateArguments[argumentIndex];
                    var delegateArgument = runtimeDelegateArgument.BoundArgument;

                    if (delegateArgument != null)
                    {
                        if (delegateArgument.Direction != runtimeDelegateArgument.Direction)
                        {
                            ActivityUtilities.Add(ref validationErrors, new ValidationError(SR.RuntimeDelegateArgumentDirectionIncorrect, parent));
                        }

                        if (delegateArgument.Type != runtimeDelegateArgument.Type)
                        {
                            ActivityUtilities.Add(ref validationErrors, new ValidationError(SR.RuntimeDelegateArgumentTypeIncorrect, parent));
                        }

                        // NOTE: We don't initialize this relationship here because at runtime we'll
                        // actually just place these variables in the environment of the Handler.
                        // We'll initialize and set an ID when we process the Handler.
                        newEnvironment.Declare(delegateArgument, this.Owner, ref validationErrors);
                    }
                }
            }

            this.Environment = delegateEnvironment;

            return this.Handler == null ? true : this.Handler.InitializeRelationship(this, collectionType, ref validationErrors);
        }

        /// <summary>
        /// Internals the cache metadata.
        /// </summary>
        internal void InternalCacheMetadata() =>
            this.delegateParameters = new ReadOnlyCollection<RuntimeDelegateArgument>(this.InternalGetRuntimeDelegateArguments());

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal virtual IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>();
            this.OnGetRuntimeDelegateArguments(result);
            return result;
        }

        /// <summary>
        /// Gets the result argument.
        /// </summary>
        /// <returns>DelegateOutArgument.</returns>
        protected internal virtual DelegateOutArgument GetResultArgument() => null;

        /// <summary>
        /// Called when [get runtime delegate arguments].
        /// </summary>
        /// <param name="runtimeDelegateArguments">The runtime delegate arguments.</param>
        /// <exception cref="ArgumentNullException">runtimeDelegateArguments</exception>
        protected virtual void OnGetRuntimeDelegateArguments(IList<RuntimeDelegateArgument> runtimeDelegateArguments)
        {
            if (runtimeDelegateArguments == null)
            {
                throw new ArgumentNullException(nameof(runtimeDelegateArguments));
            }

            foreach (PropertyDescriptor propertyDescriptor in TypeDescriptor.GetProperties(this))
            {
                if (ActivityUtilities.TryGetDelegateArgumentDirectionAndType(propertyDescriptor.PropertyType, out var direction, out var innerType))
                {
                    runtimeDelegateArguments.Add(
                        new RuntimeDelegateArgument(
                            propertyDescriptor.Name,
                            innerType,
                            direction,
                            (DelegateArgument)propertyDescriptor.GetValue(this)));
                }
            }
        }
    }
}
