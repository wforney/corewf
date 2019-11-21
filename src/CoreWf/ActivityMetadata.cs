// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Validation;
    using System.Collections.ObjectModel;

    /// <summary>
    /// The ActivityMetadata structure. Implements the <see
    /// cref="System.IEquatable{System.Activities.ActivityMetadata}" />
    /// </summary>
    /// <seealso cref="System.IEquatable{System.Activities.ActivityMetadata}" />
    public struct ActivityMetadata : IEquatable<ActivityMetadata>
    {
        /// <summary>
        /// The activity
        /// </summary>
        private Activity activity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityMetadata" /> struct.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="environment">The environment.</param>
        /// <param name="createEmptyBindings">if set to <c>true</c> [create empty bindings].</param>
        internal ActivityMetadata(Activity activity, LocationReferenceEnvironment environment, bool createEmptyBindings)
        {
            this.activity = activity;
            this.Environment = environment;
            this.CreateEmptyBindings = createEmptyBindings;
        }

        /// <summary>
        /// Gets the environment.
        /// </summary>
        /// <value>The environment.</value>
        public LocationReferenceEnvironment Environment { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has violations.
        /// </summary>
        /// <value><c>true</c> if this instance has violations; otherwise, <c>false</c>.</value>
        public bool HasViolations => this.activity == null ? false : this.activity.HasTempViolations;

        /// <summary>
        /// Gets a value indicating whether [create empty bindings].
        /// </summary>
        /// <value><c>true</c> if [create empty bindings]; otherwise, <c>false</c>.</value>
        internal bool CreateEmptyBindings { get; }

        /// <summary>
        /// Implements the != operator.
        /// </summary>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator !=(ActivityMetadata left, ActivityMetadata right) => !left.Equals(right);

        /// <summary>
        /// Implements the == operator.
        /// </summary>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator ==(ActivityMetadata left, ActivityMetadata right) => left.Equals(right);

        /// <summary>
        /// Adds the argument.
        /// </summary>
        /// <param name="argument">The argument.</param>
        public void AddArgument(RuntimeArgument argument)
        {
            this.ThrowIfDisposed();

            if (argument != null)
            {
                this.activity.AddArgument(argument, this.CreateEmptyBindings);
            }
        }

        /// <summary>
        /// Adds the default extension provider.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="extensionProvider">The extension provider.</param>
        public void AddDefaultExtensionProvider<T>(Func<T> extensionProvider)
            where T : class
        {
            if (extensionProvider == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(extensionProvider));
            }

            this.activity.AddDefaultExtensionProvider(extensionProvider);
        }

        /// <summary>
        /// Adds the imported child.
        /// </summary>
        /// <param name="importedChild">The imported child.</param>
        public void AddImportedChild(Activity importedChild) => this.AddImportedChild(importedChild, null);

        /// <summary>
        /// Adds the imported child.
        /// </summary>
        /// <param name="importedChild">The imported child.</param>
        /// <param name="origin">The origin.</param>
        public void AddImportedChild(Activity importedChild, object origin)
        {
            this.ThrowIfDisposed();
            ActivityUtilities.ValidateOrigin(origin, this.activity);

            if (importedChild != null)
            {
                this.activity.AddImportedChild(importedChild);
                if (importedChild.CacheId != this.activity.CacheId)
                {
                    importedChild.Origin = origin;
                }
            }
        }

        /// <summary>
        /// Adds the imported delegate.
        /// </summary>
        /// <param name="importedDelegate">The imported delegate.</param>
        public void AddImportedDelegate(ActivityDelegate importedDelegate) => this.AddImportedDelegate(importedDelegate, null);

        /// <summary>
        /// Adds the imported delegate.
        /// </summary>
        /// <param name="importedDelegate">The imported delegate.</param>
        /// <param name="origin">The origin.</param>
        public void AddImportedDelegate(ActivityDelegate importedDelegate, object origin)
        {
            this.ThrowIfDisposed();
            ActivityUtilities.ValidateOrigin(origin, this.activity);

            if (importedDelegate != null)
            {
                this.activity.AddImportedDelegate(importedDelegate);
                if (importedDelegate.Handler != null && importedDelegate.Handler.CacheId != this.activity.CacheId)
                {
                    importedDelegate.Handler.Origin = origin;
                }

                // We don't currently have ActivityDelegate.Origin. If we ever add it, or if we ever
                // expose Origin publicly, we need to also set it here.
            }
        }

        /// <summary>
        /// Adds the validation error.
        /// </summary>
        /// <param name="validationErrorMessage">The validation error message.</param>
        public void AddValidationError(string validationErrorMessage) => this.AddValidationError(new ValidationError(validationErrorMessage));

        /// <summary>
        /// Adds the validation error.
        /// </summary>
        /// <param name="validationError">The validation error.</param>
        public void AddValidationError(ValidationError validationError)
        {
            this.ThrowIfDisposed();

            if (validationError != null)
            {
                this.activity.AddTempValidationError(validationError);
            }
        }

        /// <summary>
        /// Adds the variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        public void AddVariable(Variable variable) => this.AddVariable(variable, null);

        /// <summary>
        /// Adds the variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <param name="origin">The origin.</param>
        public void AddVariable(Variable variable, object origin)
        {
            this.ThrowIfDisposed();
            ActivityUtilities.ValidateOrigin(origin, this.activity);

            if (variable != null)
            {
                this.activity.AddVariable(variable);
                if (variable.CacheId != this.activity.CacheId)
                {
                    variable.Origin = origin;
                    if (variable.Default != null && variable.Default.CacheId != this.activity.CacheId)
                    {
                        variable.Default.Origin = origin;
                    }
                }
            }
        }

        /// <summary>
        /// Binds the specified binding.
        /// </summary>
        /// <param name="binding">The binding.</param>
        /// <param name="argument">The argument.</param>
        public void Bind(Argument binding, RuntimeArgument argument)
        {
            this.ThrowIfDisposed();

            Argument.TryBind(binding, argument, this.activity);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns>
        /// <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance;
        /// otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (!(obj is ActivityMetadata))
            {
                return false;
            }

            var other = (ActivityMetadata)obj;
            return other.activity == this.activity && other.Environment == this.Environment
                && other.CreateEmptyBindings == this.CreateEmptyBindings;
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// <see langword="true" /> if the current object is equal to the <paramref name="other" />
        /// parameter; otherwise, <see langword="false" />.
        /// </returns>
        public bool Equals(ActivityMetadata other) =>
            other.activity == this.activity && other.Environment == this.Environment
                && other.CreateEmptyBindings == this.CreateEmptyBindings;

        /// <summary>
        /// Gets the arguments with reflection.
        /// </summary>
        /// <returns>Collection&lt;RuntimeArgument&gt;.</returns>
        public Collection<RuntimeArgument> GetArgumentsWithReflection() => Activity.ReflectedInformation.GetArguments(this.activity);

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data
        /// structures like a hash table.
        /// </returns>
        public override int GetHashCode() => this.activity == null ? 0 : this.activity.GetHashCode();

        /// <summary>
        /// Gets the imported children with reflection.
        /// </summary>
        /// <returns>Collection&lt;Activity&gt;.</returns>
        public Collection<Activity> GetImportedChildrenWithReflection() => Activity.ReflectedInformation.GetChildren(this.activity);

        /// <summary>
        /// Gets the imported delegates with reflection.
        /// </summary>
        /// <returns>Collection&lt;ActivityDelegate&gt;.</returns>
        public Collection<ActivityDelegate> GetImportedDelegatesWithReflection() => Activity.ReflectedInformation.GetDelegates(this.activity);

        /// <summary>
        /// Gets the variables with reflection.
        /// </summary>
        /// <returns>Collection&lt;Variable&gt;.</returns>
        public Collection<Variable> GetVariablesWithReflection() => Activity.ReflectedInformation.GetVariables(this.activity);

        /// <summary>
        /// Requires the extension.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RequireExtension<T>()
            where T : class => this.activity.RequireExtension(typeof(T));

        /// <summary>
        /// Requires the extension.
        /// </summary>
        /// <param name="extensionType">Type of the extension.</param>
        public void RequireExtension(Type extensionType)
        {
            if (extensionType == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(extensionType));
            }

            if (extensionType.IsValueType)
            {
                throw FxTrace.Exception.Argument(nameof(extensionType), SR.RequireExtensionOnlyAcceptsReferenceTypes(extensionType.FullName));
            }

            this.activity.RequireExtension(extensionType);
        }

        /// <summary>
        /// Sets the arguments collection.
        /// </summary>
        /// <param name="arguments">The arguments.</param>
        public void SetArgumentsCollection(Collection<RuntimeArgument> arguments)
        {
            this.ThrowIfDisposed();

            ActivityUtilities.RemoveNulls(arguments);

            this.activity.SetArgumentsCollection(arguments, this.CreateEmptyBindings);
        }

        /// <summary>
        /// Sets the imported children collection.
        /// </summary>
        /// <param name="importedChildren">The imported children.</param>
        public void SetImportedChildrenCollection(Collection<Activity> importedChildren)
        {
            this.ThrowIfDisposed();

            ActivityUtilities.RemoveNulls(importedChildren);

            this.activity.SetImportedChildrenCollection(importedChildren);
        }

        /// <summary>
        /// Sets the imported delegates collection.
        /// </summary>
        /// <param name="importedDelegates">The imported delegates.</param>
        public void SetImportedDelegatesCollection(Collection<ActivityDelegate> importedDelegates)
        {
            this.ThrowIfDisposed();

            ActivityUtilities.RemoveNulls(importedDelegates);

            this.activity.SetImportedDelegatesCollection(importedDelegates);
        }

        /// <summary>
        /// Sets the validation errors collection.
        /// </summary>
        /// <param name="validationErrors">The validation errors.</param>
        public void SetValidationErrorsCollection(Collection<ValidationError> validationErrors)
        {
            this.ThrowIfDisposed();

            ActivityUtilities.RemoveNulls(validationErrors);

            this.activity.SetTempValidationErrorCollection(validationErrors);
        }

        /// <summary>
        /// Sets the variables collection.
        /// </summary>
        /// <param name="variables">The variables.</param>
        public void SetVariablesCollection(Collection<Variable> variables)
        {
            this.ThrowIfDisposed();

            ActivityUtilities.RemoveNulls(variables);

            this.activity.SetVariablesCollection(variables);
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        internal void Dispose() => this.activity = null;

        /// <summary>
        /// Throws if disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfDisposed()
        {
            if (this.activity == null)
            {
                throw FxTrace.Exception.AsError(new ObjectDisposedException(this.ToString()));
            }
        }
    }
}
