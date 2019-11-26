// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{

    using System;
    using System.Activities.Runtime;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.IO;
    using System.Windows.Markup;

    [ContentProperty("Text")]
    public sealed class WriteLine : CodeActivity
    {
        public WriteLine()
        {
        }

        [DefaultValue(null)]
        public InArgument<TextWriter> TextWriter 
        {
            get;
            set;
        }

        [DefaultValue(null)]
        public InArgument<string> Text 
        {
            get;
            set;
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var textArgument = new RuntimeArgument("Text", typeof(string), ArgumentDirection.In);
            metadata.Bind(this.Text, textArgument);

            var textWriterArgument = new RuntimeArgument("TextWriter", typeof(TextWriter), ArgumentDirection.In);
            metadata.Bind(this.TextWriter, textWriterArgument);

            metadata.SetArgumentsCollection(
                new Collection<RuntimeArgument>
                {
                    textArgument,
                    textWriterArgument
                });
        }

        protected override void Execute(CodeActivityContext context)
        {
            var writer = this.TextWriter.Get(context);
            if (writer == null)
            {
                writer = context.GetExtension<TextWriter>() ?? Console.Out;
            }
            Fx.Assert(writer != null, "Writer should fallback to Console.Out and never be null");
            writer.WriteLine(this.Text.Get(context));
        }
    }
}
