// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace CoreWf.XamlIntegration
{
    internal class ObjectMatchInfo
    {
        private int result;

        public ObjectMatchInfo(int result) => this.result = result;

        public string? OriginalId { get; set; }
    }
}