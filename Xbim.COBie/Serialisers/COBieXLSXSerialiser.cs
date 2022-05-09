﻿using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.COBie.Contracts;
using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using System.IO;
using System.Globalization;


namespace Xbim.COBie.Serialisers
{
    public class COBieXLSXSerialiser : ICOBieSerialiser
    {
        #region Properties
        /// <summary>
        /// False gives excel xls, true excel xlsx 
        /// </summary>
        public bool IsXlsx { get; set; }

        /// <summary>
        /// Filename to write workbook to
        /// </summary>
       // public string FileName { get; set; }
        private string _fileName;

        public string FileName
        {
            get {
                if (IsXlsx)
                {
                    _fileName = IsXlsx ? Path.ChangeExtension(_fileName, ".xlsx") : Path.ChangeExtension(_fileName, ".xls");
                }
                return _fileName; 
            }
            set { _fileName = value; }
        }
        
        /// <summary>
        /// Path and name of template file to use
        /// </summary>
        //public string TemplateFileName { get; set; }
        private string _templateFileName;

        public string TemplateFileName
        {
            get {
                if (IsXlsx)
                {
                    _templateFileName = IsXlsx ? Path.ChangeExtension(_templateFileName, ".xlsx") : Path.ChangeExtension(_templateFileName, ".xls");
                }
                return _templateFileName; 
            }
            set { _templateFileName = value; }
        }
        
        /// <summary>
        /// NPOI Workbook object
        /// </summary>
        public IWorkbook ExcelWorkbook { get; private set; }

        /// <summary>
        /// COBeError has the ErrorLevel property
        /// </summary>
        private bool hasErrorLevel { get; set; }

        /// <summary>
        /// Class holds exclude rules, now required as a tab to excel workbook
        /// </summary>
        public FilterValues Excludes { get; set; }
        #endregion

        #region Constants
        const string DefaultFileName = "Cobie.xls";
        const string DefaultTemplateFileName = @"Templates\COBie-UK-2012-template.xls";
        const string InstructionsSheet = "Instruction";
        const string ErrorsSheet = "Errors";
        const string RulesSheet = "Rules"; 
        #endregion

        #region local variables
        Dictionary<string, IColor> _colours = new Dictionary<string, IColor>();
        //need two keys to get correct style so used Tuple
        Dictionary<Tuple<string, COBieAllowedType>, ICellStyle> _cellStyles = new Dictionary<Tuple<string, COBieAllowedType>, ICellStyle>();
        public int _commentCount = 0; 
        #endregion

        public COBieXLSXSerialiser() : this(DefaultFileName, DefaultTemplateFileName)
        { }

        public COBieXLSXSerialiser(string filename)
            : this(filename, DefaultTemplateFileName)
        { }

        public COBieXLSXSerialiser(string fileName, string templateFileName)
        {
            IsXlsx = false;
            FileName = fileName;
            TemplateFileName = templateFileName;
            hasErrorLevel = typeof(COBieError).GetProperties().Where(prop => prop.Name == "ErrorLevel").Any();
            _commentCount = 0;
            Excludes = new FilterValues();//get the rules for excludes for generating COBie
        }


