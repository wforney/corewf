// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.DynamicUpdate
{
    using System.Activities.Internals;
    using System.Collections.Generic;

    public class NativeActivityUpdateMapMetadata : UpdateMapMetadata
    {
        internal NativeActivityUpdateMapMetadata(DynamicUpdateMapBuilder.Finalizer finalizer, 
            DynamicUpdateMapBuilder.IDefinitionMatcher matcher, Activity targetActivity)
            : base(finalizer, matcher, targetActivity)
        {
        }

        public void SaveOriginalValue(Activity updatedChildActivity, object originalValue)
        {
            this.ThrowIfDisposed();
            ValidateOriginalValueAccess(this.TargetActivity, updatedChildActivity, nameof(updatedChildActivity), out var isReferencedChild);
            if (this.GetMatch(updatedChildActivity) == null)
            {
                throw FxTrace.Exception.Argument(nameof(updatedChildActivity), SR.CannotSaveOriginalValueForNewActivity(updatedChildActivity));
            }

            this.Finalizer.SetOriginalValue(updatedChildActivity, originalValue, isReferencedChild);
        }

        public void SaveOriginalValue(string propertyName, object originalValue)
        {
            this.ThrowIfDisposed();
            if (propertyName == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(propertyName));
            }

            if (this.Finalizer.SavedOriginalValuesForCurrentActivity == null)
            {
                this.Finalizer.SavedOriginalValuesForCurrentActivity = new Dictionary<string, object>();
            }
            this.Finalizer.SavedOriginalValuesForCurrentActivity[propertyName] = originalValue;
        }

        internal static void ValidateOriginalValueAccess(Activity parent, Activity child, string paramName, out bool isReferencedChild)
        {
            if (child == null)
            {
                throw FxTrace.Exception.ArgumentNull(paramName);
            }

            if (!IsPublicOrImportedDelegateOrChild(parent, child, out isReferencedChild))
            {
                throw FxTrace.Exception.Argument(paramName, SR.CannotSaveOriginalValueForActivity);
            }
        }

        private static bool IsPublicOrImportedDelegateOrChild(Activity parent, Activity child, out bool isReferencedChild)
        {
            isReferencedChild = false;
            if (child.Parent == parent)
            {
                if (child.HandlerOf == null)
                {
                    return child.RelationshipToParent == Activity.RelationshipType.Child || 
                        child.RelationshipToParent == Activity.RelationshipType.ImportedChild;
                }
                else
                {
                    return child.HandlerOf.ParentCollectionType == ActivityCollectionType.Public ||
                        child.HandlerOf.ParentCollectionType == ActivityCollectionType.Imports;
                }
            }
            else if (parent.MemberOf != child.MemberOf)
            {
                isReferencedChild = true;
                return IsChild(parent, child, out _);
            }
            else
            {
                return false;
            }
        }
    }
}
