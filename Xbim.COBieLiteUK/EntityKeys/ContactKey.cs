﻿using System;
using System.Linq;
using System.Xml.Serialization;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace
namespace Xbim.CobieLiteUk
{
    public partial class ContactKey : IEntityKey
    {
        [XmlIgnore, JsonIgnore]
        public Type ForType
        {
            get { return typeof(Contact); }
        }


        [XmlIgnore, JsonIgnore]
        string IEntityKey.Name
        {
            get { return Email; }
            set { Email = value; }
        }

        public string GetSheet(string mapping)
        {
            var attr =
                ForType.GetCustomAttributes(typeof(SheetMappingAttribute), true)
                    .FirstOrDefault(a => ((SheetMappingAttribute)a).Type == mapping) as SheetMappingAttribute;
            return attr == null ? null : attr.Sheet;
        }
    }
}
