﻿using System.Linq;
using Xbim.COBieLite;
using Xbim.CobieLiteUk;
using Xbim.Ifc2x3.ProductExtension;

namespace XbimExchanger.COBieLiteUkToIfc
{
    class MappingAssetToIfcElement<TToObject> : CoBieLiteUkIfcMappings<string, Asset, TToObject> where TToObject : IfcElement
    {

        protected override TToObject Mapping(Asset asset, TToObject ifcElement)
        {
            ifcElement.Name = asset.Name;
            ifcElement.Description = asset.Description;

            #region Categories
            if (asset.Categories != null)
                foreach (var category in asset.Categories)
                {
                    Exchanger.ConvertCategoryToClassification(category, ifcElement);
                }

            #endregion

            #region Attributes

            if (asset.Attributes != null)
            {

                foreach (var attribute in asset.Attributes)
                {
                    Exchanger.ConvertAttributeTypeToIfcObjectProperty(ifcElement, attribute);
                }
            }
            #endregion

            #region Space Assignments

            if (asset.Spaces != null && asset.Spaces.Any())
            {
                foreach (var spaceAssignment in asset.Spaces)
                {
                    var ifcSpace = Exchanger.GetIfcSpace(spaceAssignment);
                    if (ifcSpace != null) 
                        //throw new Exception("Space " + spaceAssignment.Name + " - " + spaceAssignment.Name+" cannot be found");

                    ifcSpace.AddElement(ifcElement);
                }
            }
            #endregion

            #region Documents
            if (asset.Documents != null && asset.Documents.Any())
            {
                Exchanger.ConvertDocumentsToDocumentSelect(ifcElement, asset.Documents);
            }
            #endregion

            return ifcElement;
        }
    }

}