        /// <summary>
        /// Formats the COBie data into an Excel XLS file
        /// </summary>
        /// <param name="workbook"></param>
        /// <param name="validationTemplate"></param>
        public void Serialise(COBieWorkbook workbook, ICOBieValidationTemplate validationTemplate = null)
        {
            if (workbook == null) { throw new ArgumentNullException("COBie", "COBieXLSXSerialiser.Serialise does not accept null as the COBie data parameter."); }

            if (!File.Exists(TemplateFileName))
                throw new Exception("COBie creation error. Could not locate template file " + TemplateFileName);
            // Load template file
            FileStream excelFile = File.Open(TemplateFileName, FileMode.Open, FileAccess.Read);

            ExcelWorkbook = IsXlsx ? new XSSFWorkbook(excelFile) as IWorkbook : new HSSFWorkbook(excelFile, true) as IWorkbook;

            CreateCellStyles();

            foreach (var sheet in workbook)
            {
                WriteSheet(sheet);
            }

            UpdateInstructions();

            ReportErrors(workbook, validationTemplate);

            ReportRules();

            using (FileStream exportFile = File.Open(FileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                ExcelWorkbook.Write(exportFile);
            }
        }

        /// <summary>
        /// Writes the Excel worksheet for this COBie sheet
        /// </summary>
        /// <param name="sheet"></param>
        private void WriteSheet(ICOBieSheet<COBieRow> sheet)
        {

            ISheet excelSheet = ExcelWorkbook.GetSheet(sheet.SheetName) ?? ExcelWorkbook.CreateSheet(sheet.SheetName);

            var datasetHeaders = sheet.Columns.Values.ToList();
            var sheetHeaders = GetTargetHeaders(excelSheet);
            ValidateHeaders(datasetHeaders, sheetHeaders, sheet.SheetName);


            // Enumerate rows
            for (int r = 0; r < sheet.RowCount; r++)
            {
                if (r >= UInt16.MaxValue && !IsXlsx)
                {
                    throw new Exception(string.Format("Row count exceeds the XLS file type limit {0}", UInt16.MaxValue));
                    //break;
                }

                COBieRow row = sheet[r];

                // GET THE ROW + 1 - This stops us overwriting the headers of the worksheet
                IRow excelRow = excelSheet.GetRow(r + 1) ?? excelSheet.CreateRow(r + 1);

                for (int c = 0; c < sheet.Columns.Count; c++)
                {
                    COBieCell cell = row[c];

                    ICell excelCell = excelRow.GetCell(c) ?? excelRow.CreateCell(c);

                    SetCellValue(excelCell, cell);
                    FormatCell(excelCell, cell);
                }
            }

            if ((sheet.RowCount == 0) 
                //&& (_colours.ContainsKey("Grey"))
                )
            {
                if (IsXlsx)
                {
                    ((XSSFSheet)excelSheet).SetTabColor(IndexedColors.Grey50Percent.Index); 
                }
                else if (_colours.ContainsKey("Grey"))
                {
                    excelSheet.TabColorIndex = _colours["Grey"].Indexed;
                }
                
            }
            if (sheet.SheetName != Constants.WORKSHEET_PICKLISTS)
            {
                HighlightErrors(excelSheet, sheet);
            }


            RecalculateSheet(excelSheet);
        }

        /// <summary>
        /// Creates an excel comment in each cell with an associated error
        /// </summary>
        /// <param name="excelSheet"></param>
        /// <param name="sheet"></param>
        private void HighlightErrors(ISheet excelSheet, ICOBieSheet<COBieRow> sheet)
        {
            //sort by row then column
            var errors = sheet.Errors.OrderBy(err => err.Row).ThenBy(err => err.Column);

            // The patriarch is a container for comments on a sheet
            IDrawing patr = excelSheet.CreateDrawingPatriarch();
            int sheetCommnetCount = 0;
            foreach (var error in errors)
            {
                if (error.Row > 0 && error.Column >= 0)
                {
                    if ((error.Row + 3) > 65280)//UInt16.MaxValue some reason the CreateCellComment has 65280 as the max row number
                    {
                        // TODO: Warn overflow of XLS 2003 worksheet
                        break;
                    }
                    //limit comments to 1000 per sheet
                    if (sheetCommnetCount == 999)
                        break;

                    IRow excelRow = excelSheet.GetRow(error.Row);
                    if (excelRow != null)
                    {
                        ICell excelCell = excelRow.GetCell(error.Column);
                        if (excelCell != null)
                        {
                            string description = error.ErrorDescription;
                            if(hasErrorLevel)
                            {
                                if (error.ErrorLevel == COBieError.ErrorLevels.Warning)
                                    description = "Warning: " + description;
                                else
                                    description = "Error: " + description;
                            }

                            if (excelCell.CellComment == null)
                            {
                                try
                                {
                                    // A client anchor is attached to an excel worksheet. It anchors against a top-left and bottom-right cell.
                                    // Create a comment 3 columns wide and 3 rows height
                                    IClientAnchor anchor = null;
                                    if (IsXlsx)
                                    {
                                        anchor =  new XSSFClientAnchor(0, 0, 0, 0, error.Column, error.Row, error.Column + 3, error.Row + 3);
                                    }
                                    else
                                    {
                                        anchor =  new HSSFClientAnchor(0, 0, 0, 0, error.Column, error.Row, error.Column + 3, error.Row + 3);
                                    }

                                    IComment comment = patr.CreateCellComment(anchor);

                                    IRichTextString str = null;
                                    if (IsXlsx)
                                    {
                                        str = new XSSFRichTextString(description); 
                                    }
                                    else
                                    {
                                        str = new HSSFRichTextString(description); 
                                    }

                                    comment.String = str;
                                    comment.Author = "XBim";
                                    excelCell.CellComment = comment;
                                    _commentCount++;
                                    sheetCommnetCount++;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                            }
                            else
                            {
                                if (IsXlsx)
                                {
                                    ((XSSFRichTextString)excelCell.CellComment.String).Append(" Also " + description);
                                }
                                else
                                {
                                    description = excelCell.CellComment.String.ToString() + " Also " + description;
                                    excelCell.CellComment.String = new HSSFRichTextString(description);
                                }
                                
                            }
                            
                            
                        }
                        
                    }
                }
            }
        }

        /// <summary>
        /// Create Styles associated by colour and COBieAllowedType as keys in Dictionary
        /// </summary>
        private void CreateCellStyles()
        {
            CreateColours();
            //create cell styles for all colours in Dictionary
            //note that 
            foreach (string colour in _colours.Keys)
            {
                foreach (COBieAllowedType type in Enum.GetValues(typeof(COBieAllowedType)))
                {
                    CreateStyle(colour, type);
                }
                
            }
        }

        /// <summary>
        /// Set required colours for COBie Sheet
        /// </summary>
        private void CreateColours()
        {
            CreateColours("White", 0xFF, 0xFF, 0xFF);
            CreateColours("Yellow", 0xFF, 0xFF, 0x99);
            CreateColours("Purple", 0xCC, 0x99, 0xFF);
            CreateColours("Green", 0xCC, 0xFF, 0xCC);
            CreateColours("Puce", 0xFF, 0xCC, 0x99);
            CreateColours("Grey", 0x96, 0x96, 0x96);
        }

        /// <summary>
        /// create the colours for the COBie sheet
        /// </summary>
        /// <param name="colourName"></param>
        /// <param name="red"></param>
        /// <param name="green"></param>
        /// <param name="blue"></param>
        private void CreateColours(string colourName, byte red, byte green, byte blue)
        {
            IColor colour = null;
            if (IsXlsx)
            {
                byte[] rgb = new byte[3] { red, green, blue };
                colour = new XSSFColor(rgb) as IColor;
            }
            else
            {
                HSSFPalette palette = ((HSSFWorkbook)ExcelWorkbook).GetCustomPalette();
                colour = palette.FindSimilarColor(red, green, blue);
                if (colour == null)
                {
                    // First 64 are system colours
                    //srl this code does not work with the latest version of NPOI
                    //if  (NPOI.HSSF.Record.PaletteRecord.STANDARD_PALETTE_SIZE  < 64 )
                    //{
                    //     NPOI.HSSF.Record.PaletteRecord.STANDARD_PALETTE_SIZE = 64; 
                    //}
                    //NPOI.HSSF.Record.PaletteRecord.STANDARD_PALETTE_SIZE++;
                    colour = palette.AddColor(red, green, blue);
                } 
            }
            _colours.Add(colourName, colour);
        }

        /// <summary>
        /// Append the required format depending on the COBieAllowedType
        /// </summary>
        /// <param name="type">COBieAllowedType</param>
        /// <param name="cellStyle">ICellStyle, style to ally formate to</param>
        private void AppendFormat(COBieAllowedType type, ICellStyle cellStyle)
        {
            string formatString = null;
            switch (type)
            {
                case COBieAllowedType.ISODate:
                    formatString = "yyyy-MM-dd";
                    break;
                case COBieAllowedType.ISODateTime:
                    formatString = "yyyy-MM-ddThh:mm:ss";
                    break;
                case COBieAllowedType.AlphaNumeric:
                    break;
                case COBieAllowedType.Email:
                    break;
                case COBieAllowedType.Numeric:
                    break;
                case COBieAllowedType.Text:
                    break;
                case COBieAllowedType.AnyType:
                    break;
                default:
                    break;
            }
            if (formatString != null)
            {
                IDataFormat dataFormat = ExcelWorkbook.CreateDataFormat();
                cellStyle.DataFormat = dataFormat.GetFormat(formatString);
            }
        }

        /// <summary>
        /// Get required colour for the cell
        /// </summary>
        /// <param name="state">COBieAttributeState</param>
        /// <returns>string</returns>
        private string GetCellColour(COBieAttributeState state)
        {
            switch (state)
            {
                case COBieAttributeState.Required_PrimaryKey:
                case COBieAttributeState.Required_CompoundKeyPart:
                case COBieAttributeState.Required_Information:
                    return "Yellow";
                case COBieAttributeState.Required_Multi_PrimaryKey:
                case COBieAttributeState.Required_Reference_PrimaryKey:
                case COBieAttributeState.Required_Reference_ForeignKey:
                case COBieAttributeState.Required_Reference_PickList:
                    return "Puce";
                case COBieAttributeState.Required_PrimaryKey_IfSpecified:
                case COBieAttributeState.Required_System:
                case COBieAttributeState.Required_System_IfSpecified:
                    return "Purple";
                case COBieAttributeState.Required_IfSpecified:
                    return "Green";
                default:
                    return "White";
            }
            
        }

        /// <summary>
        /// Create the cell style
        /// </summary>
        /// <param name="colourName">string</param>
        /// <param name="type">COBieAllowedType</param>
        private void CreateStyle(string colourName, COBieAllowedType type)
        {
            ICellStyle cellStyle;
            cellStyle = ExcelWorkbook.CreateCellStyle() ;

            if (IsXlsx)
            {
                ((XSSFCellStyle)cellStyle).FillForegroundXSSFColor = (XSSFColor)_colours[colourName];
            }
            else
            {
                    cellStyle.FillForegroundColor = _colours[colourName].Indexed;
            }
            
            cellStyle.FillPattern = FillPattern.SolidForeground;

            cellStyle.BorderBottom = BorderStyle.Thin;
            cellStyle.BorderLeft = BorderStyle.Thin;
            cellStyle.BorderRight = BorderStyle.Thin;
            cellStyle.BorderTop = BorderStyle.Thin;
            AppendFormat(type,cellStyle); //add format for value display

            // TODO:maybe clone from the template?
            var tuple = new Tuple<string, COBieAllowedType>(colourName, type);
            _cellStyles.Add(tuple, cellStyle);
        }


        /// <summary>
        /// Set the cell to the correct colour and formate(as required)
        /// </summary>
        /// <param name="excelCell"></param>
        /// <param name="cell"></param>
        private void FormatCell(ICell excelCell, COBieCell cell)
        {
            string cellColour = GetCellColour(cell.COBieColumn.AttributeState);
            var tuple = new Tuple<string, COBieAllowedType>(cellColour, cell.COBieColumn.AllowedType);
            ICellStyle style;
            if (_cellStyles.TryGetValue(tuple, out style))
            {
                excelCell.CellStyle = style;
            }

        }

        private void UpdateInstructions()
        {
            ISheet instructionsSheet = ExcelWorkbook.GetSheet(InstructionsSheet);

            if (instructionsSheet != null)
            {
                RecalculateSheet(instructionsSheet);
            }
        }

        private void ReportErrors(COBieWorkbook workbook, ICOBieValidationTemplate ValidationTemplate = null)
        {
            ISheet errorsSheet = ExcelWorkbook.GetSheet(ErrorsSheet) ?? ExcelWorkbook.CreateSheet(ErrorsSheet);

            //if we are validating here then ensure we have Indices on each sheet
            //workbook.CreateIndices();
            ICOBieSheetValidationTemplate SheetValidator = null;
    
            
            foreach(var sheet in workbook.OrderBy(w=>w.SheetName))
            {
                if(sheet.SheetName != Constants.WORKSHEET_PICKLISTS)
                {
                    if (ValidationTemplate != null && ValidationTemplate.Sheet.ContainsKey(sheet.SheetName))
                    {
                        SheetValidator = ValidationTemplate.Sheet[sheet.SheetName];
                    }
                    // Ensure the validation is up to date
                     sheet.Validate(workbook, ErrorRowIndexBase.RowTwo, SheetValidator);
                }

                WriteErrors(errorsSheet, sheet.Errors);  
            }
        }


        int _row = 0;
        private void WriteErrors(ISheet errorsSheet, COBieErrorCollection errorCollection)
        {
            // Write Header
            var summary = errorCollection
                          .GroupBy(row => new { row.SheetName, row.FieldName, row.ErrorType })
                          .Select(grp => new { grp.Key.SheetName, grp.Key.ErrorType, grp.Key.FieldName, CountError = grp.Count(err => err.ErrorLevel == COBieError.ErrorLevels.Error), CountWarning = grp.Count(err => err.ErrorLevel == COBieError.ErrorLevels.Warning) })
                          .OrderBy(r => r.SheetName);
            
            //just in case we do not have ErrorLevel property in sheet COBieErrorCollection COBieError
            if (!hasErrorLevel)
            {
                summary = errorCollection
                          .GroupBy(row => new { row.SheetName, row.FieldName, row.ErrorType })
                          .Select(grp => new { grp.Key.SheetName, grp.Key.ErrorType, grp.Key.FieldName, CountError = grp.Count(), CountWarning = 0 })
                          .OrderBy(r => r.SheetName);
            }

            
            //Add Header
            if (_row == 0)
            {
                IRow excelRow = errorsSheet.GetRow(0) ?? errorsSheet.CreateRow(0);
                int col = 0;

                ICell excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue("Sheet Name");
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue("Field Name");
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue("Error Type");
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue("Error Count");
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue("Warning Count");
                col++;

                _row++; 
            }
            
            foreach(var error in summary)
            {

                IRow excelRow = errorsSheet.GetRow(_row + 1) ?? errorsSheet.CreateRow(_row + 1);
                int col = 0;

                ICell excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(error.SheetName);
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(error.FieldName);
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(error.ErrorType.ToString());
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(error.CountError);
                col++;

                excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(error.CountWarning);
                col++;
                
                _row++;
            }
            for (int c = 0 ; c < 5 ; c++)
            {
                errorsSheet.AutoSizeColumn(c);
            }

        }

        /// <summary>
        /// Create a rules sheet
        /// </summary>
        private void ReportRules()
        {
            ISheet rulesSheet = ExcelWorkbook.GetSheet(RulesSheet) ?? ExcelWorkbook.CreateSheet(RulesSheet);
            //Add Header
            List<string> headings = new List<string>() {   "Component Sheet Excluded Objects",
                                                           "Type Sheet Excluded Objects",
                                                           "Assembly Sheet Excluded Objects",
                                                           "Attributes Excludes All Sheets (Name Containing)",
                                                           "Attributes Excludes All Sheets (Name Equal)",
                                                           "Attributes Excludes All Sheets (Name Starts With)",
                                                           "Attributes Excludes Components (Name Containing)",
                                                           "Attributes Excludes Components (Name Equal)",
                                                           "Attributes Excludes Facility (Name Containing)",
                                                           "Attributes Excludes Facility (Name Equal)",
                                                           "Attributes Excludes Floor (Name Containing)",
                                                           "Attributes Excludes Floor (Name Equal)",
                                                           "Attributes Excludes Space (Name Containing)",
                                                           "Attributes Excludes Space (Name Equal)",
                                                           "Attributes Excludes Space (PropertySet Name)",
                                                           "Attributes Excludes Spare (Name Containing)",
                                                           "Attributes Excludes Spare (Name Equal)",
                                                           "Attributes Excludes Types (Name Containing)",
                                                           "Attributes Excludes Types (Name Equal)",
                                                           "Attributes Excludes Types (PropertySet Name)",
                                                           "Attributes Excludes Zone (Name Containing)"
                                                           };
            int col = 0;

            IRow excelRow = rulesSheet.GetRow(0) ?? rulesSheet.CreateRow(0);
            foreach (string title in headings)
            {
                ICell excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(title);
                col++;
            }

            WriteExcludesObjects(0, rulesSheet, Excludes.ObjectType.Component);
            WriteExcludesObjects(1, rulesSheet, Excludes.ObjectType.Types);
            WriteExcludesObjects(2, rulesSheet, Excludes.ObjectType.Assembly);

            WriteExcludesStrings(3, rulesSheet, Excludes.Common.AttributesContain);
            WriteExcludesStrings(4, rulesSheet, Excludes.Common.AttributesEqualTo);
            WriteExcludesStrings(5, rulesSheet, Excludes.Common.AttributesStartWith);
            WriteExcludesStrings(6, rulesSheet, Excludes.Component.AttributesContain);
            WriteExcludesStrings(7, rulesSheet, Excludes.Component.AttributesEqualTo);
            WriteExcludesStrings(8, rulesSheet, Excludes.Facility.AttributesContain);
            WriteExcludesStrings(9, rulesSheet, Excludes.Facility.AttributesEqualTo);
            WriteExcludesStrings(10, rulesSheet, Excludes.Floor.AttributesContain);
            WriteExcludesStrings(11, rulesSheet, Excludes.Floor.AttributesEqualTo);
            WriteExcludesStrings(12, rulesSheet, Excludes.Space.AttributesContain);
            WriteExcludesStrings(13, rulesSheet, Excludes.Space.AttributesEqualTo);
            WriteExcludesStrings(14, rulesSheet, Excludes.Space.PropertySetsEqualTo);
            WriteExcludesStrings(15, rulesSheet, Excludes.Spare.AttributesContain);
            WriteExcludesStrings(16, rulesSheet, Excludes.Spare.AttributesEqualTo);
            WriteExcludesStrings(17, rulesSheet, Excludes.Types.AttributesContain);
            WriteExcludesStrings(18, rulesSheet, Excludes.Types.AttributesEqualTo);
            WriteExcludesStrings(19, rulesSheet, Excludes.Types.PropertySetsEqualTo);
            WriteExcludesStrings(20, rulesSheet, Excludes.Zone.AttributesContain);

            for (int c = 0; c < headings.Count; c++)
            {
                rulesSheet.AutoSizeColumn(c);
            }
        }

        /// <summary>
        /// Write object types to the excel cells
        /// </summary>
        /// <param name="col">column index</param>
        /// <param name="rulesSheet">Sheet</param>
        /// <param name="excludeObjects">List of types</param>
        private void WriteExcludesObjects(int col, ISheet rulesSheet, List<Type> excludeObjects)
        {
            int row = 2;
            foreach (Type typeobj in excludeObjects)
            {
                IRow excelRow = rulesSheet.GetRow(row) ?? rulesSheet.CreateRow(row);
                ICell excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue(typeobj.Name.ToString());
                row++;
            }
        }

        /// <summary>
        /// Write strings to excel cells
        /// </summary>
        /// <param name="col">column index</param>
        /// <param name="rulesSheet">Sheet</param>
        /// <param name="excludeStrings">List of strings</param>
        private void WriteExcludesStrings(int col, ISheet rulesSheet, List<string> excludeStrings)
        {
            int row = 2;
            foreach (string str in excludeStrings)
            {
                IRow excelRow = rulesSheet.GetRow(row) ?? rulesSheet.CreateRow(row);
                ICell excelCell = excelRow.GetCell(col) ?? excelRow.CreateCell(col);
                excelCell.SetCellValue("\"" + str + "\"");
                row++;
            }
        }

        

        private void ValidateHeaders(List<COBieColumn> columns, List<string> sheetHeaders, string sheetName)
        {
            if (columns.Count != sheetHeaders.Count)
            {
                Console.WriteLine("Mis-matched number of columns in '{2}. {0} vs {1}", columns.Count, sheetHeaders.Count, sheetName);
            }

            if (sheetHeaders.Count == 0) return; //empty headers so do not log 

            for (int i = 0; i < columns.Count; i++)
            {
                if (!columns[i].IsMatch(sheetHeaders[i]))
                {
                    Console.WriteLine(@"{2} column {3} Mismatch: {0} {1}",
              columns[i].ColumnName, sheetHeaders[i], sheetName, i);
                }
            }

        }



        
        private List<string> GetTargetHeaders(ISheet excelSheet)
        {
            List<string> headers = new List<string>();

            IRow headerRow = excelSheet.GetRow(0);
            if (headerRow == null)
                return headers;

            foreach (ICell cell in headerRow.Cells)
            {
                headers.Add(cell.StringCellValue);
            }
            return headers;

        }

        

        private void SetCellValue(ICell excelCell, COBieCell cell)
        {
            if (SetCellTypedValue(excelCell, cell) == false)
            {
                //check text length will fit in cell
                if (cell.CellValue.Length >= short.MaxValue)
                { 
                    //truncate cell text to max length
                    excelCell.SetCellValue(cell.CellValue.Substring(0, short.MaxValue - 1));
                }
                else
                {
                    excelCell.SetCellValue(cell.CellValue);
                }
            }
        }

        private bool SetCellTypedValue(ICell excelCell, COBieCell cell)
        {
            bool processed = false;

            try
            {
                if (String.IsNullOrEmpty(cell.CellValue) || cell.CellValue == Constants.DEFAULT_STRING)
                {
                    return false;
                }

                // We need to set the value in the most appropriate overload of SetCellValue, so the parsing/formatting is correct
                switch (cell.COBieColumn.AllowedType)
                {
                    case COBieAllowedType.ISODateTime:
                    case COBieAllowedType.ISODate:
                        DateTime date;
                        if (DateTime.TryParse(cell.CellValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                        {
                            excelCell.SetCellValue(date);
                            processed = true;
                        }
                        break;

                    case COBieAllowedType.Numeric:
                        Double val;
                        if (Double.TryParse(cell.CellValue, out val))
                        {
                            excelCell.SetCellValue(val);
                            processed = true;
                        }
                        break;

                    default:
                        break;
                }
            }
            catch (SystemException)
            { /* Carry on */ }

            return processed;
        }


        
        private static void RecalculateSheet(ISheet excelSheet)
        {
            // Ensures the spreadsheet formulas will be recalculated the next time the file is opened
            excelSheet.ForceFormulaRecalculation = true;
            excelSheet.SetActiveCell(1, 0);

        }
    }
}
