// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.ExpressionParser
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.Serialization;
    using System.Security;

    [Serializable]
    public class SourceExpressionException : Exception, ISerializable
    {
        private CompilerError[] errors;

        public SourceExpressionException()
            : base(SR.CompilerError)
        {
        }

        public SourceExpressionException(string message)
            : base(message)
        {
        }

        public SourceExpressionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public SourceExpressionException(string message, CompilerErrorCollection errors)
            : base(message)
        {
            this.errors = new CompilerError[errors?.Count ?? 0];
            errors.CopyTo(this.errors, 0);
        }

        protected SourceExpressionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            if (info == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(info));
            }

            var length = info.GetInt32("count");
            this.errors = new CompilerError[length];
            for (var i = 0; i < length; ++i)
            {
                var index = i.ToString(CultureInfo.InvariantCulture);
                var fileName = info.GetString("file" + index);
                var line = info.GetInt32("line" + index);
                var column = info.GetInt32("column" + index);
                var errorNumber = info.GetString("number" + index);
                var errorText = info.GetString("text" + index);
                this.errors[i] = new CompilerError(fileName, line, column, errorNumber, errorText);
            }
        }

        public IEnumerable<CompilerError> Errors
        {
            get
            {
                if (this.errors == null)
                {
                    this.errors = Array.Empty<CompilerError>();
                }

                return this.errors;
            }
        }

        [Fx.Tag.SecurityNote(Critical = "Critical because we are overriding a critical method in the base class.")]
        [SecurityCritical]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(info));
            }

            if (this.errors == null)
            {
                info.AddValue("count", 0);
            }
            else
            {
                info.AddValue("count", this.errors.Length);
                for (var i = 0; i < this.errors.Length; ++i)
                {
                    var error = this.errors[i];
                    var index = i.ToString(CultureInfo.InvariantCulture);
                    info.AddValue("file" + index, error.FileName);
                    info.AddValue("line" + index, error.Line);
                    info.AddValue("column" + index, error.Column);
                    info.AddValue("number" + index, error.ErrorNumber);
                    info.AddValue("text" + index, error.ErrorText);
                }
            }

            base.GetObjectData(info, context);
        }
    }
}
