// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Tracking
{
    using Portable.Xaml.Markup;

    using System.Collections.ObjectModel;
    using System.ComponentModel;

    [ContentProperty("Queries")]
    public class TrackingProfile
    {
        private Collection<TrackingQuery> queries;

        public TrackingProfile()
        {
        }

        [DefaultValue(null)]
        public string Name { get; set; }

        [DefaultValue(ImplementationVisibility.RootScope)]
        public ImplementationVisibility ImplementationVisibility { get; set; }

        [DefaultValue(null)]
        public string ActivityDefinitionId { get; set; }

        public Collection<TrackingQuery> Queries
        {
            get
            {
                if (this.queries == null)
                {
                    this.queries = new Collection<TrackingQuery>();
                }
                return this.queries;
            }
        }
    }
}
