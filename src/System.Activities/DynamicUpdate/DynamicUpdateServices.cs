// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.DynamicUpdate
{
    using Microsoft.VisualBasic.Activities;

    using Portable.Xaml;

    using System;
    using System.Activities;
    using System.Activities.Expressions;
    using System.Activities.Internals;
    using System.Collections.Generic;

    public static class DynamicUpdateServices
    {
        private static readonly AttachableMemberIdentifier implementationMapProperty =
            new AttachableMemberIdentifier(typeof(DynamicUpdateServices), "ImplementationMap");

        private static readonly Func<Activity, Exception> onInvalidActivityToBlockUpdate =
                    new Func<Activity, Exception>(OnInvalidActivityToBlockUpdate);

        private static readonly Func<Activity, Exception> onInvalidImplementationMapAssociation =
            new Func<Activity, Exception>(OnInvalidImplementationMapAssociation);

        public static DynamicUpdateMap CreateUpdateMap(Activity updatedWorkflowDefinition) => CreateUpdateMap(updatedWorkflowDefinition, null);

        public static DynamicUpdateMap CreateUpdateMap(Activity updatedWorkflowDefinition, IEnumerable<Activity> disallowUpdateInsideActivities) =>
            CreateUpdateMap(updatedWorkflowDefinition, disallowUpdateInsideActivities, out _);

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.AvoidOutParameters, Justification = "Approved Design. Need to return the map and the block list.")]
        public static DynamicUpdateMap CreateUpdateMap(Activity updatedWorkflowDefinition, IEnumerable<Activity> disallowUpdateInsideActivities, out IList<ActivityBlockingUpdate> activitiesBlockingUpdate)
        {
            if (updatedWorkflowDefinition == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(updatedWorkflowDefinition));
            }

            var originalDefinition = DynamicUpdateInfo.GetOriginalDefinition(updatedWorkflowDefinition);
            if (originalDefinition == null)
            {
                throw FxTrace.Exception.Argument(nameof(updatedWorkflowDefinition), SR.MustCallPrepareBeforeFinalize);
            }

            var result = InternalTryCreateUpdateMap(updatedWorkflowDefinition, originalDefinition, disallowUpdateInsideActivities, false, out activitiesBlockingUpdate);
            // Remove the DynamicUpdateMapItems now that the update is finalized Calling
            // CalculateMapItems is actually an unnecessary perf hit since it calls CacheMetadata
            // again; but we do it so that Finalize is implemented purely in terms of other public APIs.
            DynamicUpdateInfo.SetOriginalDefinition(updatedWorkflowDefinition, null);
            var mapItems = DynamicUpdateMap.CalculateMapItems(updatedWorkflowDefinition);
            foreach (var matchObject in mapItems.Keys)
            {
                DynamicUpdateInfo.SetMapItem(matchObject, null);
            }

            return result;
        }

        public static DynamicUpdateMap CreateUpdateMap(ActivityBuilder updatedActivityDefinition) => CreateUpdateMap(updatedActivityDefinition, null);

        public static DynamicUpdateMap CreateUpdateMap(ActivityBuilder updatedActivityDefinition, IEnumerable<Activity> disallowUpdateInsideActivities) =>
            CreateUpdateMap(updatedActivityDefinition, disallowUpdateInsideActivities, out _);

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.AvoidOutParameters, Justification = "Approved Design. Need to return the map and the block list.")]
        public static DynamicUpdateMap CreateUpdateMap(ActivityBuilder updatedActivityDefinition, IEnumerable<Activity> disallowUpdateInsideActivities, out IList<ActivityBlockingUpdate> activitiesBlockingUpdate)
        {
            if (updatedActivityDefinition == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(updatedActivityDefinition));
            }

            var originalActivityDefinition = DynamicUpdateInfo.GetOriginalActivityBuilder(updatedActivityDefinition);
            if (originalActivityDefinition == null)
            {
                throw FxTrace.Exception.Argument(nameof(updatedActivityDefinition), SR.MustCallPrepareBeforeFinalize);
            }

            Activity originalBuiltRoot = GetDynamicActivity(originalActivityDefinition);
            Activity updatedBuiltRoot = GetDynamicActivity(updatedActivityDefinition);

            var result = InternalTryCreateUpdateMap(updatedBuiltRoot, originalBuiltRoot, disallowUpdateInsideActivities, true, out activitiesBlockingUpdate);
            // Remove the DynamicUpdateMapItems now that the update is finalized Calling
            // CalculateMapItems is actually an unnecessary perf hit since it calls CacheMetadata
            // again; but we do it so that Finalize is implemented purely in terms of other public APIs.
            DynamicUpdateInfo.SetOriginalActivityBuilder(updatedActivityDefinition, null);
            var mapItems = DynamicUpdateMap.CalculateImplementationMapItems(updatedBuiltRoot);
            foreach (var matchObject in mapItems.Keys)
            {
                DynamicUpdateInfo.SetMapItem(matchObject, null);
            }

            return result;
        }

        public static DynamicUpdateMap GetImplementationMap(Activity targetActivity) =>
            AttachablePropertyServices.TryGetProperty(targetActivity, implementationMapProperty, out DynamicUpdateMap result)
                ? result
                : null;

        public static void PrepareForUpdate(Activity workflowDefinitionToBeUpdated)
        {
            if (workflowDefinitionToBeUpdated == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(workflowDefinitionToBeUpdated));
            }

            InternalPrepareForUpdate(workflowDefinitionToBeUpdated, false);
        }

        public static void PrepareForUpdate(ActivityBuilder activityDefinitionToBeUpdated)
        {
            if (activityDefinitionToBeUpdated == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(activityDefinitionToBeUpdated));
            }

            InternalPrepareForUpdate(activityDefinitionToBeUpdated, true);
        }

        public static void SetImplementationMap(Activity targetActivity, DynamicUpdateMap implementationMap)
        {
            if (implementationMap == null)
            {
                AttachablePropertyServices.RemoveProperty(targetActivity, implementationMapProperty);
            }
            else
            {
                AttachablePropertyServices.SetProperty(targetActivity, implementationMapProperty, implementationMap);
            }
        }

        private static DynamicActivity GetDynamicActivity(ActivityBuilder activityDefinition)
        {
            var result = new DynamicActivity
            {
                Name = activityDefinition.Name
            };
            foreach (var property in activityDefinition.Properties)
            {
                result.Properties.Add(property);
            }
            foreach (var attrib in activityDefinition.Attributes)
            {
                result.Attributes.Add(attrib);
            }
            foreach (var constraint in activityDefinition.Constraints)
            {
                result.Constraints.Add(constraint);
            }
            result.Implementation = () => activityDefinition.Implementation;

            var vbsettings = VisualBasic.GetSettings(activityDefinition);
            if (vbsettings != null)
            {
                VisualBasic.SetSettings(result, vbsettings);
            }

            var namespacesForImplementation = TextExpression.GetNamespacesForImplementation(activityDefinition);
            if (namespacesForImplementation.Count > 0)
            {
                TextExpression.SetNamespacesForImplementation(result, namespacesForImplementation);
            }

            var referencesForImplementation = TextExpression.GetReferencesForImplementation(activityDefinition);
            if (referencesForImplementation.Count > 0)
            {
                TextExpression.SetReferencesForImplementation(result, referencesForImplementation);
            }

            return result;
        }

        private static void InternalPrepareForUpdate(object definitionToBeUpdated, bool forImplementation)
        {
            // Clone the definition
            object clone;
            using (var reader = new XamlObjectReader(definitionToBeUpdated))
            {
                using var writer = new XamlObjectWriter(reader.SchemaContext);
                XamlServices.Transform(reader, writer);
                clone = writer.Result;
            }

            // Calculate the match info Set the match info as attached properties so it is
            // serializable, and available when the user calls CreateUpdateMap

            IDictionary<object, DynamicUpdateMapItem> mapItems;
            if (!forImplementation)
            {
                DynamicUpdateInfo.SetOriginalDefinition(definitionToBeUpdated, (Activity)clone);
                mapItems = DynamicUpdateMap.CalculateMapItems((Activity)definitionToBeUpdated);
            }
            else
            {
                DynamicUpdateInfo.SetOriginalActivityBuilder(definitionToBeUpdated, (ActivityBuilder)clone);
                mapItems = DynamicUpdateMap.CalculateImplementationMapItems(GetDynamicActivity((ActivityBuilder)definitionToBeUpdated));
            }

            foreach (var objectInfo in mapItems)
            {
                DynamicUpdateInfo.SetMapItem(objectInfo.Key, objectInfo.Value);
            }
        }

        private static DynamicUpdateMap InternalTryCreateUpdateMap(
            Activity updatedDefinition,
            Activity originalDefinition,
            IEnumerable<Activity> disallowUpdateInsideActivities,
            bool forImplementation,
            out IList<ActivityBlockingUpdate> activitiesBlockingUpdate)
        {
            var builder = new DynamicUpdateMapBuilder
            {
                ForImplementation = forImplementation,
                LookupMapItem = DynamicUpdateInfo.GetMapItem,
                LookupImplementationMap = GetImplementationMap,
                UpdatedWorkflowDefinition = updatedDefinition,
                OriginalWorkflowDefinition = originalDefinition,
                OnInvalidActivityToBlockUpdate = onInvalidActivityToBlockUpdate,
                OnInvalidImplementationMapAssociation = onInvalidImplementationMapAssociation,
            };
            if (disallowUpdateInsideActivities != null)
            {
                foreach (var activity in disallowUpdateInsideActivities)
                {
                    builder.DisallowUpdateInside.Add(activity);
                }
            }

            return builder.CreateMap(out activitiesBlockingUpdate);
        }

        [Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "<Pending>")]
        private static Exception OnInvalidActivityToBlockUpdate(Activity activity) =>
            new ArgumentException(SR.InvalidActivityToBlockUpdateServices(activity), "disallowUpdateInsideActivities");

        private static Exception OnInvalidImplementationMapAssociation(Activity activity) =>
            new InvalidOperationException(SR.InvalidImplementationMapAssociationServices(activity));
    }
}
