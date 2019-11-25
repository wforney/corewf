// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Tracking;
    using System.Globalization;

    /// <summary>
    /// The ActivityContext class.
    /// </summary>
    [Fx.Tag.XamlVisible(false)]
    public class ActivityContext
    {
        /// <summary>
        /// The instance identifier
        /// </summary>
        private long instanceId;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityContext"/> class.
        /// </summary>
        /// <remarks>
        /// Used by subclasses that are pooled.
        /// </remarks>
        internal ActivityContext()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityContext"/> class.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="executor">The executor.</param>
        /// <remarks>
        /// these can only be created by the WF Runtime
        /// </remarks>
        internal ActivityContext(ActivityInstance instance, ActivityExecutor executor)
        {
            Fx.Assert(instance != null, "valid activity instance is required");

            this.CurrentInstance = instance;
            this.CurrentExecutor = executor;
            this.Activity = this.CurrentInstance.Activity;
            this.instanceId = instance.InternalId;
        }

        /// <summary>
        /// Gets the environment.
        /// </summary>
        /// <value>The environment.</value>
        internal LocationEnvironment Environment
        {
            get
            {
                this.ThrowIfDisposed();
                return this.CurrentInstance.Environment;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [allow chained environment access].
        /// </summary>
        /// <value><c>true</c> if [allow chained environment access]; otherwise, <c>false</c>.</value>
        internal bool AllowChainedEnvironmentAccess { get; set; }

        /// <summary>
        /// Gets the activity.
        /// </summary>
        /// <value>The activity.</value>
        internal Activity Activity { get; private set; }

        /// <summary>
        /// Gets the current instance.
        /// </summary>
        /// <value>The current instance.</value>
        internal ActivityInstance CurrentInstance { get; private set; }

        /// <summary>
        /// Gets the current executor.
        /// </summary>
        /// <value>The current executor.</value>
        internal ActivityExecutor CurrentExecutor { get; private set; }

        /// <summary>
        /// Gets the activity instance identifier.
        /// </summary>
        /// <value>The activity instance identifier.</value>
        public string ActivityInstanceId
        {
            get
            {
                this.ThrowIfDisposed();
                return this.instanceId.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Gets the workflow instance identifier.
        /// </summary>
        /// <value>The workflow instance identifier.</value>
        public Guid WorkflowInstanceId
        {
            get
            {
                this.ThrowIfDisposed();
                return this.CurrentExecutor.WorkflowInstanceId;
            }
        }

        /// <summary>
        /// Gets the data context.
        /// </summary>
        /// <value>The data context.</value>
        public WorkflowDataContext DataContext
        {
            get
            {
                this.ThrowIfDisposed();

                // Argument expressions don't have visbility into public variables at the same scope.
                // However fast-path expressions use the parent's ActivityInstance instead of
                // creating their own, so we need to give them a DataContext without variables
                var includeLocalVariables = !this.CurrentInstance.IsResolvingArguments;

                if (this.CurrentInstance.DataContext == null ||
                    this.CurrentInstance.DataContext.IncludesLocalVariables != includeLocalVariables)
                {
                    this.CurrentInstance.DataContext
                        = new WorkflowDataContext(this.CurrentExecutor, this.CurrentInstance, includeLocalVariables);
                }

                return this.CurrentInstance.DataContext;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        /// <value><c>true</c> if this instance is disposed; otherwise, <c>false</c>.</value>
        internal bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets the extension.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>T.</returns>
        public T GetExtension<T>()
            where T : class
        {
            this.ThrowIfDisposed();
            return this.CurrentExecutor.GetExtension<T>();
        }

        /// <summary>
        /// Gets the ignorable result location.
        /// </summary>
        /// <param name="resultArgument">The result argument.</param>
        /// <returns>Location.</returns>
        internal Location GetIgnorableResultLocation(RuntimeArgument resultArgument) =>
            this.CurrentExecutor.GetIgnorableResultLocation(resultArgument);

        /// <summary>
        /// Reinitializes the specified instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="executor">The executor.</param>
        internal void Reinitialize(ActivityInstance instance, ActivityExecutor executor) =>
            this.Reinitialize(instance, executor, instance.Activity, instance.InternalId);

        /// <summary>
        /// Reinitializes the specified instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="executor">The executor.</param>
        /// <param name="activity">The activity.</param>
        /// <param name="instanceId">The instance identifier.</param>
        internal void Reinitialize(ActivityInstance instance, ActivityExecutor executor, Activity activity, long instanceId)
        {
            this.IsDisposed = false;
            this.CurrentInstance = instance;
            this.CurrentExecutor = executor;
            this.Activity = activity;
            this.instanceId = instanceId;
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        /// <remarks>
        /// extra insurance against misuse (if someone stashes away the execution context to use later)
        /// </remarks>
        internal void Dispose()
        {
            this.IsDisposed = true;
            this.CurrentInstance = null;
            this.CurrentExecutor = null;
            this.Activity = null;
            this.instanceId = 0;
        }

        /// <summary>
        /// Disposes the data context.
        /// </summary>
        internal void DisposeDataContext()
        {
            if (this.CurrentInstance.DataContext != null)
            {
                this.CurrentInstance.DataContext.DisposeEnvironment();
                this.CurrentInstance.DataContext = null;
            }
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="locationReference">The location reference.</param>
        /// <returns>Location&lt;T&gt;.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public Location<T> GetLocation<T>(LocationReference locationReference)
        {
            this.ThrowIfDisposed();

            if (locationReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(locationReference));
            }

            var location = locationReference.GetLocation(this);

            if (location is Location<T> typedLocation)
            {
                return typedLocation;
            }
            else
            {
                Fx.Assert(location != null, "The contract of LocationReference is that GetLocation never returns null.");

                if (locationReference.Type == typeof(T))
                {
                    return new TypedLocationWrapper<T>(location);
                }
                else
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.LocationTypeMismatch(locationReference.Name, typeof(T), locationReference.Type)));
                }
            }
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="locationReference">The location reference.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public T GetValue<T>(LocationReference? locationReference)
        {
            this.ThrowIfDisposed();

            if (locationReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(locationReference));
            }

            return this.GetValueCore<T>(locationReference);
        }

        /// <summary>
        /// Gets the value core.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="locationReference">The location reference.</param>
        /// <returns>T.</returns>
        internal T GetValueCore<T>(LocationReference locationReference)
        {
            var location = locationReference.GetLocationForRead(this);

            if (location is Location<T> typedLocation)
            {
                // If we hit this path we can avoid boxing value types
                return typedLocation.Value;
            }
            else
            {
                Fx.Assert(location != null, "The contract of LocationReference is that GetLocation never returns null.");

                return TypeHelper.Convert<T>(location.Value);
            }
        }

        /// <summary>
        /// Sets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="locationReference">The location reference.</param>
        /// <param name="value">The value.</param>
        public void SetValue<T>(LocationReference? locationReference, T value)
        {
            this.ThrowIfDisposed();

            if (locationReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(locationReference));
            }

            this.SetValueCore(locationReference, value);
        }

        /// <summary>
        /// Sets the value core.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="locationReference">The location reference.</param>
        /// <param name="value">The value.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void SetValueCore<T>(LocationReference locationReference, T value)
        {
            var location = locationReference.GetLocationForWrite(this);

            if (location is Location<T> typedLocation)
            {
                // If we hit this path we can avoid boxing value types
                typedLocation.Value = value;
            }
            else
            {
                if (!TypeHelper.AreTypesCompatible(value, locationReference.Type))
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotSetValueToLocation(value != null ? value.GetType() : typeof(T), locationReference.Name, locationReference.Type)));
                }

                location.Value = value;
            }
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters, Justification = "Generic needed for type inference")]
        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="argument">The argument.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public T GetValue<T>(OutArgument<T> argument)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argument));
            }

            argument.ThrowIfNotInTree();

            return this.GetValueCore<T>(argument.RuntimeArgument);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters, Justification = "Generic needed for type inference")]
        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="argument">The argument.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public T GetValue<T>(InOutArgument<T> argument)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argument));
            }

            argument.ThrowIfNotInTree();

            return this.GetValueCore<T>(argument.RuntimeArgument);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters, Justification = "Generic needed for type inference")]
        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="argument">The argument.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public T GetValue<T>(InArgument<T> argument)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argument));
            }

            argument.ThrowIfNotInTree();

            return this.GetValueCore<T>(argument.RuntimeArgument);
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <returns>System.Object.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public object GetValue(Argument argument)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argument));
            }

            argument.ThrowIfNotInTree();

            return this.GetValueCore<object>(argument.RuntimeArgument);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters,
        //    Justification = "We explicitly provide a RuntimeArgument overload to avoid requiring the object type parameter.")]
        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <param name="runtimeArgument">The runtime argument.</param>
        /// <returns>System.Object.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public object GetValue(RuntimeArgument runtimeArgument)
        {
            this.ThrowIfDisposed();

            if (runtimeArgument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(runtimeArgument));
            }

            return this.GetValueCore<object>(runtimeArgument);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters, Justification = "Generic needed for type inference")]
        /// <summary>
        /// Sets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="argument">The argument.</param>
        /// <param name="value">The value.</param>
        public void SetValue<T>(OutArgument<T> argument, T value)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                // We want to shortcut if the argument is null
                return;
            }

            argument.ThrowIfNotInTree();

            this.SetValueCore(argument.RuntimeArgument, value);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters, Justification = "Generic needed for type inference")]
        /// <summary>
        /// Sets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="argument">The argument.</param>
        /// <param name="value">The value.</param>
        public void SetValue<T>(InOutArgument<T> argument, T value)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                // We want to shortcut if the argument is null
                return;
            }

            argument.ThrowIfNotInTree();

            this.SetValueCore(argument.RuntimeArgument, value);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters, Justification = "Generic needed for type inference")]
        /// <summary>
        /// Sets the value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="argument">The argument.</param>
        /// <param name="value">The value.</param>
        public void SetValue<T>(InArgument<T> argument, T value)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                // We want to shortcut if the argument is null
                return;
            }

            argument.ThrowIfNotInTree();

            this.SetValueCore(argument.RuntimeArgument, value);
        }

        /// <summary>
        /// Sets the value.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <param name="value">The value.</param>
        public void SetValue(Argument argument, object value)
        {
            this.ThrowIfDisposed();

            if (argument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argument));
            }

            argument.ThrowIfNotInTree();

            this.SetValueCore(argument.RuntimeArgument, value);
        }

        /// <summary>
        /// Tracks the core.
        /// </summary>
        /// <param name="record">The record.</param>
        internal void TrackCore(CustomTrackingRecord record)
        {
            Fx.Assert(!this.IsDisposed, "not usable if disposed");
            Fx.Assert(record != null, "expect non-null record");

            if (this.CurrentExecutor.ShouldTrack)
            {
                record.Activity = new ActivityInfo(this.CurrentInstance);
                record.InstanceId = this.WorkflowInstanceId;
                this.CurrentExecutor.AddTrackingRecord(record);
            }
        }

        /// <summary>
        /// Throws if disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        internal void ThrowIfDisposed()
        {
            if (this.IsDisposed)
            {
                throw FxTrace.Exception.AsError(
                    new ObjectDisposedException(this.GetType().FullName, SR.AECDisposed));
            }
        }
    }
}