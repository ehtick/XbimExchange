﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xbim.CobieLiteUk.Validation;
using Xbim.CobieLiteUk.Validation.Reporting;
using Xbim.CobieLiteUk;
using Xbim.Ifc;
using XbimExchanger.IfcToCOBieLiteUK;

namespace Tests
{
    [TestClass]
    [DeploymentItem(@"ValidationFiles\")]
    public class CobieLiteUKValidationTests
    {
        [TestMethod]
        public void CanSaveValidatedFacility()
        {
            var validated = GetValidated(@"Lakeside_Restaurant-stage6-COBie.json");
            validated.WriteJson(@"..\..\ValidationReport.json", true);
            validated.WriteXml(@"..\..\ValidationReport.xml", true);
            validated.WriteJson(@"ValidationReport.json", true);
        }

        [TestMethod]
        public void HandlesIncompleteRequirmentFile()
        {
            // stage 0 is for documents
            // stage 1 is for zones
            // stage 6 is for assettypes
            var validated = GetValidated(@"Lakeside_Restaurant-FacilityNotRequired.json");
            const string repName = @"..\..\ValidationReport.xlsx";
            var xRep = new ExcelValidationReport();
            var ret = xRep.Create(validated, repName);
            Assert.IsTrue(ret, "File not created");
        }

        [TestMethod]
        public void CanSaveValidationReport()
        {
            // stage 0 is for documents
            // stage 1 is for zones
            // stage 6 is for assettypes
            var validated = GetValidated(@"Lakeside_Restaurant-stage6-COBie.json");
            const string repName = @"..\..\ValidationReport.xlsx";
            var xRep = new ExcelValidationReport();
            var ret = xRep.Create(validated, repName);
            Assert.IsTrue(ret, "File not created");
        }

        private static Facility GetValidated(string requirementFile)
        {
            const string ifcTestFile = @"Lakeside_Restaurant_fabric_only.ifczip";
            Facility sub = null;

            //create validation file from IFC
            using (var m = IfcStore.Open(ifcTestFile))
            {               
                var facilities = new List<Facility>();
                var ifcToCoBieLiteUkExchanger = new IfcToCOBieLiteUkExchanger(m, facilities);
                facilities = ifcToCoBieLiteUkExchanger.Convert();
                sub = facilities.FirstOrDefault();
            }
            Assert.IsTrue(sub!=null);
            var vd = new FacilityValidator();
            var req = Facility.ReadJson(requirementFile);
            var validated = vd.Validate(req, sub);
            return validated;
        }

        [TestMethod]
        public void ValidateXlsLakeside()
        {
            const string xlsx = @"LakesideWithDocuments.xls";
            string msg;
            var cobie = Facility.ReadCobie(xlsx, out msg);
            var req = Facility.ReadJson(@"Lakeside_Restaurant-stage6-COBie.json");
            
            var validator = new FacilityValidator();
            var result = validator.Validate(req, cobie);
            result.WriteJson(@"..\..\XlsLakesideWithDocumentsValidationStage6.json", true);
        }

        [TestMethod]
        [DeploymentItem(@"ValidationFiles\VP\")]
        public void TestViewpointValidation()
        {
            string msg;
            var subFl = Facility.ReadCobie(@"VP\Submitted.xlsx", out msg);
            var reqFl = Facility.ReadCobie(@"VP\Required.xlsx", out msg);

            var validator = new FacilityValidator();
            var result = validator.Validate(reqFl, subFl);

            const string repName = @"..\..\VPValidationReport.xlsx";
            var xRep = new ExcelValidationReport();
            var ret = xRep.Create(result, repName);
            
            result.WriteCobie(@"..\..\VPValidationResult.xlsx", out msg);
        }

        

        [TestMethod]
        [DeploymentItem(@"ValidationFiles\XLSX\")]
        public void TestXlsxValidation()
        {
            string msg;
            var subFl = Facility.ReadCobie(@"XLSX\LakesideWithDocuments.xlsx", out msg);
            var reqFl = Facility.ReadCobie(@"XLSX\Requirements6.xlsx", out msg);
            
            var validator = new FacilityValidator();
            var result = validator.Validate(reqFl, subFl);
            
            var xRep = new ExcelValidationReport();
            var ret = xRep.Create(result, @"C:\Data\dev\XbimTeam\XbimExchange\Tests\ValidationFiles\XLSX\Report.xlsx");
        }

        [TestMethod]
        public void ValidateXlsLakesideForStage0()
        {
            var result = LakeSide0();
            result.WriteJson(@"..\..\XlsLakesideWithDocumentsValidationStage0.json", true);
        }

        [TestMethod]
        public void LakeSideXls0ValidationReport()
        {
            var repNames = new[]
            {
                @"..\..\LakeSideXls0ValidationReport.xlsx",
                @"..\..\LakeSideXls0ValidationReport.xls",
            };
            var validated = LakeSide0();
            foreach (var repName in repNames)
            {
                var xRep = new ExcelValidationReport();
                var ret = xRep.Create(validated, repName);
                Assert.IsTrue(ret, "File not created");
            }           
        }

        private static Facility LakeSide0()
        {
            const string xlsx = @"LakesideWithDocuments.xls";
            string msg;
            var cobie = Facility.ReadCobie(xlsx, out msg);
            var req = Facility.ReadJson(@"Lakeside_Restaurant-stage0-COBie.json");
            var validator = new FacilityValidator();
            var result = validator.Validate(req, cobie);
            return result;
        }
    }
}
