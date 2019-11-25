// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Hosting
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Tracking;
    using System.Collections.Generic;
    using System.Linq;

    internal class WorkflowInstanceExtensionCollection
    {
        private readonly List<KeyValuePair<WorkflowInstanceExtensionProvider, object>>? instanceExtensions;
        private List<object>? additionalInstanceExtensions;
        private readonly List<object>? allSingletonExtensions;
        private bool hasTrackingParticipant;
        private bool hasPersistenceModule;
        private bool shouldSetInstanceForInstanceExtensions;

        /// <summary>
        /// cache for cases where we have a single match        
        /// </summary>
        private Dictionary<Type, object>? singleTypeCache;

        /// <summary>
        /// optimization for common extension in a loop/parallel (like Compensation or Send)        
        /// </summary>
        private Type? lastTypeCached;
        private object? lastObjectCached;

        /// <summary>
        /// temporary pointer to our parent manager between ctor and Initialize        
        /// </summary>
        private readonly WorkflowInstanceExtensionManager? extensionManager;

        internal WorkflowInstanceExtensionCollection(Activity workflowDefinition, WorkflowInstanceExtensionManager? extensionManager)
        {
            this.extensionManager = extensionManager;

            var extensionProviderCount = 0;
            if (extensionManager != null)
            {
                extensionProviderCount = extensionManager.ExtensionProviders?.Count ?? 0;
                this.hasTrackingParticipant = extensionManager.HasSingletonTrackingParticipant;
                this.hasPersistenceModule = extensionManager.HasSingletonPersistenceModule;

                // create an uber-IEnumerable to simplify our iteration code
                this.allSingletonExtensions = this.extensionManager?.GetAllSingletonExtensions();
            }
            else
            {
                this.allSingletonExtensions = WorkflowInstanceExtensionManager.EmptySingletonExtensions;
            }

            // Resolve activity extensions
            Dictionary<Type, WorkflowInstanceExtensionProvider>? filteredActivityExtensionProviders = null;
            if (workflowDefinition.GetActivityExtensionInformation(out var activityExtensionProviders, out var requiredActivityExtensionTypes))
            {
                // a) filter out the extension Types that were already configured by the host. Note that only "primary" extensions are in play here, not
                // "additional" extensions
                var allExtensionTypes = new HashSet<Type>();
                if (extensionManager != null)
                {
                    extensionManager.AddAllExtensionTypes(allExtensionTypes);
                }

                if (activityExtensionProviders != null)
                {
                    filteredActivityExtensionProviders = new Dictionary<Type, WorkflowInstanceExtensionProvider>(activityExtensionProviders.Count);
                    foreach (var keyedActivityExtensionProvider in activityExtensionProviders)
                    {
                        var newExtensionProviderType = keyedActivityExtensionProvider.Key;
                        if (!TypeHelper.ContainsCompatibleType(allExtensionTypes, newExtensionProviderType))
                        {
                            // first see if the new provider supersedes any existing ones
                            List<Type>? typesToRemove = null;
                            var skipNewExtensionProvider = false;
                            foreach (var existingExtensionProviderType in filteredActivityExtensionProviders.Keys)
                            {
                                // Use AreReferenceTypesCompatible for performance since we know that all of these must be reference types
                                if (TypeHelper.AreReferenceTypesCompatible(existingExtensionProviderType, newExtensionProviderType))
                                {
                                    skipNewExtensionProvider = true;
                                    break;
                                }

                                if (TypeHelper.AreReferenceTypesCompatible(newExtensionProviderType, existingExtensionProviderType))
                                {
                                    if (typesToRemove == null)
                                    {
                                        typesToRemove = new List<Type>();
                                    }
                                    typesToRemove.Add(existingExtensionProviderType);
                                }
                            }

                            // prune unnecessary extension providers (either superseded by the new extension or by an existing extension that supersedes them both)
                            if (typesToRemove != null)
                            {
                                for (var i = 0; i < typesToRemove.Count; i++)
                                {
                                    filteredActivityExtensionProviders.Remove(typesToRemove[i]);
                                }
                            }

                            // and add a new extension if necessary
                            if (!skipNewExtensionProvider)
                            {
                                filteredActivityExtensionProviders.Add(newExtensionProviderType, keyedActivityExtensionProvider.Value);
                            }
                        }
                    }
                    if (filteredActivityExtensionProviders.Count > 0)
                    {
                        allExtensionTypes.UnionWith(filteredActivityExtensionProviders.Keys);
                        extensionProviderCount += filteredActivityExtensionProviders.Count;
                    }
                }

                // b) Validate that all required extensions will be provided
                if (requiredActivityExtensionTypes != null && requiredActivityExtensionTypes.Count > 0)
                {
                    foreach (var requiredType in requiredActivityExtensionTypes)
                    {
                        if (!TypeHelper.ContainsCompatibleType(allExtensionTypes, requiredType))
                        {
                            throw FxTrace.Exception.AsError(new ValidationException(SR.RequiredExtensionTypeNotFound(requiredType.ToString())));
                        }
                    }
                }
            }

            // Finally, if our checks of passed, resolve our delegates
            if (extensionProviderCount > 0)
            {
                this.instanceExtensions = new List<KeyValuePair<WorkflowInstanceExtensionProvider, object>>(extensionProviderCount);

                if (extensionManager != null)
                {
                    var extensionProviders = extensionManager.ExtensionProviders;
                    if (extensionProviders != null)
                    {
                        for (var i = 0; i < (extensionProviders.Count); i++)
                        {
                            var extensionProvider = extensionProviders[i];
                            this.AddInstanceExtension(extensionProvider.Value);
                        }
                    }
                }

                if (filteredActivityExtensionProviders != null)
                {
                    foreach (var extensionProvider in filteredActivityExtensionProviders.Values)
                    {
                        this.AddInstanceExtension(extensionProvider);
                    }
                }
            }
        }

        private void AddInstanceExtension(WorkflowInstanceExtensionProvider extensionProvider)
        {
            Fx.Assert(this.instanceExtensions != null, "instanceExtensions should be setup by now");
            var newExtension = extensionProvider.ProvideValue();
            if (newExtension is SymbolResolver)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.SymbolResolverMustBeSingleton));
            }

            // for IWorkflowInstance we key off the type of the value, not the declared type
            if (!this.shouldSetInstanceForInstanceExtensions && newExtension is IWorkflowInstanceExtension)
            {
                this.shouldSetInstanceForInstanceExtensions = true;
            }
            if (!this.hasTrackingParticipant && extensionProvider.IsMatch<TrackingParticipant>(newExtension))
            {
                this.hasTrackingParticipant = true;
            }
            if (!this.hasPersistenceModule && extensionProvider.IsMatch<IPersistencePipelineModule>(newExtension))
            {
                this.hasPersistenceModule = true;
            }

            this.instanceExtensions?.Add(new KeyValuePair<WorkflowInstanceExtensionProvider, object>(extensionProvider, newExtension));

            WorkflowInstanceExtensionManager.AddExtensionClosure(newExtension, ref this.additionalInstanceExtensions, ref this.hasTrackingParticipant, ref this.hasPersistenceModule);
        }

        internal bool HasPersistenceModule => this.hasPersistenceModule;

        internal bool HasTrackingParticipant => this.hasTrackingParticipant;

        public bool HasWorkflowInstanceExtensions => this.WorkflowInstanceExtensions != null && this.WorkflowInstanceExtensions.Count > 0;

        public List<IWorkflowInstanceExtension>? WorkflowInstanceExtensions { get; private set; }

        internal void Initialize()
        {
            if (this.extensionManager != null)
            {
                // if we have any singleton IWorkflowInstanceExtensions, initialize them first
                // All validation logic for singletons is done through WorkflowInstanceExtensionManager
                if (this.extensionManager.HasSingletonIWorkflowInstanceExtensions && this.extensionManager.SingletonExtensions != null)
                {
                    this.SetInstance(this.extensionManager.SingletonExtensions);

                    if (this.extensionManager.HasAdditionalSingletonIWorkflowInstanceExtensions && this.extensionManager.AdditionalSingletonExtensions != null)
                    {
                        this.SetInstance(this.extensionManager.AdditionalSingletonExtensions);
                    }
                }
            }

            if (this.shouldSetInstanceForInstanceExtensions)
            {
                if (this.instanceExtensions != null)
                {
                    for (var i = 0; i < this.instanceExtensions.Count; i++)
                    {
                        var keyedExtension = this.instanceExtensions[i];
                        // for IWorkflowInstance we key off the type of the value, not the declared type

                        if (keyedExtension.Value is IWorkflowInstanceExtension workflowInstanceExtension)
                        {
                            if (this.WorkflowInstanceExtensions == null)
                            {
                                this.WorkflowInstanceExtensions = new List<IWorkflowInstanceExtension>();
                            }

                            this.WorkflowInstanceExtensions.Add(workflowInstanceExtension);
                        }
                    }
                }

                if (this.additionalInstanceExtensions != null)
                {
                    this.SetInstance(this.additionalInstanceExtensions);
                }
            }
        }

        private void SetInstance(List<object> extensionsList)
        {
            for (var i = 0; i < extensionsList.Count; i++)
            {
                var extension = extensionsList[i];
                if (extension is IWorkflowInstanceExtension)
                {
                    if (this.WorkflowInstanceExtensions == null)
                    {
                        this.WorkflowInstanceExtensions = new List<IWorkflowInstanceExtension>();
                    }

                    this.WorkflowInstanceExtensions.Add((IWorkflowInstanceExtension)extension);
                }
            }
        }

        public T? Find<T>()
            where T : class
        {
            T? result = null;

            if (this.TryGetCachedExtension(typeof(T), out var cachedExtension))
            {
                return cachedExtension as T;
            }

            try
            {
                // when we have support for context.GetExtensions<T>(),
                // then change from early break to ThrowOnMultipleMatches ("There are more than one matched extensions found
                // which is not allowed with GetExtension method call. Please use GetExtensions method instead.")
                if (this.allSingletonExtensions != null)
                {
                    for (var i = 0; i < this.allSingletonExtensions.Count; i++)
                    {
                        var extension = this.allSingletonExtensions[i];
                        result = extension as T;
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }

                if (this.instanceExtensions != null)
                {
                    for (var i = 0; i < this.instanceExtensions.Count; i++)
                    {
                        var keyedExtension = this.instanceExtensions[i];
                        if (keyedExtension.Key.IsMatch<T>(keyedExtension.Value))
                        {
                            result = (T)keyedExtension.Value;
                            return result;
                        }
                    }

                    if (this.additionalInstanceExtensions != null)
                    {
                        for (var i = 0; i < this.additionalInstanceExtensions.Count; i++)
                        {
                            var additionalExtension = this.additionalInstanceExtensions[i];
                            result = additionalExtension as T;
                            if (result != null)
                            {
                                return result;
                            }
                        }
                    }
                }

                return result;
            }
            finally
            {
                this.CacheExtension(result);
            }
        }

        public IEnumerable<T?> FindAll<T>()
            where T : class => this.FindAll<T>(false);

        private IEnumerable<T?> FindAll<T>(bool useObjectTypeForComparison)
            where T : class
        {
            // sometimes we match the single case even when you ask for multiple
            if (this.TryGetCachedExtension(typeof(T), out var cachedExtension))
            {
                yield return cachedExtension as T;
            }
            else
            {
                T? lastExtension = null;
                var hasMultiple = false;

                foreach (var extension in this.allSingletonExtensions.OfType<T>())
                {
                    if (lastExtension == null)
                    {
                        lastExtension = extension;
                    }
                    else
                    {
                        hasMultiple = true;
                    }

                    yield return extension;
                }

                foreach (var extension in this.GetInstanceExtensions<T>(useObjectTypeForComparison))
                {
                    if (lastExtension == null)
                    {
                        lastExtension = extension;
                    }
                    else
                    {
                        hasMultiple = true;
                    }

                    yield return extension;
                }

                if (!hasMultiple)
                {
                    this.CacheExtension(lastExtension);
                }
            }
        }

        private IEnumerable<T> GetInstanceExtensions<T>(bool useObjectTypeForComparison) where T : class
        {
            if (this.instanceExtensions != null)
            {
                for (var i = 0; i < this.instanceExtensions.Count; i++)
                {
                    var keyedExtension = this.instanceExtensions[i];
                    if ((useObjectTypeForComparison && keyedExtension.Value is T)
                        || keyedExtension.Key.IsMatch<T>(keyedExtension.Value))
                    {
                        yield return (T)keyedExtension.Value;
                    }
                }

                if (this.additionalInstanceExtensions != null)
                {
                    foreach (var additionalExtension in this.additionalInstanceExtensions)
                    {
                        if (additionalExtension is T)
                        {
                            yield return (T)additionalExtension;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            // we should only call dispose on instance extensions, since those are
            // the only ones we created
            foreach (var disposableExtension in this.GetInstanceExtensions<IDisposable>(true))
            {
                disposableExtension.Dispose();
            }
        }

        public void Cancel()
        {
            foreach (var cancelableExtension in this.GetInstanceExtensions<ICancelable>(true))
            {
                cancelableExtension.Cancel();
            }
        }

        private void CacheExtension<T>(T extension)
            where T : class?
        {
            if (extension != null)
            {
                this.CacheExtension(typeof(T), extension);
            }
        }

        private void CacheExtension(Type extensionType, object extension)
        {
            if (extension != null)
            {
                if (this.singleTypeCache == null)
                {
                    this.singleTypeCache = new Dictionary<Type, object>();
                }

                this.lastTypeCached = extensionType;
                this.lastObjectCached = extension;
                this.singleTypeCache[extensionType] = extension;
            }
        }

        private bool TryGetCachedExtension(Type type, out object? extension)
        {
            if (this.singleTypeCache == null)
            {
                extension = null;
                return false;
            }

            if (object.ReferenceEquals(type, this.lastTypeCached))
            {
                extension = this.lastObjectCached;
                return true;
            }

            return this.singleTypeCache.TryGetValue(type, out extension);
        }
    }
}
