// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Tracking;

    /// <summary>
    /// The WorkflowInstanceExtensionManager class.
    /// </summary>
    /// <remarks>
    /// One workflow host should have one manager, and one manager should have one catalog.
    /// One workflow instance should have one container as the instance itself would be
    /// added as one extension to the container as well    
    /// </remarks>
    public class WorkflowInstanceExtensionManager
    {
        /// <summary>
        /// The empty extension providers
        /// </summary>
        /// <remarks>
        /// using an empty list instead of null simplifies our calculations immensely        
        /// </remarks>
        internal static List<KeyValuePair<Type, WorkflowInstanceExtensionProvider>> EmptyExtensionProviders = new List<KeyValuePair<Type, WorkflowInstanceExtensionProvider>>(0);
        internal static List<object> EmptySingletonExtensions = new List<object>(0);
        private bool isReadonly;
        private List<object>? additionalSingletonExtensions;
        private List<object>? allSingletonExtensions;
        private bool hasSingletonTrackingParticipant;
        private bool hasSingletonPersistenceModule;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstanceExtensionManager"/> class.
        /// </summary>
        public WorkflowInstanceExtensionManager()
        {
        }

        internal SymbolResolver? SymbolResolver { get; private set; }

        internal List<object>? SingletonExtensions { get; private set; }

        internal List<object>? AdditionalSingletonExtensions => this.additionalSingletonExtensions;

        internal List<KeyValuePair<Type, WorkflowInstanceExtensionProvider>>? ExtensionProviders { get; private set; }

        internal bool HasSingletonIWorkflowInstanceExtensions { get; private set; }

        internal bool HasSingletonTrackingParticipant => this.hasSingletonTrackingParticipant;

        internal bool HasSingletonPersistenceModule => this.hasSingletonPersistenceModule;

        internal bool HasAdditionalSingletonIWorkflowInstanceExtensions { get; private set; }

        /// <summary>
        /// Adds the specified singleton extension.
        /// </summary>
        /// <param name="singletonExtension">The singleton extension.</param>
        /// <remarks>
        /// use this method to add the singleton extension        
        /// </remarks>
        public virtual void Add(object singletonExtension)
        {
            if (singletonExtension == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(singletonExtension));
            }

            this.ThrowIfReadOnly();

            if (singletonExtension is SymbolResolver)
            {
                if (this.SymbolResolver != null)
                {
                    throw FxTrace.Exception.Argument(nameof(singletonExtension), SR.SymbolResolverAlreadyExists);
                }
                this.SymbolResolver = (SymbolResolver)singletonExtension;
            }
            else
            {
                if (singletonExtension is IWorkflowInstanceExtension)
                {
                    this.HasSingletonIWorkflowInstanceExtensions = true;
                }
                if (!this.HasSingletonTrackingParticipant && singletonExtension is TrackingParticipant)
                {
                    this.hasSingletonTrackingParticipant = true;
                }
                if (!this.HasSingletonPersistenceModule && singletonExtension is IPersistencePipelineModule)
                {
                    this.hasSingletonPersistenceModule = true;
                }
            }

            if (this.SingletonExtensions == null)
            {
                this.SingletonExtensions = new List<object>();
            }

            this.SingletonExtensions.Add(singletonExtension);
        }

        /// <summary>
        /// Adds the specified extension creation function.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="extensionCreationFunction">The extension creation function.</param>
        /// <remarks>
        /// use this method to add a per-instance extension        
        /// </remarks>
        public virtual void Add<T>(Func<T> extensionCreationFunction) where T : class
        {
            if (extensionCreationFunction == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(extensionCreationFunction));
            }
            this.ThrowIfReadOnly();

            if (this.ExtensionProviders == null)
            {
                this.ExtensionProviders = new List<KeyValuePair<Type, WorkflowInstanceExtensionProvider>>();
            }

            this.ExtensionProviders.Add(new KeyValuePair<Type, WorkflowInstanceExtensionProvider>(typeof(T), new WorkflowInstanceExtensionProvider<T>(extensionCreationFunction)));
        }

        internal List<object> GetAllSingletonExtensions()
        {
            return this.allSingletonExtensions;
        }

        internal void AddAllExtensionTypes(HashSet<Type> extensionTypes)
        {
            Fx.Assert(this.isReadonly, "should be read only at this point");
            for (var i = 0; i < this.SingletonExtensions.Count; i++)
            {
                extensionTypes.Add(this.SingletonExtensions[i].GetType());
            }
            for (var i = 0; i < this.ExtensionProviders.Count; i++)
            {
                extensionTypes.Add(this.ExtensionProviders[i].Key);
            }
        }

        internal static WorkflowInstanceExtensionCollection? CreateInstanceExtensions(Activity workflowDefinition, WorkflowInstanceExtensionManager extensionManager)
        {
            Fx.Assert(workflowDefinition.IsRuntimeReady, "activity should be ready with extensions after a successful CacheMetadata call");
            if (extensionManager != null)
            {
                extensionManager.MakeReadOnly();
                return new WorkflowInstanceExtensionCollection(workflowDefinition, extensionManager);
            }
            else if ((workflowDefinition.DefaultExtensionsCount > 0) || (workflowDefinition.RequiredExtensionTypesCount > 0))
            {
                return new WorkflowInstanceExtensionCollection(workflowDefinition, null);
            }
            else
            {
                return null;
            }
        }

        internal static void AddExtensionClosure(object newExtension, ref List<object>? targetCollection, ref bool addedTrackingParticipant, ref bool addedPersistenceModule)
        {
            // see if we need to process "additional" extensions
            if (!(newExtension is IWorkflowInstanceExtension currentInstanceExtension))
            {
                return; // bail early
            }

            Queue<IWorkflowInstanceExtension>? additionalInstanceExtensions = null;
            if (targetCollection == null)
            {
                targetCollection = new List<object>();
            }

            while (currentInstanceExtension != null)
            {
                var additionalExtensions = currentInstanceExtension.GetAdditionalExtensions();
                if (additionalExtensions != null)
                {
                    foreach (var additionalExtension in additionalExtensions)
                    {
                        targetCollection.Add(additionalExtension);
                        if (additionalExtension is IWorkflowInstanceExtension)
                        {
                            if (additionalInstanceExtensions == null)
                            {
                                additionalInstanceExtensions = new Queue<IWorkflowInstanceExtension>();
                            }
                            additionalInstanceExtensions.Enqueue((IWorkflowInstanceExtension)additionalExtension);
                        }
                        if (!addedTrackingParticipant && additionalExtension is TrackingParticipant)
                        {
                            addedTrackingParticipant = true;
                        }
                        if (!addedPersistenceModule && additionalExtension is IPersistencePipelineModule)
                        {
                            addedPersistenceModule = true;
                        }
                    }
                }

                if (additionalInstanceExtensions != null && additionalInstanceExtensions.Count > 0)
                {
                    currentInstanceExtension = additionalInstanceExtensions.Dequeue();
                }
                else
                {
                    currentInstanceExtension = null;
                }
            }
        }

        /// <summary>
        /// Makes the read only.
        /// </summary>
        public void MakeReadOnly()
        {
            // if any singleton extensions have dependents, calculate them now so that we're only
            // doing this process once per-host
            if (!this.isReadonly)
            {
                if (this.SingletonExtensions != null)
                {
                    if (this.HasSingletonIWorkflowInstanceExtensions)
                    {
                        foreach (var additionalExtensionProvider in this.SingletonExtensions.OfType<IWorkflowInstanceExtension>())
                        {
                            AddExtensionClosure(additionalExtensionProvider, ref this.additionalSingletonExtensions, ref this.hasSingletonTrackingParticipant, ref this.hasSingletonPersistenceModule);
                        }

                        if (this.AdditionalSingletonExtensions != null)
                        {
                            for (var i = 0; i < this.AdditionalSingletonExtensions.Count; i++)
                            {
                                var extension = this.AdditionalSingletonExtensions[i];
                                if (extension is IWorkflowInstanceExtension)
                                {
                                    this.HasAdditionalSingletonIWorkflowInstanceExtensions = true;
                                    break;
                                }
                            }
                        }
                    }

                    this.allSingletonExtensions = this.SingletonExtensions;
                    if (this.AdditionalSingletonExtensions != null && this.AdditionalSingletonExtensions.Count > 0)
                    {
                        this.allSingletonExtensions = new List<object>(this.SingletonExtensions);
                        this.allSingletonExtensions.AddRange(this.AdditionalSingletonExtensions);
                    }
                }
                else
                {
                    this.SingletonExtensions = EmptySingletonExtensions;
                    this.allSingletonExtensions = EmptySingletonExtensions;
                }

                if (this.ExtensionProviders == null)
                {
                    this.ExtensionProviders = EmptyExtensionProviders;
                }

                this.isReadonly = true;
            }
        }

        private void ThrowIfReadOnly()
        {
            if (this.isReadonly)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ExtensionsCannotBeModified));
            }
        }
    }
}
