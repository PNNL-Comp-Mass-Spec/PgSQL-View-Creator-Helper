using System;
using System.Reflection;
using System.Text;
using PRISM;

namespace PgSqlViewCreatorHelper
{
    public class ViewCreatorHelperOptions
    {
        // Ignore Spelling: pre

        /// <summary>
        /// Program date
        /// </summary>
        public const string PROGRAM_DATE = "June 9, 2022";

        [Option("Input", "I", ArgPosition = 1, HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "SQL script file to process")]
        public string InputScriptFile { get; set; }

        [Option("Map", "M", HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "Column name map file (typically created by sqlserver2pgsql.pl)\n" +
                       "Tab-delimited file with five columns:\n" +
                       "SourceTable  SourceName  Schema  NewTable  NewName")]
        public string ColumnNameMapFile { get; set; }

        [Option("Map2", "AltMap", HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "Alternative column name map file\n" +
                       "(typically sent to DB_Schema_Export_Tool.exe via the ColumnMap parameter when using the ExistingDDL option " +
                       "to pre-process a DDL file prior to calling sqlserver2pgsql.pl)\n" +
                       "Tab-delimited file with three columns:\n" +
                       "SourceTableName  SourceColumnName  TargetColumnName")]
        public string ColumnNameMapFile2 { get; set; }

        [Option("TableNameMap", "TableNames", HelpShowsDefault = false, IsInputFilePath = true,
            HelpText = "Text file with table names (one name per line) used to track renamed tables\n" +
                       "(typically sent to DB_Schema_Export_Tool.exe via the DataTables parameter when using the ExistingDDL option " +
                       "to pre-process a DDL file prior to calling sqlserver2pgsql.pl)\n" +
                       "Tab-delimited file that must include columns SourceTableName and TargetTableName")]
        public string TableNameMapFile { get; set; }

        [Option("DefaultSchema", "Schema", HelpShowsDefault = false,
            HelpText = "Schema to prefix table names with (when the name does not have a schema)")]
        public string DefaultSchema { get; set; }

        [Option("SnakeCaseColumnAliases", "SnakeCaseColumns", "SnakeCaseAliases", "Snake", HelpShowsDefault = true,
            HelpText = "When true, convert aliased column names to snake case (e.g. switch from ColumnName to column_name); additionally:\n" +
                       "  Replace spaces and dashes with underscores\n" +
                       "  Replace percent signs with pct\n" +
                       "  Remove periods at the end of words\n" +
                       "  Remove parentheses")]
        public bool SnakeCaseColumnAliases { get; set; }

        [Option("SnakeCaseDisableViewSuffixes", "SuffixesNoSnakeCase", "NoSnake", HelpShowsDefault = true,
            HelpText = "Comma separated list of view name suffixes for which column aliases should not be converted to snake case\n" +
                       "Additionally, for table columns that do not have an alias, if the column is renamed, add an alias using the original column name")]
        public string SnakeCaseDisableViewSuffixes { get; set; }

        [Option("CreateRenamedColumnMapFile", "CreateColumnMapFile", "LogRenamed", HelpShowsDefault = true,
            HelpText = "When true, create a tab-delimited text file that lists the renamed columns for each view; this is only valid if SnakeCaseColumnAliases is also true\n" +
                       "For aliased column names, if the column name in the database changed but the alias name did not change, the alias name will not be listed in this file")]
        public bool CreateRenamedColumnMapFile { get; set; }

        [Option("RenamedMapFileIncludeCaseChange", "IncludeCaseChange", "LogCaseChanges", HelpShowsDefault = true,
            HelpText = "When true, include columns and column aliases where the only change was to convert to lowercase")]
        public bool IncludeCaseChangeInRenamedColumnMapFile { get; set; }

        [Option("Verbose", "V", HelpShowsDefault = true,
            HelpText = "When true, display the old and new version of each updated line")]
        public bool VerboseOutput { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ViewCreatorHelperOptions()
        {
            InputScriptFile = string.Empty;
            ColumnNameMapFile = string.Empty;
            ColumnNameMapFile2 = string.Empty;
            TableNameMapFile = string.Empty;
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

            if (!string.IsNullOrWhiteSpace(TableNameMapFile))
            {
                Console.WriteLine(" {0,-35} {1}", "Table name map file:", PathUtils.CompactPathString(TableNameMapFile, 80));
            }

            Console.WriteLine(" {0,-35} {1}", "Default schema name:",
                string.IsNullOrWhiteSpace(DefaultSchema) ? "not defined" : DefaultSchema);

            Console.WriteLine(" {0,-35} {1}", "Snake case column aliases:", SnakeCaseColumnAliases);

            SnakeCaseDisableViewSuffixes ??= string.Empty;

            if (SnakeCaseDisableViewSuffixes.Length > 40)
            {
                var currentLine = new StringBuilder();
                var currentSuffixCount = 0;

                currentLine.AppendFormat(" {0,-35} ", "View name suffixes no snake case:");

                foreach (var suffix in SnakeCaseDisableViewSuffixes.Split(','))
                {
                    var newLength = currentLine.Length + suffix.Trim().Length + (currentSuffixCount > 0 ? 2 : 0);

                    if (newLength > 110)
                    {
                        Console.WriteLine(currentLine);
                        currentLine.Clear();
                        currentSuffixCount = 0;

                        currentLine.AppendFormat(" {0,-35} ", string.Empty);
                    }

                    if (currentSuffixCount > 0)
                        currentLine.Append(", ");

                    currentLine.Append(suffix.Trim());
                    currentSuffixCount++;
                }

                Console.WriteLine(currentLine);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine(" {0,-35} {1}", "View name suffixes no snake case:", SnakeCaseDisableViewSuffixes);
            }

            Console.WriteLine(" {0,-35} {1}", "Verbose Output:", VerboseOutput);

            Console.WriteLine(" {0,-35} {1}", "Create renamed column map file:", SnakeCaseColumnAliases && CreateRenamedColumnMapFile);

            Console.WriteLine(" {0,-45} {1}", "Include case change only columns in map file:", IncludeCaseChangeInRenamedColumnMapFile);

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
