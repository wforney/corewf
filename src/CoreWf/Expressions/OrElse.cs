// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System.Activities;
    using System.Activities.Statements;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;

    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Optimizing for XAML naming. VB imperative users will [] qualify (e.g. New [OrElse])")]
    public sealed class OrElse : Activity<bool>
    {
        public OrElse()
            : base() => this.Implementation =
                () =>
                {
                    return this.Left == null || this.Right == null
                        ? null
                        : new If
                        {
                            Condition = this.Left,
                            Then = new Assign<bool>
                            {
                                To = new OutArgument<bool>(context => this.Result.Get(context)),
                                Value = true,
                            },
                            Else = new Assign<bool>
                            {
                                To = new OutArgument<bool>(context => this.Result.Get(context)),
                                Value = new InArgument<bool>(this.Right)
                            }
                        };
                };

        [DefaultValue(null)]
        public Activity<bool> Left { get; set; }

        [DefaultValue(null)]
        public Activity<bool> Right { get; set; }

#if NET45
        protected override void OnCreateDynamicUpdateMap(UpdateMapMetadata metadata, Activity originalActivity)
        {
            metadata.AllowUpdateInsideThisActivity();
        } 
#endif

        protected override void CacheMetadata(ActivityMetadata metadata)
        {
            metadata.AddImportedChild(this.Left);
            metadata.AddImportedChild(this.Right);

            if (this.Left == null)
            {
                metadata.AddValidationError(
                    SR.BinaryExpressionActivityRequiresArgument(
                        nameof(this.Left),
                        nameof(OrElse),
                        this.DisplayName));
            }

            if (this.Right == null)
            {
                metadata.AddValidationError(
                    SR.BinaryExpressionActivityRequiresArgument(
                        nameof(this.Right),
                        nameof(OrElse),
                        this.DisplayName));
            }
        }
    }
}
