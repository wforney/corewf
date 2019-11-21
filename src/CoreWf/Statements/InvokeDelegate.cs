// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Globalization;
    using System.Activities.Runtime;
    using System.Windows.Markup;

using System.Activities.DynamicUpdate;

    //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotHaveIncorrectSuffix,
    //    Justification = "Approved Workflow naming")]
    [ContentProperty("Delegate")]
    public sealed class InvokeDelegate : NativeActivity
    {
        private readonly IDictionary<string, Argument> delegateArguments;
        private bool hasOutputArguments;

        public InvokeDelegate()
        {
            this.delegateArguments = new Dictionary<string, Argument>();
        }

        [DefaultValue(null)]
        public ActivityDelegate Delegate
        {
            get;
            set;
        }

        public IDictionary<string, Argument> DelegateArguments
        {
            get
            {
                return this.delegateArguments;
            }
        }

        [DefaultValue(null)]
        public Activity Default
        {
            get;
            set;
        }

#if NET45
        protected override void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            metadata.AllowUpdateInsideThisActivity();
        } 
#endif

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var arguments = new Collection<RuntimeArgument>();

            foreach (var entry in this.DelegateArguments)
            {
                var argument = new RuntimeArgument(entry.Key, entry.Value.ArgumentType, entry.Value.Direction);
                metadata.Bind(entry.Value, argument);
                arguments.Add(argument);
            }

            metadata.SetArgumentsCollection(arguments);
            metadata.AddDelegate(this.Delegate);

            if (this.Delegate != null)
            {
                var targetDelegateArguments = this.Delegate.RuntimeDelegateArguments;
                if (this.DelegateArguments.Count != targetDelegateArguments.Count)
                {
                    metadata.AddValidationError(SR.WrongNumberOfArgumentsForActivityDelegate);
                }

                // Validate that the names and directionality of arguments in DelegateArguments dictionary 
                // match the names and directionality of arguments returned by the ActivityDelegate.GetDelegateParameters 
                // call above. 
                for (var i = 0; i < targetDelegateArguments.Count; i++)
                {
                    var expectedParameter = targetDelegateArguments[i];
                    var parameterName = expectedParameter.Name;
                    if (this.DelegateArguments.TryGetValue(parameterName, out var delegateArgument))
                    {
                        if (delegateArgument.Direction != expectedParameter.Direction)
                        {
                            metadata.AddValidationError(SR.DelegateParameterDirectionalityMismatch(parameterName, delegateArgument.Direction, expectedParameter.Direction));
                        }

                        if (expectedParameter.Direction == ArgumentDirection.In)
                        {
                            if (!TypeHelper.AreTypesCompatible(delegateArgument.ArgumentType, expectedParameter.Type))
                            {
                                metadata.AddValidationError(SR.DelegateInArgumentTypeMismatch(parameterName, expectedParameter.Type, delegateArgument.ArgumentType));
                            }
                        }
                        else
                        {
                            if (!TypeHelper.AreTypesCompatible(expectedParameter.Type, delegateArgument.ArgumentType))
                            {
                                metadata.AddValidationError(SR.DelegateOutArgumentTypeMismatch(parameterName, expectedParameter.Type, delegateArgument.ArgumentType));
                            }
                        }
                    }
                    else
                    {
                        metadata.AddValidationError(SR.InputParametersMissing(expectedParameter.Name));
                    }

                    if (!this.hasOutputArguments && ArgumentDirectionHelper.IsOut(expectedParameter.Direction))
                    {
                        this.hasOutputArguments = true;
                    }
                }
            }

            metadata.AddChild(this.Default);
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (Delegate == null || Delegate.Handler == null)
            {
                if (this.Default != null)
                {
                    context.ScheduleActivity(this.Default);
                }

                return;
            }

            var inputParameters = new Dictionary<string, object>();

            if (DelegateArguments.Count > 0)
            {
                foreach (var entry in DelegateArguments)
                {
                    if (ArgumentDirectionHelper.IsIn(entry.Value.Direction))
                    {
                        inputParameters.Add(entry.Key, entry.Value.Get(context));
                    }
                }
            }

            context.ScheduleDelegate(Delegate, inputParameters, new DelegateCompletionCallback(OnHandlerComplete), null);
        }

        private void OnHandlerComplete(NativeActivityContext context, ActivityInstance completedInstance, IDictionary<string, object> outArguments)
        {
            if (this.hasOutputArguments)
            {
                foreach (var entry in outArguments)
                {
                    if (DelegateArguments.TryGetValue(entry.Key, out var argument))
                    {
                        if (ArgumentDirectionHelper.IsOut(argument.Direction))
                        {
                            DelegateArguments[entry.Key].Set(context, entry.Value);
                        }
                        else
                        {
                            Fx.Assert(string.Format(CultureInfo.InvariantCulture, "Expected argument named '{0}' in the DelegateArguments collection to be an out argument.", entry.Key));
                        }
                    }
                }
            }
        }

    }
}
