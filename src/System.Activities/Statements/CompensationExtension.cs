// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Statements
{
    using System;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Persistence;
    using System.Activities.Runtime;
    using System.Collections.Generic;
    using System.Xml.Linq;

    /// <summary>
    /// The CompensationExtension class. Implements the <see
    /// cref="System.Activities.Persistence.PersistenceParticipant" /> Implements the <see
    /// cref="System.Activities.Hosting.IWorkflowInstanceExtension" />
    /// </summary>
    /// <seealso cref="System.Activities.Persistence.PersistenceParticipant" />
    /// <seealso cref="System.Activities.Hosting.IWorkflowInstanceExtension" />
    public class CompensationExtension : PersistenceParticipant, IWorkflowInstanceExtension
    {
        /// <summary>
        /// The compensation extension data
        /// </summary>
        private static readonly XName compensationExtensionData = compensationNamespace.GetName("Data");

        /// <summary>
        /// The compensation namespace
        /// </summary>
        private static readonly XNamespace compensationNamespace = XNamespace.Get("urn:schemas-microsoft-com:System.Activities/4.0/compensation");

        /// <summary>
        /// The compensation token table
        /// </summary>
        [Fx.Tag.SynchronizationObject(Blocking = false)]
        private Dictionary<long, CompensationTokenData> compensationTokenTable;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompensationExtension" /> class.
        /// </summary>
        public CompensationExtension() => this.compensationTokenTable = new Dictionary<long, CompensationTokenData>();

        /// <summary>
        /// Gets the compensation token table.
        /// </summary>
        /// <value>The compensation token table.</value>
        internal Dictionary<long, CompensationTokenData> CompensationTokenTable
        {
            get => this.compensationTokenTable;
            private set => this.compensationTokenTable = value;
        }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        internal long Id { get; set; }

        /// <summary>
        /// Gets the instance.
        /// </summary>
        /// <value>The instance.</value>
        internal WorkflowInstanceProxy Instance { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is workflow compensation behavior scheduled.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is workflow compensation behavior scheduled; otherwise, <c>false</c>.
        /// </value>
        internal bool IsWorkflowCompensationBehaviorScheduled { get; private set; }

        /// <summary>
        /// Gets or sets the workflow compensation.
        /// </summary>
        /// <value>The workflow compensation.</value>
        internal Bookmark WorkflowCompensation { get; set; }

        /// <summary>
        /// Gets the workflow compensation scheduled.
        /// </summary>
        /// <value>The workflow compensation scheduled.</value>
        internal Bookmark WorkflowCompensationScheduled { get; private set; }

        /// <summary>
        /// Gets or sets the workflow confirmation.
        /// </summary>
        /// <value>The workflow confirmation.</value>
        internal Bookmark WorkflowConfirmation { get; set; }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.InterfaceMethodsShouldBeCallableByChildTypes,
        //    Justification = "The inherit class don't need to call this method or access this method")]
        /// <summary>
        /// Gets the additional extensions.
        /// </summary>
        /// <returns>IEnumerable&lt;System.Object&gt;.</returns>
        IEnumerable<object> IWorkflowInstanceExtension.GetAdditionalExtensions() => null;

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.InterfaceMethodsShouldBeCallableByChildTypes,
        //    Justification = "The inherit class don't need to call this method or access this method")]
        /// <summary>
        /// Sets the instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        void IWorkflowInstanceExtension.SetInstance(WorkflowInstanceProxy instance) => this.Instance = instance;

        /// <summary>
        /// Adds the specified compensation identifier.
        /// </summary>
        /// <param name="compensationId">The compensation identifier.</param>
        /// <param name="compensationToken">The compensation token.</param>
        internal void Add(long compensationId, CompensationTokenData compensationToken)
        {
            Fx.Assert(compensationToken != null, "compensationToken must be valid");

            this.CompensationTokenTable[compensationId] = compensationToken;
        }

        /// <summary>
        /// Finds the bookmark.
        /// </summary>
        /// <param name="compensationId">The compensation identifier.</param>
        /// <param name="bookmarkName">Name of the bookmark.</param>
        /// <returns>Bookmark.</returns>
        internal Bookmark FindBookmark(long compensationId, CompensationBookmarkName bookmarkName)
        {
            Bookmark bookmark = null;

            if (this.CompensationTokenTable.TryGetValue(compensationId, out var compensationToken))
            {
                bookmark = compensationToken.BookmarkTable[bookmarkName];
            }

            return bookmark;
        }

        /// <summary>
        /// Gets the specified compensation identifier.
        /// </summary>
        /// <param name="compensationId">The compensation identifier.</param>
        /// <returns>CompensationTokenData.</returns>
        internal CompensationTokenData Get(long compensationId)
        {
            this.CompensationTokenTable.TryGetValue(compensationId, out var compensationToken);
            return compensationToken;
        }

        /// <summary>
        /// Gets the next identifier.
        /// </summary>
        /// <returns>System.Int64.</returns>
        internal long GetNextId() => ++this.Id;

        /// <summary>
        /// Notifies the message.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="compensationId">The compensation identifier.</param>
        /// <param name="compensationBookmark">The compensation bookmark.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void NotifyMessage(NativeActivityContext context, long compensationId, CompensationBookmarkName compensationBookmark)
        {
            var bookmark = this.FindBookmark(compensationId, compensationBookmark);
            if (bookmark == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.BookmarkNotRegistered(compensationBookmark)));
            }
            else
            {
                context.ResumeBookmark(bookmark, compensationId);
            }
        }

        /// <summary>
        /// Removes the specified compensation identifier.
        /// </summary>
        /// <param name="compensationId">The compensation identifier.</param>
        internal void Remove(long compensationId) => this.CompensationTokenTable.Remove(compensationId);

        /// <summary>
        /// Setups the workflow compensation behavior.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="workflowCompensationBehavior">The workflow compensation behavior.</param>
        internal void SetupWorkflowCompensationBehavior(NativeActivityContext context, BookmarkCallback callback, Activity workflowCompensationBehavior)
        {
            this.WorkflowCompensationScheduled = context.CreateBookmark(callback);

            Fx.Assert(workflowCompensationBehavior != null, "WorkflowCompensationBehavior must be valid");
            context.ScheduleSecondaryRoot(workflowCompensationBehavior, null);

            // Add the root compensationToken to track all root CA execution order.
            this.Add(CompensationToken.RootCompensationId, new CompensationTokenData(CompensationToken.RootCompensationId, CompensationToken.RootCompensationId));
            this.IsWorkflowCompensationBehaviorScheduled = true;
        }

        // PersistenceParticipant
        /// <summary>
        /// Collects the values.
        /// </summary>
        /// <param name="readWriteValues">The read write values.</param>
        /// <param name="writeOnlyValues">The write only values.</param>
        protected override void CollectValues(out IDictionary<XName, object> readWriteValues, out IDictionary<XName, object> writeOnlyValues)
        {
            writeOnlyValues = null;
            readWriteValues = new Dictionary<XName, object>(1)
            {
                {
                    compensationExtensionData,
                    new List<object>(6)
                    {
                        this.CompensationTokenTable,
                        this.WorkflowCompensation,
                        this.WorkflowConfirmation,
                        this.WorkflowCompensationScheduled,
                        this.IsWorkflowCompensationBehaviorScheduled,
                        this.Id
                    }
                }
            };
        }

        /// <summary>
        /// Publishes the values.
        /// </summary>
        /// <param name="readWriteValues">The read write values.</param>
        /// <exception cref="ArgumentNullException">readWriteValues</exception>
        protected override void PublishValues(IDictionary<XName, object> readWriteValues)
        {
            if (readWriteValues == null)
            {
                throw new ArgumentNullException(nameof(readWriteValues));
            }

            if (readWriteValues.TryGetValue(compensationExtensionData, out var data))
            {
                var list = (List<object>)data;
                this.CompensationTokenTable = (Dictionary<long, CompensationTokenData>)list[0];
                this.WorkflowCompensation = (Bookmark)list[1];
                this.WorkflowConfirmation = (Bookmark)list[2];
                this.WorkflowCompensationScheduled = (Bookmark)list[3];
                this.IsWorkflowCompensationBehaviorScheduled = (bool)list[4];
                this.Id = (long)list[5];
            }
        }
    }
}
