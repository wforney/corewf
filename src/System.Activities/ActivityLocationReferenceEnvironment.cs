// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System.Collections.Generic;

    [Fx.Tag.XamlVisible(false)]
    internal sealed class ActivityLocationReferenceEnvironment : LocationReferenceEnvironment
    {
        private Dictionary<string, LocationReference> declarations;
        private List<LocationReference> unnamedDeclarations;

        public ActivityLocationReferenceEnvironment()
        {
        }

        public ActivityLocationReferenceEnvironment(LocationReferenceEnvironment parent)
        {
            this.Parent = parent;

            if (this.Parent != null)
            {
                this.InternalRoot = parent.Root;
            }
        }

        public override Activity Root
        {
            get
            {
                return this.InternalRoot;
            }
        }

        public Activity InternalRoot
        {
            get;
            set;
        }

        private Dictionary<string, LocationReference> Declarations
        {
            get
            {
                if (this.declarations == null)
                {
                    this.declarations = new Dictionary<string, LocationReference>();
                }

                return this.declarations;
            }
        }

        public override bool IsVisible(LocationReference locationReference)
        {
            if (locationReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(locationReference));
            }

            LocationReferenceEnvironment currentScope = this;

            while (currentScope != null)
            {

                if (currentScope is ActivityLocationReferenceEnvironment activityEnvironment)
                {
                    if (activityEnvironment.declarations != null)
                    {
                        foreach (var declaration in activityEnvironment.declarations.Values)
                        {
                            if (locationReference == declaration)
                            {
                                return true;
                            }
                        }
                    }

                    if (activityEnvironment.unnamedDeclarations != null)
                    {
                        for (var i = 0; i < activityEnvironment.unnamedDeclarations.Count; i++)
                        {
                            if (locationReference == activityEnvironment.unnamedDeclarations[i])
                            {
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    return currentScope.IsVisible(locationReference);
                }

                currentScope = currentScope.Parent;
            }

            return false;
        }

        public void Declare(LocationReference locationReference, Activity owner, ref IList<ValidationError> validationErrors)
        {
            Fx.Assert(locationReference != null, "Must not be null");

            if (locationReference.Name == null)
            {
                if (this.unnamedDeclarations == null)
                {
                    this.unnamedDeclarations = new List<LocationReference>();
                }

                this.unnamedDeclarations.Add(locationReference);
            }
            else
            {
                if (this.Declarations.ContainsKey(locationReference.Name))
                {
                    string id = null;

                    if (owner != null)
                    {
                        id = owner.Id;
                    }

                    var validationError = new ValidationError(SR.SymbolNamesMustBeUnique(locationReference.Name))
                    {
                        Source = owner,
                        Id = id
                    };

                    ActivityUtilities.Add(ref validationErrors, validationError);
                }
                else
                {
                    this.Declarations.Add(locationReference.Name, locationReference);
                }
            }
        }

        public override bool TryGetLocationReference(string name, out LocationReference result)
        {
            if (name == null)
            {
                // We don't allow null names in our LocationReferenceEnvironment but
                // a custom declared environment might.  We need to walk up
                // to the root and see if it chains to a
                // non-ActivityLocationReferenceEnvironment implementation
                var currentEnvironment = this.Parent;

                while (currentEnvironment is ActivityLocationReferenceEnvironment)
                {
                    currentEnvironment = currentEnvironment.Parent;
                }

                if (currentEnvironment != null)
                {
                    Fx.Assert(!(currentEnvironment is ActivityLocationReferenceEnvironment), "We must be at a non-ActivityLocationReferenceEnvironment implementation.");

                    return currentEnvironment.TryGetLocationReference(name, out result);
                }
            }
            else
            {
                if (this.declarations != null && this.declarations.TryGetValue(name, out result))
                {
                    return true;
                }

                var found = false;
                var currentEnvironment = this.Parent;
                LocationReferenceEnvironment rootEnvironment = this;

                // Loop through all of the ActivityLocationReferenceEnvironments we have chained together
                while (currentEnvironment != null && currentEnvironment is ActivityLocationReferenceEnvironment)
                {
                    var activityEnvironment = (ActivityLocationReferenceEnvironment)currentEnvironment;
                    if (activityEnvironment.declarations != null && activityEnvironment.declarations.TryGetValue(name, out result))
                    {
                        return true;
                    }

                    rootEnvironment = currentEnvironment;
                    currentEnvironment = currentEnvironment.Parent;
                }

                if (!found)
                {
                    if (currentEnvironment != null)
                    {
                        // Looks like we have a non-ActivityLocationReferenceEnvironment at the root
                        Fx.Assert(!(currentEnvironment is ActivityLocationReferenceEnvironment), "We should have some other host environment at this point.");
                        if (currentEnvironment.TryGetLocationReference(name, out result))
                        {
                            return true;
                        }
                    }
                }
            }

            result = null;
            return false;
        }

        public override IEnumerable<LocationReference> GetLocationReferences()
        {
            return this.Declarations.Values;
        }
    }
}
