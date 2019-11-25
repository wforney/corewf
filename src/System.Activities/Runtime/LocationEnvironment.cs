// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Activities.DynamicUpdate;
    using System.Activities.Internals;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    /// <summary>
    /// The LocationEnvironment class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.Runtime.ActivityInstanceMap.IActivityReferenceWithEnvironment" />
    /// </summary>
    /// <seealso cref="System.Activities.Runtime.ActivityInstanceMap.IActivityReferenceWithEnvironment" />
    [DataContract]
    internal sealed partial class LocationEnvironment : ActivityInstanceMap.IActivityReferenceWithEnvironment
    {
        private static readonly DummyLocation dummyLocation = new DummyLocation();
        private ActivityExecutor? executor;
        private bool isDisposed;
        private IList<LocationReference>? locationsToRegister;

        /// <summary>
        /// These two fields should be null unless we're in between calls to Update() and
        /// OnDeserialized(). Therefore they should never need to serialize.        
        /// </summary>
        private IList<Location>? locationsToUnregister;
        private Location? singleLocation;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocationEnvironment"/> class.
        /// </summary>
        internal LocationEnvironment()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocationEnvironment"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="capacity">The capacity.</param>
        /// <remarks>
        /// this ctor overload is to be exclusively used by DU for creating a LocationEnvironment for
        /// "noSymbols" ActivityInstance        
        /// </remarks>
        internal LocationEnvironment(LocationEnvironment? parent, int capacity)
            : this(null, null, parent, capacity)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocationEnvironment"/> class.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="definition">The definition.</param>
        internal LocationEnvironment(ActivityExecutor? executor, Activity? definition)
        {
            this.executor = executor;
            this.Definition = definition;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocationEnvironment"/> class.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="definition">The definition.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="capacity">The capacity.</param>
        internal LocationEnvironment(ActivityExecutor? executor, Activity? definition, LocationEnvironment? parent, int capacity)
            : this(executor, definition)
        {
            this.Parent = parent;

            Fx.Assert(capacity > 0, "must have a positive capacity if using this overload");
            if (capacity > 1)
            {
                this.SerializedLocations = new Location[capacity];
            }
        }

        /// <summary>
        /// Delegate GetNewVariableIndex
        /// </summary>
        /// <param name="oldIndex">The old index.</param>
        /// <returns>System.Nullable&lt;System.Int32&gt;.</returns>
        private delegate int? GetNewVariableIndex(int oldIndex);

        /// <summary>
        /// Gets the activity.
        /// </summary>
        /// <value>The activity.</value>
        Activity? ActivityInstanceMap.IActivityReference.Activity => this.Definition;

        /// <summary>
        /// Gets the definition.
        /// </summary>
        /// <value>The definition.</value>
        internal Activity? Definition { get; private set; }

        /// <summary>
        /// Gets the handles.
        /// </summary>
        /// <value>The handles.</value>
        /// <remarks>
        /// This list keeps track of handles that are created and initialized.        
        /// </remarks>
        internal List<Handle>? Handles { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance has handles.
        /// </summary>
        /// <value><c>true</c> if this instance has handles; otherwise, <c>false</c>.</value>
        internal bool HasHandles { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance has owner completed.
        /// </summary>
        /// <value><c>true</c> if this instance has owner completed; otherwise, <c>false</c>.</value>
        internal bool HasOwnerCompleted { get; private set; }

        /// <summary>
        /// Gets or sets the parent.
        /// </summary>
        /// <value>The parent.</value>
        internal LocationEnvironment? Parent { get; set; }

        /// <summary>
        /// Gets or sets the serialized handles.
        /// </summary>
        /// <value>The serialized handles.</value>
        [DataMember(EmitDefaultValue = false, Name = "handles")]
        internal List<Handle>? SerializedHandles
        {
            get => this.Handles;
            set => this.Handles = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether [serialized has mappable locations].
        /// </summary>
        /// <value><c>true</c> if [serialized has mappable locations]; otherwise, <c>false</c>.</value>
        [DataMember(EmitDefaultValue = false, Name = "hasMappableLocations")]
        internal bool SerializedHasMappableLocations { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [serialized has owner completed].
        /// </summary>
        /// <value><c>true</c> if [serialized has owner completed]; otherwise, <c>false</c>.</value>
        [DataMember(EmitDefaultValue = false, Name = "hasOwnerCompleted")]
        internal bool SerializedHasOwnerCompleted
        {
            get => this.HasOwnerCompleted;
            set => this.HasOwnerCompleted = value;
        }
        
        [DataMember(EmitDefaultValue = false, Name = "locations")]
        internal Location[]? SerializedLocations { get; set; }
        
        [DataMember(EmitDefaultValue = false, Name = "parent")]
        internal LocationEnvironment? SerializedParent
        {
            get => this.Parent;
            set => this.Parent = value;
        }

        /// <summary>
        /// Gets or sets the serialized reference count minus one.
        /// </summary>
        /// <value>The serialized reference count minus one.</value>
        /// <remarks>
        /// We store refCount - 1 because it is more likely to be zero and skipped by serialization        
        /// </remarks>
        [DataMember(EmitDefaultValue = false, Name = "referenceCountMinusOne")]
        internal int SerializedReferenceCountMinusOne { get; set; }
        
        [DataMember(EmitDefaultValue = false, Name = "singleLocation")]
        internal Location? SerializedSingleLocation
        {
            get => this.singleLocation;
            set => this.singleLocation = value;
        }

        internal bool ShouldDispose => this.SerializedReferenceCountMinusOne == -1;

        private MappableObjectManager? MappableObjectManager => this.executor?.MappableObjectManager;

        /// <summary>
        /// Loads.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="instanceMap">The instance map.</param>
        void ActivityInstanceMap.IActivityReference.Load(Activity activity, ActivityInstanceMap instanceMap) => this.Definition = activity;

        /// <summary>
        /// Updates the environment.
        /// </summary>
        /// <param name="map">The map.</param>
        /// <param name="activity">The activity.</param>
        void ActivityInstanceMap.IActivityReferenceWithEnvironment.UpdateEnvironment(EnvironmentUpdateMap map, Activity activity) =>
                    // LocationEnvironment.Update() is invoked through this path when this is a seondary
                    // root's environment(and in its parent chain) whose owner has already completed.
                    this.Update(map, activity);

        /// <summary>
        /// Adds the handle.
        /// </summary>
        /// <param name="handleToAdd">The handle to add.</param>
        internal void AddHandle(Handle handleToAdd)
        {
            if (this.Handles == null)
            {
                this.Handles = new List<Handle>();
            }
        
            this.Handles.Add(handleToAdd);
            this.HasHandles = true;
        }

        /// <summary>
        /// Note that the owner should never call this as the first AddReference is assumed        
        /// </summary>
        internal void AddReference() => this.SerializedReferenceCountMinusOne++;

        /// <summary>
        /// Called after an argument is added in Dynamic Update, when we need to collapse just one
        /// location rather than the whole environment        
        /// </summary>
        /// <param name="location">The location.</param>
        internal void CollapseTemporaryResolutionLocation(Location location)
        {
            // This assert doesn't necessarily imply that the location is still part of this
            // environment; it might have been removed in a subsequent update. If so, this method is
            // a no-op.
            Fx.Assert(location.TemporaryResolutionEnvironment == this, "Trying to collapse from the wrong environment");

            if (this.singleLocation == location)
            {
                this.CollapseTemporaryResolutionLocation(ref this.singleLocation);
            }
            else if (this.SerializedLocations != null)
            {
                for (var i = 0; i < this.SerializedLocations.Length; i++)
                {
                    if (this.SerializedLocations[i] == location)
                    {
                        this.CollapseTemporaryResolutionLocation(ref this.SerializedLocations[i]);
                    }
                }
            }
        }

        /// <summary>
        /// called for asynchronous argument resolution to collapse Location<Location<T>> to
        /// Location<T> in the environment        
        /// </summary>
        internal void CollapseTemporaryResolutionLocations()
        {
            if (this.SerializedLocations == null)
            {
                if (this.singleLocation != null &&
                    object.ReferenceEquals(this.singleLocation.TemporaryResolutionEnvironment, this))
                {
                    this.CollapseTemporaryResolutionLocation(ref this.singleLocation);
                }
            }
            else
            {
                for (var i = 0; i < this.SerializedLocations.Length; i++)
                {
                    var referenceLocation = this.SerializedLocations[i];

                    if (referenceLocation != null &&
                        object.ReferenceEquals(referenceLocation.TemporaryResolutionEnvironment, this))
                    {
                        this.CollapseTemporaryResolutionLocation(ref this.SerializedLocations[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Declares the specified location reference.
        /// </summary>
        /// <param name="locationReference">The location reference.</param>
        /// <param name="location">The location.</param>
        /// <param name="activityInstance">The activity instance.</param>
        internal void Declare(LocationReference locationReference, Location? location, ActivityInstance activityInstance)
        {
            Fx.Assert((locationReference.Id == 0 && this.SerializedLocations == null) || (locationReference.Id >= 0 && this.SerializedLocations != null && locationReference.Id < this.SerializedLocations.Length), "The environment should have been created with the appropriate capacity.");
            Fx.Assert(location != null, "");

            this.RegisterLocation(location, locationReference, activityInstance);

            if (this.SerializedLocations == null)
            {
                Fx.Assert(this.singleLocation == null, "We should not have had a single location if we are trying to declare one.");
                Fx.Assert(locationReference.Id == 0, "We should think the id is zero if we are setting the single location.");

                this.singleLocation = location;
            }
            else
            {
                Fx.Assert(this.SerializedLocations[locationReference.Id] == null || this.SerializedLocations[locationReference.Id] is DummyLocation, "We should not have had a location at the spot we are replacing.");

                this.SerializedLocations[locationReference.Id] = location;
            }
        }
        
        /// <summary>
        /// Declares the handle.
        /// </summary>
        /// <param name="locationReference">The location reference.</param>
        /// <param name="location">The location.</param>
        /// <param name="activityInstance">The activity instance.</param>
        internal void DeclareHandle(LocationReference locationReference, Location location, ActivityInstance activityInstance)
        {
            this.HasHandles = true;

            this.Declare(locationReference, location, activityInstance);
        }

        internal void DeclareTemporaryLocation<T>(LocationReference locationReference, ActivityInstance activityInstance, bool bufferGetsOnCollapse)
            where T : Location
        {
            Location locationToDeclare = new Location<T>();
            locationToDeclare.SetTemporaryResolutionData(this, bufferGetsOnCollapse);

            this.Declare(locationReference, locationToDeclare, activityInstance);
        }

        internal void Dispose()
        {
            Fx.Assert(this.ShouldDispose, "We shouldn't be calling Dispose when we have existing references.");
            Fx.Assert(!this.HasHandles, "We should have already uninitialized the handles and set our hasHandles variable to false.");
            Fx.Assert(!this.isDisposed, "We should not already be disposed.");

            this.isDisposed = true;

            this.CleanupMappedLocations();
        }

        internal Location<T>? GetSpecificLocation<T>(int id) => this.GetSpecificLocation(id) as Location<T>;

        internal Location? GetSpecificLocation(int id)
        {
            Fx.Assert(id >= 0 && ((this.SerializedLocations == null && id == 0) || (this.SerializedLocations != null && id < this.SerializedLocations.Length)), "Id needs to be within bounds.");

            if (this.SerializedLocations == null)
            {
                return this.singleLocation;
            }
            else
            {
                return this.SerializedLocations[id];
            }
        }

        internal void OnDeserialized(ActivityExecutor executor, ActivityInstance handleScope)
        {
            this.executor = executor;

            // The instance map Load might have already set the definition to the correct one. If
            // not then we assume the definition is the same as the handle scope.
            if (this.Definition == null)
            {
                this.Definition = handleScope.Activity;
            }

            this.ReinitializeHandles(handleScope);
            this.RegisterUpdatedLocations(handleScope);
        }

        internal void ReinitializeHandles(ActivityInstance handleScope)
        {
            // Need to reinitialize the handles in the list.
            if (this.Handles != null)
            {
                var count = this.Handles.Count;
                for (var i = 0; i < count; i++)
                {
                    this.Handles[i].Reinitialize(handleScope);
                    this.HasHandles = true;
                }
            }
        }

        internal void RemoveReference(bool isOwner)
        {
            if (isOwner)
            {
                this.HasOwnerCompleted = true;
            }

            Fx.Assert(this.SerializedReferenceCountMinusOne >= 0, "We must at least have 1 reference (0 for refCountMinusOne)");
            this.SerializedReferenceCountMinusOne--;
        }

        // Gets the location at this scope. The caller verifies that ref.owner == this.definition.
        internal bool TryGetLocation(int id, out Location value)
        {
            this.ThrowIfDisposed();

            value = null;

            if (this.SerializedLocations == null)
            {
                if (id == 0)
                {
                    value = this.singleLocation;
                }
            }
            else
            {
                if (this.SerializedLocations.Length > id)
                {
                    value = this.SerializedLocations[id];
                }
            }

            return value != null;
        }

        internal bool TryGetLocation(int id, Activity? environmentOwner, out Location? value)
        {
            this.ThrowIfDisposed();

            LocationEnvironment? targetEnvironment = this;

            while (targetEnvironment != null && targetEnvironment.Definition != environmentOwner)
            {
                targetEnvironment = targetEnvironment.Parent;
            }

            if (targetEnvironment == null)
            {
                value = null;
                return false;
            }

            value = null;

            if (id == 0 && targetEnvironment.SerializedLocations == null)
            {
                value = targetEnvironment.singleLocation;
            }
            else if (targetEnvironment.SerializedLocations != null && targetEnvironment.SerializedLocations.Length > id)
            {
                value = targetEnvironment.SerializedLocations[id];
            }

            return value != null;
        }

        internal void UninitializeHandles(ActivityInstance scope)
        {
            if (this.HasHandles)
            {
                HandleInitializationContext? context = null;

                try
                {
                    this.UninitializeHandles(scope, this.Definition?.RuntimeVariables, ref context);
                    this.UninitializeHandles(scope, this.Definition?.ImplementationVariables, ref context);

                    this.HasHandles = false;
                }
                finally
                {
                    if (context != null)
                    {
                        context.Dispose();
                    }
                }
            }
        }

        internal void Update(EnvironmentUpdateMap map, Activity activity)
        {
            // arguments public variables private variables RuntimeDelegateArguments Locations
            // array: AAAAAAAAAA VVVVVVVVVVVVVVVVVVVVVV PPPPPPPPPPPPPPPPPPP DDDDDDDDDDDDDDDDDDDDDDDDDDDDDD

            var actualRuntimeDelegateArgumentCount = activity.HandlerOf == null ? 0 : activity.HandlerOf.RuntimeDelegateArguments.Count;

            if (map.NewArgumentCount != activity.RuntimeArguments.Count ||
                map.NewVariableCount != activity.RuntimeVariables.Count ||
                map.NewPrivateVariableCount != activity.ImplementationVariables.Count ||
                map.RuntimeDelegateArgumentCount != actualRuntimeDelegateArgumentCount)
            {
                throw FxTrace.Exception.AsError(new InstanceUpdateException(SR.InvalidUpdateMap(
                    SR.WrongEnvironmentCount(activity, map.NewArgumentCount, map.NewVariableCount, map.NewPrivateVariableCount, map.RuntimeDelegateArgumentCount,
                        activity.RuntimeArguments.Count, activity.RuntimeVariables.Count, activity.ImplementationVariables.Count, actualRuntimeDelegateArgumentCount))));
            }

            var expectedLocationCount = map.OldArgumentCount + map.OldVariableCount + map.OldPrivateVariableCount + map.RuntimeDelegateArgumentCount;

            int actualLocationCount;
            if (this.SerializedLocations == null)
            {
                if (this.singleLocation == null)
                {
                    // we can hit this condition when the root activity instance has zero symbol.
                    actualLocationCount = 0;
                }
                else
                {
                    actualLocationCount = 1;

                    // temporarily normalize to locations array for the sake of environment update processing
                    this.SerializedLocations = new Location[] { this.singleLocation };
                    this.singleLocation = null;
                }
            }
            else
            {
                Fx.Assert(this.singleLocation == null, "locations and singleLocations cannot be non-null at the same time.");
                actualLocationCount = this.SerializedLocations.Length;
            }

            if (expectedLocationCount != actualLocationCount)
            {
                throw FxTrace.Exception.AsError(new InstanceUpdateException(SR.InvalidUpdateMap(
                    SR.WrongOriginalEnvironmentCount(activity, map.OldArgumentCount, map.OldVariableCount, map.OldPrivateVariableCount, map.RuntimeDelegateArgumentCount,
                        expectedLocationCount, actualLocationCount))));
            }

            Location[]? newLocations = null;

            // If newTotalLocations == 0, update will leave us with an empty LocationEnvironment,
            // which is something the runtime would normally never create. This is harmless, but it
            // is a loosening of normal invariants.
            var newTotalLocations = map.NewArgumentCount + map.NewVariableCount + map.NewPrivateVariableCount + map.RuntimeDelegateArgumentCount;
            if (newTotalLocations > 0)
            {
                newLocations = new Location[newTotalLocations];
            }

            this.UpdateArguments(map, newLocations);
            this.UnregisterRemovedVariables(map);
            this.UpdatePublicVariables(map, newLocations, activity);
            this.UpdatePrivateVariables(map, newLocations, activity);
            this.CopyRuntimeDelegateArguments(map, newLocations);

            Location? newSingleLocation = null;
            if (newTotalLocations == 1)
            {
                newSingleLocation = newLocations?[0];
                newLocations = null;
            }

            this.singleLocation = newSingleLocation;
            this.SerializedLocations = newLocations;
        }

        private void CleanupMappedLocations()
        {
            if (this.SerializedHasMappableLocations)
            {
                if (this.singleLocation != null)
                {
                    Fx.Assert(this.singleLocation.CanBeMapped, "Can only have mappable locations for a singleton if its mappable.");
                    this.UnregisterLocation(this.singleLocation);
                }
                else if (this.SerializedLocations != null)
                {
                    for (var i = 0; i < this.SerializedLocations.Length; i++)
                    {
                        var location = this.SerializedLocations[i];

                        if (location.CanBeMapped)
                        {
                            this.UnregisterLocation(location);
                        }
                    }
                }
            }
        }

        private void CollapseTemporaryResolutionLocation(ref Location location)
        {
            if (location.Value == null)
            {
                location = (Location)location.CreateDefaultValue();
            }
            else
            {
                location = ((Location)location.Value).CreateReference(location.BufferGetsOnCollapse);
            }
        }

        private void CopyRuntimeDelegateArguments(EnvironmentUpdateMap map, Location[] newLocations)
        {
            for (var i = 1; i <= map.RuntimeDelegateArgumentCount; i++)
            {
                newLocations[newLocations.Length - i] = this.SerializedLocations[this.SerializedLocations.Length - i];
            }
        }

        private void FindVariablesToUnregister(bool forImplementation, EnvironmentUpdateMap map, int oldVariableCount, int offset, ref bool hasMappableLocationsRemaining)
        {
            for (var i = 0; i < oldVariableCount; i++)
            {
                var location = this.SerializedLocations[i + offset];
                if (location.CanBeMapped)
                {
                    if ((forImplementation && map.GetNewPrivateVariableIndex(i).HasValue) || (!forImplementation && map.GetNewVariableIndex(i).HasValue))
                    {
                        hasMappableLocationsRemaining = true;
                    }
                    else
                    {
                        ActivityUtilities.Add(ref this.locationsToUnregister, location);
                    }
                }
            }
        }

        private void RegisterLocation(Location? location, LocationReference locationReference, ActivityInstance activityInstance)
        {
            if (location?.CanBeMapped ?? false)
            {
                this.SerializedHasMappableLocations = true;
                this.MappableObjectManager.Register(location, this.Definition, locationReference, activityInstance);
            }
        }

        private void RegisterUpdatedLocations(ActivityInstance activityInstance)
        {
            if (this.locationsToRegister != null)
            {
                foreach (var locationReference in this.locationsToRegister)
                {
                    this.RegisterLocation(this.GetSpecificLocation(locationReference.Id), locationReference, activityInstance);
                }
                this.locationsToRegister = null;
            }

            if (this.locationsToUnregister != null)
            {
                foreach (var location in this.locationsToUnregister)
                {
                    this.UnregisterLocation(location);
                }
                this.locationsToUnregister = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
            {
                throw FxTrace.Exception.AsError(
                    new ObjectDisposedException(this.GetType().FullName, SR.EnvironmentDisposed));
            }
        }

        private void UninitializeHandles(ActivityInstance scope, IList<Variable>? variables, ref HandleInitializationContext? context)
        {
            if (variables != null)
            {
                for (var i = 0; i < variables.Count; i++)
                {
                    var variable = variables[i];
                    Fx.Assert(variable.Owner == this.Definition, "We should only be targeting the vairables at this scope.");

                    if (variable.IsHandle)
                    {
                        var location = this.GetSpecificLocation(variable.Id);

                        if (location != null)
                        {
                            var handle = (Handle?)location.Value;

                            if (handle != null)
                            {
                                if (context == null)
                                {
                                    context = new HandleInitializationContext(this.executor, scope);
                                }

                                handle.Uninitialize(context);
                            }

                            location.Value = null;
                        }
                    }
                }
            }
        }

        private void UnregisterLocation(Location location) => this.MappableObjectManager.Unregister(location);

        private void UnregisterRemovedVariables(EnvironmentUpdateMap map)
        {
            var hasMappableLocationsRemaining = false;
            var offset = map.OldArgumentCount;

            this.FindVariablesToUnregister(false, map, map.OldVariableCount, offset, ref hasMappableLocationsRemaining);

            offset = map.OldArgumentCount + map.OldVariableCount;

            this.FindVariablesToUnregister(true, map, map.OldPrivateVariableCount, offset, ref hasMappableLocationsRemaining);

            this.SerializedHasMappableLocations = hasMappableLocationsRemaining;
        }

        private void UpdateArguments(EnvironmentUpdateMap map, Location?[]? newLocations)
        {
            if (map.HasArgumentEntries)
            {
                for (var i = 0; i < map.ArgumentEntries.Count; i++)
                {
                    var entry = map.ArgumentEntries[i];

                    Fx.Assert(entry.NewOffset >= 0 && entry.NewOffset < map.NewArgumentCount, "Argument offset is out of range");

                    if (entry.IsAddition)
                    {
                        // Location allocation will be performed later during
                        // ResolveDynamicallyAddedArguments(). for now, simply assign a dummy
                        // location so we know not to copy over the old value.
                        newLocations[entry.NewOffset] = dummyLocation;
                    }
                    else
                    {
                        Fx.Assert(this.SerializedLocations != null && this.singleLocation == null, "Caller should have copied singleLocation into locations array");
                        if (this.SerializedLocations == null)
                        {
                            throw new NullReferenceException("Caller should have copied singleLocation into locations array");
                        }

                        // rearrangement of existing arguments this entry here doesn't describe
                        // argument removal
                        newLocations[entry.NewOffset] = this.SerializedLocations[entry.OldOffset];
                    }
                }
            }

            // copy over unchanged Locations, and null out DummyLocations
            for (var i = 0; i < map.NewArgumentCount; i++)
            {
                if (newLocations?[i] == null)
                {
                    Fx.Assert(this.SerializedLocations != null && this.SerializedLocations.Length > i, "locations must be non-null and index i must be within the range of locations.");
                    newLocations[i] = this.SerializedLocations?[i];
                }
                else if (newLocations[i] == dummyLocation)
                {
                    newLocations[i] = null;
                }
            }
        }

        private void UpdatePrivateVariables(EnvironmentUpdateMap map, Location[] newLocations, Activity activity) => this.UpdateVariables(
                map.NewArgumentCount + map.NewVariableCount,
                map.OldArgumentCount + map.OldVariableCount,
                map.NewPrivateVariableCount,
                map.OldPrivateVariableCount,
                map.PrivateVariableEntries,
                activity.ImplementationVariables,
                newLocations);

        private void UpdatePublicVariables(EnvironmentUpdateMap map, Location[] newLocations, Activity activity) => this.UpdateVariables(
                map.NewArgumentCount,
                map.OldArgumentCount,
                map.NewVariableCount,
                map.OldVariableCount,
                map.VariableEntries,
                activity.RuntimeVariables,
                newLocations);

        private void UpdateVariables(int newVariablesOffset, int oldVariablesOffset, int newVariableCount, int oldVariableCount, IList<EnvironmentUpdateMapEntry> variableEntries, IList<Variable> variables, Location[] newLocations)
        {
            if (variableEntries != null)
            {
                for (var i = 0; i < variableEntries.Count; i++)
                {
                    var entry = variableEntries[i];

                    Fx.Assert(entry.NewOffset >= 0 && entry.NewOffset < newVariableCount, "Variable offset is out of range");
                    Fx.Assert(!entry.IsNewHandle, "This should have been caught in ActivityInstanceMap.UpdateRawInstance");

                    if (entry.IsAddition)
                    {
                        var newVariable = variables[entry.NewOffset];
                        var location = newVariable.CreateLocation();
                        newLocations[newVariablesOffset + entry.NewOffset] = location;
                        if (location.CanBeMapped)
                        {
                            ActivityUtilities.Add(ref this.locationsToRegister, newVariable);
                        }
                    }
                    else
                    {
                        Fx.Assert(this.SerializedLocations != null && this.singleLocation == null, "Caller should have copied singleLocation into locations array");

                        // rearrangement of existing variable this entry here doesn't describe
                        // variable removal
                        newLocations[newVariablesOffset + entry.NewOffset] = this.SerializedLocations[oldVariablesOffset + entry.OldOffset];
                    }
                }
            }

            // copy over unchanged variable Locations
            for (var i = 0; i < newVariableCount; i++)
            {
                if (newLocations[newVariablesOffset + i] == null)
                {
                    Fx.Assert(i < oldVariableCount, "New variable should have a location");
                    Fx.Assert(this.SerializedLocations != null && this.SerializedLocations.Length > oldVariablesOffset + i, "locations must be non-null and index i + oldVariableOffset must be within the range of locations.");

                    newLocations[newVariablesOffset + i] = this.SerializedLocations[oldVariablesOffset + i];
                }
            }
        }
    }
}
