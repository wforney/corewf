// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.XamlIntegration
{

    using System;
    using System.Activities.DynamicUpdate;
    using System.Windows.Markup;
    using System.Xml.Serialization;

    [ContentProperty("XmlContent")]
    public class DynamicUpdateMapExtension : MarkupExtension
    {
        private NetDataContractXmlSerializable<DynamicUpdateMap> content;

        public DynamicUpdateMapExtension()
        {
        }

        public DynamicUpdateMapExtension(DynamicUpdateMap updateMap)
        {
            this.content = new NetDataContractXmlSerializable<DynamicUpdateMap>(updateMap);
        }

        public DynamicUpdateMap UpdateMap
        {
            get
            {
                return this.content?.Value;
            }
        }

        public IXmlSerializable XmlContent
        {
            get
            {
                if (this.content == null)
                {
                    this.content = new NetDataContractXmlSerializable<DynamicUpdateMap>();
                }

                return this.content;
            }
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this.UpdateMap;
        }
    }
}
