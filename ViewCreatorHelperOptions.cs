using System;
using System.Reflection;
using PRISM;

namespace PgSqlViewCreatorHelper
{
    public class ViewCreatorHelperOptions
    {
        /// <summary>
        /// Program date
        /// </summary>
        public const string PROGRAM_DATE = "December 13, 2019";

        #region "Properties"

        [Option("I", "Input", ArgPosition = 1, HelpShowsDefault = false, HelpText = "SQL script file to process")]
        public string InputScriptFile { get; set; }

        [Option("M", "Map", ArgPosition = 2, HelpShowsDefault = false, HelpText = "Column name map file; tab-delimited file with five columns: SourceTable SourceName Schema NewTable NewName")]
        public string ColumnNameMapFile { get; set; }

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public ViewCreatorHelperOptions()
        {
            InputScriptFile = string.Empty;
            ColumnNameMapFile = string.Empty;
        }

        /// <summary>
        /// Get the program version
        /// </summary>
        /// <returns></returns>
        public static string GetAppVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version + " (" + PROGRAM_DATE + ")";

            return version;
        }

        /// <summary>
        /// Show the options at the console
        /// </summary>
        public void OutputSetOptions()
        {
            Console.WriteLine("Options:");

            Console.WriteLine(" Input script file: {0}", InputScriptFile);
            Console.WriteLine(" Column name map file: {0}", ColumnNameMapFile);

            Console.WriteLine();

        }

        /// <summary>
        /// Validate the options
        /// </summary>
        /// <returns></returns>
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
