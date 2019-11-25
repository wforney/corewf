// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.DurableInstancing;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.DurableInstancing;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading;
    using System.Transactions;
    using System.Xml.Linq;

    public sealed partial class WorkflowApplication
    {
        private class PersistenceManager : PersistenceManagerBase
        {
            private readonly bool isTryLoad;
            private readonly InstanceStore store;
            private bool aborted;
            private InstanceHandle handle;
            private Guid instanceId;

            // Initializing metadata, used when instance is created
            private IDictionary<XName, InstanceValue> instanceMetadata;

            private bool isLocked;

            // Updateable metadata, used when instance is saved
            private IDictionary<XName, InstanceValue> mutableMetadata;

            private InstanceOwner owner;
            private bool ownerWasCreated;
            private InstanceHandle temporaryHandle;

            public PersistenceManager(InstanceStore store, IDictionary<XName, InstanceValue> instanceMetadata, Guid instanceId)
            {
                Fx.Assert(store != null, "We should never gets here without a store.");

                this.instanceId = instanceId;
                this.instanceMetadata = instanceMetadata;

                this.InitializeInstanceMetadata();

                this.owner = store.DefaultInstanceOwner;
                if (this.owner != null)
                {
                    this.handle = store.CreateInstanceHandle(this.owner, instanceId);
                }

                this.store = store;
            }

            public PersistenceManager(InstanceStore store, IDictionary<XName, InstanceValue> instanceMetadata)
            {
                Fx.Assert(store != null, "We should never get here without a store.");

                this.isTryLoad = true;
                this.instanceMetadata = instanceMetadata;

                this.InitializeInstanceMetadata();

                this.owner = store.DefaultInstanceOwner;
                if (this.owner != null)
                {
                    this.handle = store.CreateInstanceHandle(this.owner);
                }

                this.store = store;
            }

            public sealed override Guid InstanceId => this.instanceId;

            public sealed override InstanceStore InstanceStore => this.store;

            public bool IsInitialized => (this.handle != null);

            public bool IsLocked => this.isLocked;

            public bool OwnerWasCreated => this.ownerWasCreated;

            public static Dictionary<XName, InstanceValue> GenerateInitialData(WorkflowApplication instance)
            {
                var data = new Dictionary<XName, InstanceValue>(10);
                data[WorkflowNamespace.Bookmarks] = new InstanceValue(instance.Controller.GetBookmarks(), InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                data[WorkflowNamespace.LastUpdate] = new InstanceValue(DateTime.UtcNow, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);

                foreach (var mappedVariable in instance.Controller.GetMappedVariables())
                {
                    data[WorkflowNamespace.VariablesPath.GetName(mappedVariable.Key)] = new InstanceValue(mappedVariable.Value, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                }

                Fx.AssertAndThrow(instance.Controller.State != WorkflowInstanceState.Aborted, "Cannot generate data for an aborted instance.");
                if (instance.Controller.State != WorkflowInstanceState.Complete)
                {
                    data[WorkflowNamespace.Workflow] = new InstanceValue(instance.Controller.PrepareForSerialization());
                    data[WorkflowNamespace.Status] = new InstanceValue(instance.Controller.State == WorkflowInstanceState.Idle ? "Idle" : "Executing", InstanceValueOptions.WriteOnly);
                }
                else
                {
                    data[WorkflowNamespace.Workflow] = new InstanceValue(instance.Controller.PrepareForSerialization(), InstanceValueOptions.Optional);
                    var completionState = instance.Controller.GetCompletionState(out var outputs, out var completionException);

                    if (completionState == ActivityInstanceState.Faulted)
                    {
                        data[WorkflowNamespace.Status] = new InstanceValue("Faulted", InstanceValueOptions.WriteOnly);
                        data[WorkflowNamespace.Exception] = new InstanceValue(completionException, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                    }
                    else if (completionState == ActivityInstanceState.Closed)
                    {
                        data[WorkflowNamespace.Status] = new InstanceValue("Closed", InstanceValueOptions.WriteOnly);
                        if (outputs != null)
                        {
                            foreach (var output in outputs)
                            {
                                data[WorkflowNamespace.OutputPath.GetName(output.Key)] = new InstanceValue(output.Value, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                            }
                        }
                    }
                    else
                    {
                        Fx.AssertAndThrow(completionState == ActivityInstanceState.Canceled, "Cannot be executing when WorkflowState was completed.");
                        data[WorkflowNamespace.Status] = new InstanceValue("Canceled", InstanceValueOptions.WriteOnly);
                    }
                }
                return data;
            }

            public void Abort()
            {
                this.aborted = true;

                // Make sure the setter of handle sees aborted, or v.v., or both.
                Thread.MemoryBarrier();

                var handle = this.handle;
                if (handle != null)
                {
                    handle.Free();
                }

                this.FreeTemporaryHandle();
            }

            public IAsyncResult BeginDeleteOwner(TimeSpan timeout, AsyncCallback callback, object state)
            {
                IAsyncResult result = null;
                try
                {
                    this.CreateTemporaryHandle(this.owner);
                    result = this.store.BeginExecute(this.temporaryHandle, new DeleteWorkflowOwnerCommand(), timeout, callback, state);
                }
                // Ignore some exceptions because DeleteWorkflowOwner is best effort.
                catch (InstancePersistenceCommandException) { }
                catch (InstanceOwnerException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    if (result == null)
                    {
                        this.FreeTemporaryHandle();
                    }
                }
                return result;
            }

            public IAsyncResult BeginEnsureReadyness(TimeSpan timeout, AsyncCallback callback, object state)
            {
                Fx.Assert(this.handle != null, "We should already be initialized by now");
                Fx.Assert(!this.IsLocked, "We are already ready for persistence; why are we being called?");
                Fx.Assert(!this.isTryLoad, "Should not be on an initial save path if we tried load.");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    return this.store.BeginExecute(this.handle, CreateSaveCommand(null, this.instanceMetadata, PersistenceOperation.Save), timeout, callback, state);
                }
            }

            public IAsyncResult BeginInitialize(WorkflowIdentity definitionIdentity, TimeSpan timeout, AsyncCallback callback, object state)
            {
                Fx.Assert(this.handle == null, "We are already initialized by now");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    IAsyncResult result = null;

                    try
                    {
                        this.CreateTemporaryHandle(null);
                        result = this.store.BeginExecute(this.temporaryHandle, GetCreateOwnerCommand(definitionIdentity), timeout, callback, state);
                    }
                    finally
                    {
                        // We've encountered an exception
                        if (result == null)
                        {
                            this.FreeTemporaryHandle();
                        }
                    }
                    return result;
                }
            }

            public IAsyncResult BeginLoad(TimeSpan timeout, AsyncCallback callback, object state) => this.store.BeginExecute(this.handle, new LoadWorkflowCommand(), timeout, callback, state);

            public IAsyncResult BeginSave(IDictionary<XName, InstanceValue> instance, PersistenceOperation operation, TimeSpan timeout, AsyncCallback callback, object state) => this.store.BeginExecute(this.handle, CreateSaveCommand(instance, (this.isLocked ? this.mutableMetadata : this.instanceMetadata), operation), timeout, callback, state);

            public IAsyncResult BeginTryLoad(TimeSpan timeout, AsyncCallback callback, object state) => this.store.BeginExecute(this.handle, new TryLoadRunnableWorkflowCommand(), timeout, callback, state);

            public IAsyncResult BeginUnlock(TimeSpan timeout, AsyncCallback callback, object state)
            {
                var saveCmd = new SaveWorkflowCommand()
                {
                    UnlockInstance = true,
                };

                return this.store.BeginExecute(this.handle, saveCmd, timeout, callback, state);
            }

            public void DeleteOwner(TimeSpan timeout)
            {
                try
                {
                    this.CreateTemporaryHandle(this.owner);
                    this.store.Execute(this.temporaryHandle, new DeleteWorkflowOwnerCommand(), timeout);
                }
                // Ignore some exceptions because DeleteWorkflowOwner is best effort.
                catch (InstancePersistenceCommandException) { }
                catch (InstanceOwnerException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    this.FreeTemporaryHandle();
                }
            }

            public void EndDeleteOwner(IAsyncResult result)
            {
                try
                {
                    this.store.EndExecute(result);
                }
                // Ignore some exceptions because DeleteWorkflowOwner is best effort.
                catch (InstancePersistenceCommandException) { }
                catch (InstanceOwnerException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    this.FreeTemporaryHandle();
                }
            }

            public void EndEnsureReadyness(IAsyncResult result)
            {
                this.store.EndExecute(result);
                this.isLocked = true;
            }

            public void EndInitialize(IAsyncResult result)
            {
                try
                {
                    this.owner = this.store.EndExecute(result).InstanceOwner;
                    this.ownerWasCreated = true;
                }
                finally
                {
                    this.FreeTemporaryHandle();
                }

                this.handle = this.isTryLoad ? this.store.CreateInstanceHandle(this.owner) : this.store.CreateInstanceHandle(this.owner, this.InstanceId);
                Thread.MemoryBarrier();
                if (this.aborted)
                {
                    this.handle.Free();
                }
            }

            public IDictionary<XName, InstanceValue> EndLoad(IAsyncResult result)
            {
                var view = this.store.EndExecute(result);
                this.isLocked = true;

                if (!this.handle.IsValid)
                {
                    throw FxTrace.Exception.AsError(new OperationCanceledException(SR.WorkflowInstanceAborted(this.InstanceId)));
                }

                return view.InstanceData;
            }

            public void EndSave(IAsyncResult result)
            {
                this.store.EndExecute(result);
                this.isLocked = true;
            }

            public bool EndTryLoad(IAsyncResult result, out IDictionary<XName, InstanceValue> data)
            {
                var view = this.store.EndExecute(result);
                return this.TryLoadHelper(view, out data);
            }

            public void EndUnlock(IAsyncResult result) => this.store.EndExecute(result);

            public void EnsureReadyness(TimeSpan timeout)
            {
                Fx.Assert(this.handle != null, "We should already be initialized by now");
                Fx.Assert(!this.IsLocked, "We are already ready for persistence; why are we being called?");
                Fx.Assert(!this.isTryLoad, "Should not be on an initial save path if we tried load.");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    this.store.Execute(this.handle, CreateSaveCommand(null, this.instanceMetadata, PersistenceOperation.Save), timeout);
                    this.isLocked = true;
                }
            }

            public void Initialize(WorkflowIdentity definitionIdentity, TimeSpan timeout)
            {
                Fx.Assert(this.handle == null, "We are already initialized by now");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    try
                    {
                        this.CreateTemporaryHandle(null);
                        this.owner = this.store.Execute(this.temporaryHandle, GetCreateOwnerCommand(definitionIdentity), timeout).InstanceOwner;
                        this.ownerWasCreated = true;
                    }
                    finally
                    {
                        this.FreeTemporaryHandle();
                    }

                    this.handle = this.isTryLoad ? this.store.CreateInstanceHandle(this.owner) : this.store.CreateInstanceHandle(this.owner, this.InstanceId);

                    Thread.MemoryBarrier();
                    if (this.aborted)
                    {
                        this.handle.Free();
                    }
                }
            }

            public IDictionary<XName, InstanceValue> Load(TimeSpan timeout)
            {
                var view = this.store.Execute(this.handle, new LoadWorkflowCommand(), timeout);
                this.isLocked = true;

                if (!this.handle.IsValid)
                {
                    throw FxTrace.Exception.AsError(new OperationCanceledException(SR.WorkflowInstanceAborted(this.InstanceId)));
                }

                return view.InstanceData;
            }

            public void Save(IDictionary<XName, InstanceValue> instance, PersistenceOperation operation, TimeSpan timeout)
            {
                this.store.Execute(this.handle, CreateSaveCommand(instance, (this.isLocked ? this.mutableMetadata : this.instanceMetadata), operation), timeout);
                this.isLocked = true;
            }

            public void SetInstanceMetadata(IDictionary<XName, InstanceValue> metadata)
            {
                Fx.Assert(this.instanceMetadata.Count == 1, "We should only have the default metadata from InitializeInstanceMetadata");
                if (metadata != null)
                {
                    this.instanceMetadata = metadata;
                    this.InitializeInstanceMetadata();
                }
            }

            public void SetMutablemetadata(IDictionary<XName, InstanceValue> metadata) => this.mutableMetadata = metadata;

            public bool TryLoad(TimeSpan timeout, out IDictionary<XName, InstanceValue> data)
            {
                var view = this.store.Execute(this.handle, new TryLoadRunnableWorkflowCommand(), timeout);
                return this.TryLoadHelper(view, out data);
            }

            public void Unlock(TimeSpan timeout)
            {
                var saveCmd = new SaveWorkflowCommand()
                {
                    UnlockInstance = true,
                };

                this.store.Execute(this.handle, saveCmd, timeout);
            }

            private static SaveWorkflowCommand CreateSaveCommand(IDictionary<XName, InstanceValue> instance, IDictionary<XName, InstanceValue> instanceMetadata, PersistenceOperation operation)
            {
                var saveCommand = new SaveWorkflowCommand()
                {
                    CompleteInstance = operation == PersistenceOperation.Complete,
                    UnlockInstance = operation != PersistenceOperation.Save,
                };

                if (instance != null)
                {
                    foreach (var value in instance)
                    {
                        saveCommand.InstanceData.Add(value);
                    }
                }

                if (instanceMetadata != null)
                {
                    foreach (var value in instanceMetadata)
                    {
                        saveCommand.InstanceMetadataChanges.Add(value);
                    }
                }

                return saveCommand;
            }

            private static InstancePersistenceCommand GetCreateOwnerCommand(WorkflowIdentity definitionIdentity)
            {
                // Technically, we only need to pass the owner identity when doing LoadRunnable.
                // However, if we create an instance with identity on a store that doesn't recognize
                // it, the identity metadata might be stored in a way which makes it unqueryable if
                // the store is later upgraded to support identity (e.g. SWIS 4.0 -> 4.5 upgrade).
                // So to be on the safe side, if we're using identity, we require the store to
                // explicitly support it.
                if (definitionIdentity != null)
                {
                    var result = new CreateWorkflowOwnerWithIdentityCommand();
                    if (!object.ReferenceEquals(definitionIdentity, WorkflowApplication.unknownIdentity))
                    {
                        result.InstanceOwnerMetadata.Add(Workflow45Namespace.DefinitionIdentities,
                            new InstanceValue(new Collection<WorkflowIdentity> { definitionIdentity }));
                    }
                    return result;
                }
                else
                {
                    return new CreateWorkflowOwnerCommand();
                }
            }

            private void CreateTemporaryHandle(InstanceOwner owner)
            {
                this.temporaryHandle = this.store.CreateInstanceHandle(owner);

                Thread.MemoryBarrier();

                if (this.aborted)
                {
                    this.FreeTemporaryHandle();
                }
            }

            private void FreeTemporaryHandle()
            {
                var handle = this.temporaryHandle;

                if (handle != null)
                {
                    handle.Free();
                }
            }

            private void InitializeInstanceMetadata()
            {
                if (this.instanceMetadata == null)
                {
                    this.instanceMetadata = new Dictionary<XName, InstanceValue>(1);
                }

                // We always set this key explicitly so that users can't override this metadata value
                this.instanceMetadata[PersistenceMetadataNamespace.InstanceType] = new InstanceValue(WorkflowNamespace.WorkflowHostType, InstanceValueOptions.WriteOnly);
            }

            private bool TryLoadHelper(InstanceView view, out IDictionary<XName, InstanceValue> data)
            {
                if (!view.IsBoundToLock)
                {
                    data = null;
                    return false;
                }
                this.instanceId = view.InstanceId;
                this.isLocked = true;

                if (!this.handle.IsValid)
                {
                    throw FxTrace.Exception.AsError(new OperationCanceledException(SR.WorkflowInstanceAborted(this.InstanceId)));
                }

                data = view.InstanceData;
                return true;
            }
        }
    }
}