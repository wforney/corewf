// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{

    using System;
    using System.Activities;
    using System.Activities.DynamicUpdate;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Activities.Validation;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Windows.Markup;

    /// <summary>
    /// This class represents a StateMachine which contains States and Variables.
    /// </summary>
    [ContentProperty("States")]
    public sealed class StateMachine : NativeActivity
    {
        // internal Id of StateMachine. it's a constant value and states of state machine will generate their ids based on this root id.
        private const string RootId = "0";
        private const string ExitProperty = "Exit";
        private static readonly Func<StateMachineExtension> getDefaultExtension = new Func<StateMachineExtension>(GetStateMachineExtension);

        // states in root level of StateMachine
        private Collection<State> states;

        // variables used in StateMachine
        private Collection<Variable> variables;

        // internal representations of states
        private readonly Collection<InternalState> internalStates;

        // ActivityFuncs who call internal activities
        private readonly Collection<ActivityFunc<StateMachineEventManager, string>> internalStateFuncs;

        // Callback when a state completes
        private readonly CompletionCallback<string> onStateComplete;

        // eventManager is used to manage the events of trigger completion.
        // When a trigger on a transition is completed, the corresponding event will be sent to eventManager.
        // eventManager will decide whether immediate process it or just register it.
        private readonly Variable<StateMachineEventManager> eventManager;

        /// <summary>
        /// It's constructor.
        /// </summary>
        public StateMachine()
        {
            this.internalStates = new Collection<InternalState>();
            this.internalStateFuncs = new Collection<ActivityFunc<StateMachineEventManager, string>>();
            this.eventManager = new Variable<StateMachineEventManager> { Name = "EventManager", Default = new StateMachineEventManagerFactory() };
            this.onStateComplete = new CompletionCallback<string>(this.OnStateComplete);
        }

        /// <summary>
        /// Gets or sets the start point of the StateMachine.
        /// </summary>
        [DefaultValue(null)]
        public State InitialState
        {
            get;
            set;
        }

        /// <summary>
        /// Gets all root level States in the StateMachine.
        /// </summary>
        [DependsOn("InitialState")]
        public Collection<State> States
        {
            get
            {
                if (this.states == null)
                {
                    this.states = new ValidatingCollection<State>
                    {
                        // disallow null values
                        OnAddValidationCallback = item =>
                        {
                            if (item == null)
                            {
                                throw FxTrace.Exception.AsError(new ArgumentNullException(nameof(item)));
                            }
                        },
                    };
                }
             
                return this.states;
            }
        }

        /// <summary>
        /// Gets Variables which can be used within StateMachine scope.
        /// </summary>
        [DependsOn("States")]
        public Collection<Variable> Variables
        {
            get
            {
                if (this.variables == null)
                {
                    this.variables = new ValidatingCollection<Variable>
                    {
                        // disallow null values
                        OnAddValidationCallback = item =>
                        {
                            if (item == null)
                            {
                                throw FxTrace.Exception.AsError(new ArgumentNullException(nameof(item)));
                            }
                        },
                    };
                }

                return this.variables;
            }
        }

        private uint PassNumber
        {
            get;
            set;
        }

        /// <summary>
        /// Perform State Machine validation, in the following order:
        /// 1. Mark all states in States collection with an Id.
        /// 2. Traverse all states via declared transitions, and mark reachable states.
        /// 3. Validate transitions, states, and state machine
        /// Finally, declare arguments and variables of state machine, and declare states and transitions as activitydelegates.
        /// </summary>
        /// <param name="metadata">NativeActivityMetadata reference</param>
        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            // cleanup
            this.internalStateFuncs.Clear();
            this.internalStates.Clear();

            // clear Ids and Flags via transitions
            this.PassNumber++;
            this.TraverseViaTransitions(ClearState, ClearTransition);
            
            // clear Ids and Flags of all containing State references.
            this.PassNumber++;
            this.TraverseStates(
                (NativeActivityMetadata m, Collection<State> states) => { ClearStates(states); },
                (NativeActivityMetadata m, State state) => { ClearTransitions(state); },
                metadata,
                checkReached: false);

            // Mark via states and do some check
            this.PassNumber++;
            this.TraverseStates(
                this.MarkStatesViaChildren,
                (NativeActivityMetadata m, State state) => { MarkTransitionsInState(state); },
                metadata,
                checkReached: false);

            this.PassNumber++;
            
            // Mark via transition
            this.TraverseViaTransitions(delegate(State state) { MarkStateViaTransition(state); }, null);

            // Do validation via children
            // need not check the violation of state which is not reached
            this.PassNumber++;

            this.TraverseViaTransitions(
                (State state) =>
                {
                    ValidateTransitions(metadata, state);
                },
                actionForTransition: null);

            this.PassNumber++;

            this.TraverseStates(
                ValidateStates,
                (NativeActivityMetadata m, State state) => 
                {
                    if (!state.Reachable)
                    {
                        // log validation for states that are not reachable in the previous pass.
                        ValidateTransitions(m, state);
                    }
                },
                metadata: metadata,
                checkReached: true);

            // Validate the root state machine itself
            this.ValidateStateMachine(metadata);
            this.ProcessStates(metadata);

            metadata.AddImplementationVariable(this.eventManager);
            foreach (var variable in this.Variables)
            {
                metadata.AddVariable(variable);
            }

            metadata.AddDefaultExtensionProvider<StateMachineExtension>(getDefaultExtension);
        }

        /// <summary>
        /// Execution of StateMachine
        /// </summary>
        /// <param name="context">NativeActivityContext reference</param>
        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0",
                        Justification = "The context is used by workflow runtime. The parameter should be fine.")]
        protected override void Execute(NativeActivityContext context)
        {
            // We view the duration before moving to initial state is on transition.
            var localEventManager = this.eventManager.Get(context);
            localEventManager.OnTransition = true;
            localEventManager.CurrentBeingProcessedEvent = null;
            var index = StateMachineIdHelper.GetChildStateIndex(RootId, this.InitialState.StateId);

            context.ScheduleFunc<StateMachineEventManager, string>(
                this.internalStateFuncs[index],
                localEventManager,
                this.onStateComplete);
        }


        protected override void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            // enable dynamic update in state machine
            metadata.AllowUpdateInsideThisActivity();
        } 

        private static void MarkTransitionsInState(State state)
        {
            if (state.Transitions.Count > 0)
            {
                for (var i = 0; i < state.Transitions.Count; i++)
                {
                    var transition = state.Transitions[i];
                    if (!string.IsNullOrEmpty(state.StateId))
                    {
                        transition.Id = StateMachineIdHelper.GenerateTransitionId(state.StateId, i);
                    }
                }
            }
        }

        private static void MarkStateViaTransition(State state)
        {
            state.Reachable = true;
        }

        private static void ClearStates(Collection<State> states)
        {
            foreach (var state in states)
            {
                ClearState(state);
            }
        }

        private static void ClearState(State state)
        {
            state.StateId = null;
            state.Reachable = false;
            state.ClearInternalState();
        }

        private static void ClearTransitions(State state)
        {
            foreach (var transition in state.Transitions)
            {
                ClearTransition(transition);
            }
        }

        private static void ClearTransition(Transition transition)
        {
            transition.Source = null;
        }

        private static void ValidateStates(NativeActivityMetadata metadata, Collection<State> states)
        {
            foreach (var state in states)
            {
                // only validate reached state.
                ValidateState(metadata, state);
            }
        }

        private static void ValidateState(NativeActivityMetadata metadata, State state)
        {
            Fx.Assert(!string.IsNullOrEmpty(state.StateId), "StateId should have been set.");
            if (state.Reachable)
            {
                if (state.IsFinal)
                {
                    if (state.Exit != null)
                    {
                        metadata.AddValidationError(new ValidationError(
                            SR.FinalStateCannotHaveProperty(state.DisplayName, ExitProperty),
                            isWarning: false,
                            propertyName: string.Empty,
                            sourceDetail: state));
                    }

                    if (state.Transitions.Count > 0)
                    {
                        metadata.AddValidationError(new ValidationError(
                            SR.FinalStateCannotHaveTransition(state.DisplayName),
                            isWarning: false,
                            propertyName: string.Empty,
                            sourceDetail: state));
                    }
                }
                else
                {
                    if (state.Transitions.Count == 0)
                    {
                        metadata.AddValidationError(new ValidationError(
                            SR.SimpleStateMustHaveOneTransition(state.DisplayName),
                            isWarning: false,
                            propertyName: string.Empty,
                            sourceDetail: state));
                    }
                }
            }
        }

        private static void ValidateTransitions(NativeActivityMetadata metadata, State currentState)
        {
            var transitions = currentState.Transitions;
            var conditionalTransitionTriggers = new HashSet<Activity>();
            var unconditionalTransitionMapping = new Dictionary<Activity, List<Transition>>();

            foreach (var transition in transitions)
            {
                if (transition.Source != null)
                {
                    metadata.AddValidationError(new ValidationError(
                        SR.TransitionCannotBeAddedTwice(transition.DisplayName, currentState.DisplayName, transition.Source.DisplayName),
                        isWarning: false,
                        propertyName: string.Empty,
                        sourceDetail: transition));
                    continue;
                }
                else
                {
                    transition.Source = currentState;
                }

                if (transition.To == null)
                {
                    metadata.AddValidationError(new ValidationError(
                        SR.TransitionTargetCannotBeNull(transition.DisplayName, currentState.DisplayName),
                        isWarning: false,
                        propertyName: string.Empty,
                        sourceDetail: transition));
                }
                else if (string.IsNullOrEmpty(transition.To.StateId))
                {
                    metadata.AddValidationError(new ValidationError(
                        SR.StateNotBelongToAnyParent(
                            transition.DisplayName,
                            transition.To.DisplayName),
                        isWarning: false,
                        propertyName: string.Empty,
                        sourceDetail: transition));
                }

                var triggerActivity = transition.ActiveTrigger;

                if (transition.Condition == null)
                {
                    if (!unconditionalTransitionMapping.ContainsKey(triggerActivity))
                    {
                        unconditionalTransitionMapping.Add(triggerActivity, new List<Transition>());
                    }

                    unconditionalTransitionMapping[triggerActivity].Add(transition);
                }
                else
                {
                    conditionalTransitionTriggers.Add(triggerActivity);
                }
            }

            foreach (var unconditionalTransitions in unconditionalTransitionMapping)
            {
                if (conditionalTransitionTriggers.Contains(unconditionalTransitions.Key) ||
                    unconditionalTransitions.Value.Count > 1)
                {
                    foreach (var transition in unconditionalTransitions.Value)
                    {
                        if (transition.Trigger != null)
                        {
                            metadata.AddValidationError(new ValidationError(
                                SR.UnconditionalTransitionShouldNotShareTriggersWithOthers(
                                    transition.DisplayName,
                                    currentState.DisplayName,
                                    transition.Trigger.DisplayName),
                                isWarning: false,
                                propertyName: string.Empty,
                                sourceDetail: currentState));
                        }
                        else
                        {
                            // Null Trigger
                            metadata.AddValidationError(new ValidationError(
                                SR.UnconditionalTransitionShouldNotShareNullTriggersWithOthers(
                                    transition.DisplayName,
                                    currentState.DisplayName),
                                isWarning: false,
                                propertyName: string.Empty,
                                sourceDetail: currentState));
                        }
                    }
                }
            }
        }

        private static StateMachineExtension GetStateMachineExtension()
        {
            return new StateMachineExtension();
        }

        /// <summary>
        /// Create internal states
        /// </summary>
        /// <param name="metadata">NativeActivityMetadata reference.</param>
        private void ProcessStates(NativeActivityMetadata metadata)
        {
            // remove duplicate state in the collection during evaluation
            var distinctStates = this.states.Distinct();

            foreach (var state in distinctStates)
            {
                var internalState = state.InternalState;
                this.internalStates.Add(internalState);

                var eventManager = new DelegateInArgument<Statements.StateMachineEventManager>();
                internalState.EventManager = eventManager;

                var activityFunc = new ActivityFunc<StateMachineEventManager, string>
                {
                    Argument = eventManager,
                    Handler = internalState,
                };

                if (state.Reachable)
                {
                    // If this state is not reached, we should not add it as child because it's even not well validated.
                    metadata.AddDelegate(activityFunc, /* origin = */ state);
                }

                this.internalStateFuncs.Add(activityFunc);
            }
        }

        private void OnStateComplete(NativeActivityContext context, ActivityInstance completedInstance, string result)
        {
            if (StateMachineIdHelper.IsAncestor(RootId, result))
            {
                var index = StateMachineIdHelper.GetChildStateIndex(RootId, result);
                context.ScheduleFunc<StateMachineEventManager, string>(
                    this.internalStateFuncs[index],
                    this.eventManager.Get(context),
                    this.onStateComplete);
            }
        }

        private void ValidateStateMachine(NativeActivityMetadata metadata)
        {
            if (this.InitialState == null)
            {
                metadata.AddValidationError(SR.StateMachineMustHaveInitialState(this.DisplayName));
            }
            else
            {
                if (this.InitialState.IsFinal)
                {
                    Fx.Assert(!string.IsNullOrEmpty(this.InitialState.StateId), "StateId should have get set on the initialState.");
                    metadata.AddValidationError(new ValidationError(
                        SR.InitialStateCannotBeFinalState(this.InitialState.DisplayName),
                        isWarning: false,
                        propertyName: string.Empty,
                        sourceDetail: this.InitialState));
                }

                if (!this.States.Contains(this.InitialState))
                {
                    Fx.Assert(string.IsNullOrEmpty(this.InitialState.StateId), "Initial state would not have an id because it is not in the States collection.");
                    metadata.AddValidationError(SR.InitialStateNotInStatesCollection(this.InitialState.DisplayName));
                }
            }
        }

        private void TraverseStates(
            Action<NativeActivityMetadata, Collection<State>> actionForStates,
            Action<NativeActivityMetadata, State> actionForTransitions,
            NativeActivityMetadata metadata,
            bool checkReached)
        {
            if (actionForStates != null)
            {
                actionForStates(metadata, this.States);
            }

            var passNumber = this.PassNumber;

            var distinctStates = this.States.Distinct();
            foreach (var state in distinctStates)
            {
                if (!checkReached || state.Reachable)
                {
                    state.PassNumber = passNumber;

                    if (actionForTransitions != null)
                    {
                        actionForTransitions(metadata, state);
                    }
                }
            }
        }

        private void MarkStatesViaChildren(NativeActivityMetadata metadata, Collection<State> states)
        {
            if (states.Count > 0)
            {
                for (var i = 0; i < states.Count; i++)
                {
                    var state = states[i];

                    if (string.IsNullOrEmpty(state.StateId))
                    {
                        state.StateId = StateMachineIdHelper.GenerateStateId(RootId, i);
                        state.StateMachineName = this.DisplayName; 
                    }
                    else
                    {
                        // the state has been makred already: a duplicate state is found
                        metadata.AddValidationError(new ValidationError(
                        SR.StateCannotBeAddedTwice(state.DisplayName),
                            isWarning: false,
                            propertyName: string.Empty,
                            sourceDetail: state));
                    }
                }
            }
        }

        private void TraverseViaTransitions(Action<State> actionForState, Action<Transition> actionForTransition)
        {
            var stack = new Stack<State>();
            stack.Push(this.InitialState);
            var passNumber = this.PassNumber;
            while (stack.Count > 0)
            {
                var currentState = stack.Pop();
                if (currentState == null || currentState.PassNumber == passNumber)
                {
                    continue;
                }
                
                currentState.PassNumber = passNumber;

                if (actionForState != null)
                {
                    actionForState(currentState);
                }

                foreach (var transition in currentState.Transitions)
                {
                    if (actionForTransition != null)
                    {
                        actionForTransition(transition);
                    }

                    stack.Push(transition.To);
                }
            }
        }

        /// <summary>
        /// Originally, the Default value for StateMachineEventManager variable in StateMachine activity,
        /// is initialized via a LambdaValue activity. However, PartialTrust environment does not support 
        /// LambdaValue activity that references any local variables or non-public members.
        /// The recommended approach is to convert the LambdaValue to an equivalent internal CodeActivity.
        /// </summary>
        private sealed class StateMachineEventManagerFactory : CodeActivity<StateMachineEventManager>
        {
            protected override void CacheMetadata(CodeActivityMetadata metadata)
            {
                if (this.Result == null)
                { 
                    // metdata.Bind uses reflection if the argument property has a value of null. 
                    // So by forcing the argument property to have a non-null value, it avoids reflection.
                    // Otherwise it would use reflection to initializer Result and would fail Partial Trust.
                    this.Result = new OutArgument<StateMachineEventManager>();
                }
                
                var eventManagerArgument = new RuntimeArgument("Result", this.ResultType, ArgumentDirection.Out);
                metadata.Bind(this.Result, eventManagerArgument);
                metadata.AddArgument(eventManagerArgument);
            }
            
            protected override StateMachineEventManager Execute(CodeActivityContext context)
            {
                return new StateMachineEventManager();  
            }
        }
    }
}
