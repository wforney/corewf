// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.DynamicUpdate
{
    using Portable.Xaml;

    public static class DynamicUpdateInfo
    {
        private static AttachableMemberIdentifier mapItemProperty = new AttachableMemberIdentifier(typeof(DynamicUpdateInfo), "MapItem");
        private static AttachableMemberIdentifier originalActivityBuilderProperty = new AttachableMemberIdentifier(typeof(DynamicUpdateInfo), "OriginalActivityBuilder");
        private static AttachableMemberIdentifier originalDefinitionProperty = new AttachableMemberIdentifier(typeof(DynamicUpdateInfo), "OriginalDefinition");

        public static DynamicUpdateMapItem GetMapItem(object instance)
        {
            if (AttachablePropertyServices.TryGetProperty(instance, mapItemProperty, out DynamicUpdateMapItem result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        public static ActivityBuilder GetOriginalActivityBuilder(object instance)
        {
            if (AttachablePropertyServices.TryGetProperty(instance, originalActivityBuilderProperty, out ActivityBuilder result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        public static Activity GetOriginalDefinition(object instance)
        {
            if (AttachablePropertyServices.TryGetProperty(instance, originalDefinitionProperty, out Activity result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        public static void SetMapItem(object instance, DynamicUpdateMapItem mapItem)
        {
            if (mapItem != null)
            {
                AttachablePropertyServices.SetProperty(instance, mapItemProperty, mapItem);
            }
            else
            {
                AttachablePropertyServices.RemoveProperty(instance, mapItemProperty);
            }
        }

        public static void SetOriginalActivityBuilder(object instance, ActivityBuilder originalActivityBuilder)
        {
            if (originalActivityBuilder != null)
            {
                AttachablePropertyServices.SetProperty(instance, originalActivityBuilderProperty, originalActivityBuilder);
            }
            else
            {
                AttachablePropertyServices.RemoveProperty(instance, originalActivityBuilderProperty);
            }
        }

        public static void SetOriginalDefinition(object instance, Activity originalDefinition)
        {
            if (originalDefinition != null)
            {
                AttachablePropertyServices.SetProperty(instance, originalDefinitionProperty, originalDefinition);
            }
            else
            {
                AttachablePropertyServices.RemoveProperty(instance, originalDefinitionProperty);
            }
        }
    }
}
