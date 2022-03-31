using System;
using System.Reflection;
using PRISM;

namespace PgSqlViewCreatorHelper
{
    public class ViewCreatorHelperOptions
    {
        // Ignore Spelling: pre

        /// <summary>
        /// Program date
        /// </summary>
        public const string PROGRAM_DATE = "March 30, 2022";

        [Option("Input", "I", ArgPosition = 1, HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "SQL script file to process")]
        public string InputScriptFile { get; set; }

        [Option("Map", "M", ArgPosition = 2, HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "Column name map file (typically created by sqlserver2pgsql.pl); tab-delimited file with five columns:\n" +
                       "SourceTable  SourceName  Schema  NewTable  NewName")]
        public string ColumnNameMapFile { get; set; }

        [Option("Map2", "AltMap", HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "Alternative column name map file (typically sent to DB_Schema_Export_Tool.exe via the ColumnMap parameter when using the ExistingDDL option " +
                       "to pre-process a DDL file prior to calling sqlserver2pgsql.pl); tab-delimited file with three columns:\n" +
                       "SourceTableName  SourceColumnName  TargetColumnName")]
        public string ColumnNameMapFile2 { get; set; }

        [Option("DefaultSchema", "Schema", HelpShowsDefault = false,
            HelpText = "Schema to prefix table names with (when the name does not have a schema)")]
        public string DefaultSchema { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ViewCreatorHelperOptions()
        {
            InputScriptFile = string.Empty;
            ColumnNameMapFile = string.Empty;
            ColumnNameMapFile2 = string.Empty;
            DefaultSchema = string.Empty;
        }

        /// <summary>
        /// Get the program version
        /// </summary>
        public static string GetAppVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version + " (" + PROGRAM_DATE + ")";
        }

        /// <summary>
        /// Show the options at the console
        /// </summary>
        public void OutputSetOptions()
        {
            Console.WriteLine("Options:");

            Console.WriteLine(" {0,-35} {1}", "Input script file:", PathUtils.CompactPathString(InputScriptFile, 80));
            Console.WriteLine(" {0,-35} {1}", "Column name map file:", PathUtils.CompactPathString(ColumnNameMapFile, 80));

            if (!string.IsNullOrWhiteSpace(ColumnNameMapFile2))
            {
                Console.WriteLine(" {0,-35} {1}", "Secondary column name map file:", PathUtils.CompactPathString(ColumnNameMapFile2, 80));
            }

            Console.WriteLine(" {0,-35} {1}", "Default schema name:",
                string.IsNullOrWhiteSpace(DefaultSchema) ? "not defined" : DefaultSchema);

            Console.WriteLine();
        }

        /// <summary>
        /// Validate the options
        /// </summary>
        /// <returns>True if options are valid, false if /I or /M is missing</returns>
        public bool ValidateArgs(out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(InputScriptFile))
            {
                errorMessage = "Use /I to specify the SQL script file to process";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ColumnNameMapFile))
            {
                errorMessage = "Use /M to specify the column name map file";
                return false;
            }

            errorMessage = string.Empty;

            return true;
        }
    }
}
