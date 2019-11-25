// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.ComponentModel;
    using System.Collections.Generic;

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction : ActivityDelegate
    {
        /// <summary>
        /// The empty delegate parameters
        /// </summary>
        private static readonly IList<RuntimeDelegateArgument> EmptyDelegateParameters = new List<RuntimeDelegateArgument>(0);

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments() => ActivityAction.EmptyDelegateParameters;
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument.
        /// </summary>
        /// <value>The argument.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T> Argument { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(1)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.ArgumentName, typeof(T), ArgumentDirection.In, this.Argument) }
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(2)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) }
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(3)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) }
            };
            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(4)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) }
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(5)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) }
            };
            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(6)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        [DefaultValue(null)]
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(7)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
            };
            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(8)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
            };
            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(9)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
            };
            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(10)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <typeparam name="T11">The type of the T11.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Gets or sets the argument11.
        /// </summary>
        /// <value>The argument11.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T11> Argument11 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(11)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument11Name, typeof(T11), ArgumentDirection.In, this.Argument11) },
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <typeparam name="T11">The type of the T11.</typeparam>
    /// <typeparam name="T12">The type of the T12.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Gets or sets the argument11.
        /// </summary>
        /// <value>The argument11.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T11> Argument11 { get; set; }

        /// <summary>
        /// Gets or sets the argument12.
        /// </summary>
        /// <value>The argument12.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T12> Argument12 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(12)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument11Name, typeof(T11), ArgumentDirection.In, this.Argument11) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument12Name, typeof(T12), ArgumentDirection.In, this.Argument12) },
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <typeparam name="T11">The type of the T11.</typeparam>
    /// <typeparam name="T12">The type of the T12.</typeparam>
    /// <typeparam name="T13">The type of the T13.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Gets or sets the argument11.
        /// </summary>
        /// <value>The argument11.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T11> Argument11 { get; set; }

        /// <summary>
        /// Gets or sets the argument12.
        /// </summary>
        /// <value>The argument12.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T12> Argument12 { get; set; }

        /// <summary>
        /// Gets or sets the argument13.
        /// </summary>
        /// <value>The argument13.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T13> Argument13 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(13)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument11Name, typeof(T11), ArgumentDirection.In, this.Argument11) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument12Name, typeof(T12), ArgumentDirection.In, this.Argument12) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument13Name, typeof(T13), ArgumentDirection.In, this.Argument13) }
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <typeparam name="T11">The type of the T11.</typeparam>
    /// <typeparam name="T12">The type of the T12.</typeparam>
    /// <typeparam name="T13">The type of the T13.</typeparam>
    /// <typeparam name="T14">The type of the T14.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Gets or sets the argument11.
        /// </summary>
        /// <value>The argument11.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T11> Argument11 { get; set; }

        /// <summary>
        /// Gets or sets the argument12.
        /// </summary>
        /// <value>The argument12.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T12> Argument12 { get; set; }

        /// <summary>
        /// Gets or sets the argument13.
        /// </summary>
        /// <value>The argument13.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T13> Argument13 { get; set; }

        /// <summary>
        /// Gets or sets the argument14.
        /// </summary>
        /// <value>The argument14.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T14> Argument14 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(14)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument11Name, typeof(T11), ArgumentDirection.In, this.Argument11) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument12Name, typeof(T12), ArgumentDirection.In, this.Argument12) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument13Name, typeof(T13), ArgumentDirection.In, this.Argument13) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument14Name, typeof(T14), ArgumentDirection.In, this.Argument14) }
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <typeparam name="T11">The type of the T11.</typeparam>
    /// <typeparam name="T12">The type of the T12.</typeparam>
    /// <typeparam name="T13">The type of the T13.</typeparam>
    /// <typeparam name="T14">The type of the T14.</typeparam>
    /// <typeparam name="T15">The type of the T15.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Gets or sets the argument11.
        /// </summary>
        /// <value>The argument11.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T11> Argument11 { get; set; }

        /// <summary>
        /// Gets or sets the argument12.
        /// </summary>
        /// <value>The argument12.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T12> Argument12 { get; set; }

        /// <summary>
        /// Gets or sets the argument13.
        /// </summary>
        /// <value>The argument13.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T13> Argument13 { get; set; }

        /// <summary>
        /// Gets or sets the argument14.
        /// </summary>
        /// <value>The argument14.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T14> Argument14 { get; set; }

        /// <summary>
        /// Gets or sets the argument15.
        /// </summary>
        /// <value>The argument15.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T15> Argument15 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(15)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument11Name, typeof(T11), ArgumentDirection.In, this.Argument11) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument12Name, typeof(T12), ArgumentDirection.In, this.Argument12) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument13Name, typeof(T13), ArgumentDirection.In, this.Argument13) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument14Name, typeof(T14), ArgumentDirection.In, this.Argument14) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument15Name, typeof(T15), ArgumentDirection.In, this.Argument15) }
            };

            return result;
        }
    }

    /// <summary>
    /// The ActivityAction class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.ActivityDelegate" />
    /// </summary>
    /// <typeparam name="T1">The type of the t1.</typeparam>
    /// <typeparam name="T2">The type of the t2.</typeparam>
    /// <typeparam name="T3">The type of the t3.</typeparam>
    /// <typeparam name="T4">The type of the t4.</typeparam>
    /// <typeparam name="T5">The type of the t5.</typeparam>
    /// <typeparam name="T6">The type of the t6.</typeparam>
    /// <typeparam name="T7">The type of the t7.</typeparam>
    /// <typeparam name="T8">The type of the t8.</typeparam>
    /// <typeparam name="T9">The type of the t9.</typeparam>
    /// <typeparam name="T10">The type of the T10.</typeparam>
    /// <typeparam name="T11">The type of the T11.</typeparam>
    /// <typeparam name="T12">The type of the T12.</typeparam>
    /// <typeparam name="T13">The type of the T13.</typeparam>
    /// <typeparam name="T14">The type of the T14.</typeparam>
    /// <typeparam name="T15">The type of the T15.</typeparam>
    /// <typeparam name="T16">The type of the T16.</typeparam>
    /// <seealso cref="System.Activities.ActivityDelegate" />
    public sealed class ActivityAction<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : ActivityDelegate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityAction{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16}"/> class.
        /// </summary>
        public ActivityAction()
        {
        }

        /// <summary>
        /// Gets or sets the argument1.
        /// </summary>
        /// <value>The argument1.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T1> Argument1 { get; set; }

        /// <summary>
        /// Gets or sets the argument2.
        /// </summary>
        /// <value>The argument2.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T2> Argument2 { get; set; }

        /// <summary>
        /// Gets or sets the argument3.
        /// </summary>
        /// <value>The argument3.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T3> Argument3 { get; set; }

        /// <summary>
        /// Gets or sets the argument4.
        /// </summary>
        /// <value>The argument4.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T4> Argument4 { get; set; }

        /// <summary>
        /// Gets or sets the argument5.
        /// </summary>
        /// <value>The argument5.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T5> Argument5 { get; set; }

        /// <summary>
        /// Gets or sets the argument6.
        /// </summary>
        /// <value>The argument6.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T6> Argument6 { get; set; }

        /// <summary>
        /// Gets or sets the argument7.
        /// </summary>
        /// <value>The argument7.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T7> Argument7 { get; set; }

        /// <summary>
        /// Gets or sets the argument8.
        /// </summary>
        /// <value>The argument8.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T8> Argument8 { get; set; }

        /// <summary>
        /// Gets or sets the argument9.
        /// </summary>
        /// <value>The argument9.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T9> Argument9 { get; set; }

        /// <summary>
        /// Gets or sets the argument10.
        /// </summary>
        /// <value>The argument10.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T10> Argument10 { get; set; }

        /// <summary>
        /// Gets or sets the argument11.
        /// </summary>
        /// <value>The argument11.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T11> Argument11 { get; set; }

        /// <summary>
        /// Gets or sets the argument12.
        /// </summary>
        /// <value>The argument12.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T12> Argument12 { get; set; }

        /// <summary>
        /// Gets or sets the argument13.
        /// </summary>
        /// <value>The argument13.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T13> Argument13 { get; set; }

        /// <summary>
        /// Gets or sets the argument14.
        /// </summary>
        /// <value>The argument14.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T14> Argument14 { get; set; }

        /// <summary>
        /// Gets or sets the argument15.
        /// </summary>
        /// <value>The argument15.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T15> Argument15 { get; set; }

        /// <summary>
        /// Gets or sets the argument16.
        /// </summary>
        /// <value>The argument16.</value>
        [DefaultValue(null)]
        public DelegateInArgument<T16> Argument16 { get; set; }

        /// <summary>
        /// Internals the get runtime delegate arguments.
        /// </summary>
        /// <returns>IList&lt;RuntimeDelegateArgument&gt;.</returns>
        internal override IList<RuntimeDelegateArgument> InternalGetRuntimeDelegateArguments()
        {
            IList<RuntimeDelegateArgument> result = new List<RuntimeDelegateArgument>(16)
            {
                { new RuntimeDelegateArgument(ActivityDelegate.Argument1Name, typeof(T1), ArgumentDirection.In, this.Argument1) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument2Name, typeof(T2), ArgumentDirection.In, this.Argument2) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument3Name, typeof(T3), ArgumentDirection.In, this.Argument3) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument4Name, typeof(T4), ArgumentDirection.In, this.Argument4) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument5Name, typeof(T5), ArgumentDirection.In, this.Argument5) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument6Name, typeof(T6), ArgumentDirection.In, this.Argument6) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument7Name, typeof(T7), ArgumentDirection.In, this.Argument7) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument8Name, typeof(T8), ArgumentDirection.In, this.Argument8) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument9Name, typeof(T9), ArgumentDirection.In, this.Argument9) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument10Name, typeof(T10), ArgumentDirection.In, this.Argument10) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument11Name, typeof(T11), ArgumentDirection.In, this.Argument11) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument12Name, typeof(T12), ArgumentDirection.In, this.Argument12) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument13Name, typeof(T13), ArgumentDirection.In, this.Argument13) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument14Name, typeof(T14), ArgumentDirection.In, this.Argument14) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument15Name, typeof(T15), ArgumentDirection.In, this.Argument15) },
                { new RuntimeDelegateArgument(ActivityDelegate.Argument16Name, typeof(T16), ArgumentDirection.In, this.Argument16) }
            };

            return result;
        }
    }
}