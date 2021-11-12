using System;
using System.IO;
using System.Reflection;
using System.Threading;
using PRISM;

namespace PgSqlViewCreatorHelper
{
    internal class Program
    {
        private static DateTime mLastProgressTime;

        private static int Main(string[] args)
        {
            mLastProgressTime = DateTime.UtcNow;

            var asmName = typeof(Program).GetTypeInfo().Assembly.GetName();
            var exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);       // Alternatively: System.AppDomain.CurrentDomain.FriendlyName
            var version = ViewCreatorHelperOptions.GetAppVersion();

            var parser = new CommandLineParser<ViewCreatorHelperOptions>(asmName.Name, version)
            {
                ProgramInfo = "This program processes a SQL DDL file with CREATE VIEW commands and " +
                              "renames the column and table names referenced by the views to use " +
                              "new names defined in the mapping files.",

                ContactInfo = "Program written by Matthew Monroe for the Department of Energy" + Environment.NewLine +
                              "(PNNL, Richland, WA) in 2019" +
                              Environment.NewLine + Environment.NewLine +
                              "E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov" + Environment.NewLine +
                              "Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics",

                UsageExamples = {
                        exeName + @" DBName_unsure.sql /M:DBName_ColumnNameMap.txt"
                    }
            };

            parser.AddParamFileKey("Conf");

            var parseResults = parser.ParseArgs(args);
            var options = parseResults.ParsedResults;

            try
            {
                if (!parseResults.Success)
                {
                    Thread.Sleep(1500);
                    return -1;
                }

                if (!options.ValidateArgs(out var errorMessage))
                {
                    parser.PrintHelp();

                    Console.WriteLine();
                    ConsoleMsgUtils.ShowWarning("Validation error:");
                    ConsoleMsgUtils.ShowWarning(errorMessage);

                    Thread.Sleep(1500);
                    return -1;
                }

                options.OutputSetOptions();

            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.Write($"Error running {exeName}");
                Console.WriteLine(e.Message);
                Console.WriteLine($"See help with {exeName} --help");
                return -1;
            }

            try
            {
                var processor = new ViewCreatorHelper(options);

                processor.ErrorEvent += Processor_ErrorEvent;
                processor.StatusEvent += Processor_StatusEvent;
                processor.WarningEvent += Processor_WarningEvent;

                var success = processor.ProcessInputFile();

                if (success)
                {
                    Console.WriteLine();
                    Console.WriteLine("Processing complete");
                    Thread.Sleep(1500);
                    return 0;
                }

                ConsoleMsgUtils.ShowWarning("Processing error");
                Thread.Sleep(2000);
                return -1;

            }
            catch (Exception ex)
            {
                ConsoleMsgUtils.ShowError("Error occurred in Program->Main", ex);
                Thread.Sleep(2000);
                return -1;
            }
        }

        private static void Processor_DebugEvent(string message)
        {
            ConsoleMsgUtils.ShowDebug(message);
        }

        private static void Processor_ErrorEvent(string message, Exception ex)
        {
            ConsoleMsgUtils.ShowErrorCustom(message, ex, false);
        }

        private static void Processor_StatusEvent(string message)
        {
            Console.WriteLine(message);
        }

        private static void Processor_ProgressUpdate(string progressMessage, float percentComplete)
        {
            if (DateTime.UtcNow.Subtract(mLastProgressTime).TotalSeconds < 5)
                return;

            Console.WriteLine();
            mLastProgressTime = DateTime.UtcNow;
            Processor_DebugEvent(percentComplete.ToString("0.0") + "%, " + progressMessage);
        }

        private static void Processor_WarningEvent(string message)
        {
            ConsoleMsgUtils.ShowWarning(message);
        }
    }
}
