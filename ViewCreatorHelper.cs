using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PRISM;
using TableColumnNameMapContainer;

namespace PgSqlViewCreatorHelper
{
    public class ViewCreatorHelper : EventNotifier
    {
        // Ignore Spelling: dbo, dms, dpkg, mc, ont, sw

        private readonly ViewCreatorHelperOptions mOptions;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public ViewCreatorHelper(ViewCreatorHelperOptions options)
        {
            mOptions = options;
        }

        private void AppendCreateView(
            Match match,
            TextWriter writer,
            ICollection<string> matchedViews,
            out string viewName)
        {
            viewName = match.Groups["ViewName"].Value.Trim();

            var newCreateViewLine = "CREATE OR REPLACE VIEW " + viewName.Trim();

            OnDebugEvent(newCreateViewLine);
            writer.WriteLine(newCreateViewLine);

            matchedViews.Add(viewName);
        }

        private void AppendFormattingComment(TextWriter writer)
        {
            writer.WriteLine("-- PostgreSQL stores views as Parse Trees, meaning any whitespace that is present in the CREATE VIEW statements will be lost");
            writer.WriteLine("--");
            writer.WriteLine("-- The PgSQL View Creator Helper will convert any comments on views to COMMENT ON VIEW statements");
            writer.WriteLine();
        }

        /// <summary>
        /// Create a merged column name map file
        /// </summary>
        /// <param name="inputDirectory"></param>
        /// <param name="columnMapFile">Source Column name map file</param>
        /// <param name="tableNameMap">
        /// Dictionary where keys are the original (source) table names
        /// and values are WordReplacer classes that track the new table names and new column names in PostgreSQL
        /// </param>
        /// <param name="columnNameMap">
        /// Dictionary where keys are new table names
        /// and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
        /// names should not have double quotes around them
        /// </param>
        private void CreateMergedColumnNameMapFile(
            FileSystemInfo inputDirectory,
            FileSystemInfo columnMapFile,
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            IReadOnlyDictionary<string, Dictionary<string, WordReplacer>> columnNameMap)
        {
            try
            {
                if (inputDirectory == null)
                {
                    return;
                }

                var mergedFileName = Path.GetFileNameWithoutExtension(columnMapFile.Name) + "_merged" + columnMapFile.Extension;
                var outputFilePath = Path.Combine(inputDirectory.FullName, mergedFileName);

                OnStatusEvent("Creating " + outputFilePath);

                using var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read));

                var headerColumns = new List<string>
                {
                    "SourceTable", "SourceName", "Schema", "NewTable", "NewName"
                };

                writer.WriteLine(string.Join("\t", headerColumns));

                var dataValues = new List<string>();

                foreach (var sourceTableName in from item in tableNameMap.Keys orderby item select item)
                {
                    var tableInfo = tableNameMap[sourceTableName];

                    var newTableName = tableInfo.ReplacementText;
                    var newSchema = tableInfo.DefaultSchema;

                    if (!columnNameMap.TryGetValue(newTableName, out var targetTableColumnMap))
                    {
                        continue;
                    }

                    foreach (var columnItem in targetTableColumnMap)
                    {
                        var sourceColumnName = columnItem.Key;
                        var newColumnName = columnItem.Value.ReplacementText;

                        dataValues.Clear();
                        dataValues.Add(sourceTableName);
                        dataValues.Add(sourceColumnName);
                        dataValues.Add(newSchema);
                        dataValues.Add(newTableName);
                        dataValues.Add(newColumnName);

                        writer.WriteLine(string.Join("\t", dataValues));
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in CreateMergedColumnNameMapFile", ex);
            }
        }

        private bool LoadNameMapFiles(
            FileSystemInfo columnMapFile,
            out Dictionary<string, WordReplacer> tableNameMap,
            out Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap)
        {
            var mapReader = new NameMapReader();
            RegisterEvents(mapReader);

            var columnMapFileLoaded = mapReader.LoadSqlServerToPgSqlColumnMapFile(
                columnMapFile,
                mOptions.DefaultSchema,
                true,
                out tableNameMap,
                out columnNameMap);

            if (!columnMapFileLoaded)
                return false;

            var tableNameMapSynonyms = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(mOptions.TableNameMapFile))
            {
                var tableNameMapFile = new FileInfo(mOptions.TableNameMapFile);
                if (!tableNameMapFile.Exists)
                {
                    OnErrorEvent("Table name map file not found: " + tableNameMapFile.FullName);
                    return false;
                }

                var tableNameMapReader = new TableNameMapContainer.NameMapReader();
                RegisterEvents(tableNameMapReader);

                var tableNameInfo = tableNameMapReader.LoadTableNameMapFile(tableNameMapFile.FullName, true, out var abortProcessing);

                if (abortProcessing)
                {
                    return false;
                }

                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var item in tableNameInfo)
                {
                    if (tableNameMapSynonyms.ContainsKey(item.SourceTableName) || string.IsNullOrWhiteSpace(item.TargetTableName))
                        continue;

                    tableNameMapSynonyms.Add(item.SourceTableName, item.TargetTableName);
                }
            }

            if (string.IsNullOrWhiteSpace(mOptions.ColumnNameMapFile2))
                return true;

            var columnMapFile2 = new FileInfo(mOptions.ColumnNameMapFile2);
            if (!columnMapFile2.Exists)
            {
                OnErrorEvent("Secondary column name map file not found: " + columnMapFile2.FullName);
                return false;
            }

            return mapReader.LoadTableColumnMapFile(columnMapFile2, tableNameMap, columnNameMap, tableNameMapSynonyms);
        }

        /// <summary>
        /// Process the input file
        /// </summary>
        /// <returns>True if successful, false if an error</returns>
        public bool ProcessInputFile()
        {
            var cachedLines = new List<string>();

            try
            {
                var unmatchedStartingBracketMatcher = new Regex(@"(?<FieldName>[( ][a-z_]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var unmatchedEndingBracketMatcher = new Regex(@"\[(?<FieldName>[a-z_]+[, ])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var inputFile = new FileInfo(mOptions.InputScriptFile);
                if (!inputFile.Exists)
                {
                    OnErrorEvent("Input file not found: " + inputFile.FullName);
                    return false;
                }

                if (inputFile.Directory == null || inputFile.DirectoryName == null)
                {
                    OnErrorEvent("Unable to determine the parent directory of the input file: " + inputFile.FullName);
                    return false;
                }

                var outputFilePath = Path.Combine(
                    inputFile.DirectoryName,
                    Path.GetFileNameWithoutExtension(inputFile.Name) + "_updated" + inputFile.Extension);

                var columnMapFile = new FileInfo(mOptions.ColumnNameMapFile);
                if (!columnMapFile.Exists)
                {
                    OnErrorEvent("Column name map file not found: " + columnMapFile.FullName);
                    return false;
                }

                if (!LoadNameMapFiles(columnMapFile, out var tableNameMap, out var columnNameMap))
                    return false;

                var matchedViews = new List<string>();

                using (var reader = new StreamReader(new FileStream(inputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                using (var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    // The default search path is: "$user", public
                    // The DMS database customizes this, as shown here

                    // Command "SET search_path" updates the search path for the current session

                    // To update permanently, use:
                    // ALTER DATABASE dms SET search_path=public, sw, cap, dpkg, mc, ont;

                    writer.WriteLine("SET search_path TO public, sw, cap, dpkg, mc, ont;");
                    writer.WriteLine("SHOW search_path;");
                    writer.WriteLine();

                    var viewsProcessed = 0;

                    while (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();

                        if (string.IsNullOrWhiteSpace(dataLine))
                        {
                            cachedLines.Add(string.Empty);
                            continue;
                        }

                        if (dataLine.Trim().StartsWith("Create View", StringComparison.OrdinalIgnoreCase))
                        {
                            if (viewsProcessed == 0)
                                AppendFormattingComment(writer);

                            if (cachedLines.Count > 0)
                            {
                                var success = ProcessCachedLines(cachedLines, tableNameMap, columnNameMap, writer, matchedViews);
                                if (!success)
                                    return false;

                                cachedLines.Clear();

                                if (viewsProcessed == 0)
                                    writer.WriteLine();

                                viewsProcessed++;
                            }

                            cachedLines.Add(dataLine);
                            continue;
                        }

                        if (dataLine.Trim().StartsWith("Alter Table", StringComparison.OrdinalIgnoreCase))
                        {
                            var updatedLine = dataLine.Replace("(dbo].", "(");

                            while (true)
                            {
                                var match = unmatchedStartingBracketMatcher.Match(updatedLine);

                                if (match.Success)
                                {
                                    updatedLine = updatedLine.Replace(match.Value, match.Groups["FieldName"].Value);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            while (true)
                            {
                                var match = unmatchedEndingBracketMatcher.Match(updatedLine);

                                if (match.Success)
                                {
                                    updatedLine = updatedLine.Replace(match.Value, match.Groups["FieldName"].Value);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            cachedLines.Add(updatedLine);
                            continue;
                        }

                        cachedLines.Add(dataLine);
                    }

                    if (cachedLines.Count > 0)
                    {
                        var success = ProcessCachedLines(cachedLines, tableNameMap, columnNameMap, writer, matchedViews);
                        if (!success)
                            return false;

                        cachedLines.Clear();
                    }

                    writer.WriteLine();

                    foreach (var viewName in matchedViews)
                    {
                        writer.WriteLine("SELECT * FROM {0};", viewName);
                    }
                }

                CreateMergedColumnNameMapFile(inputFile.Directory, columnMapFile, tableNameMap, columnNameMap);

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ProcessInputFile", ex);
                return false;
            }
        }

        /// <summary>
        /// Looks for table names in cachedLines, then uses that information to update column names
        /// Writes the updated text to disk
        /// </summary>
        /// <param name="cachedLines"></param>
        /// <param name="tableNameMap">
        /// Dictionary where keys are the original (source) table names
        /// and values are WordReplacer classes that track the new table names and new column names in PostgreSQL
        /// </param>
        /// <param name="columnNameMap">
        /// Dictionary where keys are new table names
        /// and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
        /// names should not have double quotes around them
        /// </param>
        /// <param name="writer"></param>
        /// <param name="matchedViews">List of matched view names</param>
        private bool ProcessCachedLines(
            List<string> cachedLines,
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            TextWriter writer,
            ICollection<string> matchedViews)
        {
            // Keys in this list are the original version of the line
            // Values are the updated version
            var updatedLines = new List<KeyValuePair<string, string>>();

            var referencedTables = new SortedSet<string>();

            var createViewMatcher = new Regex(@"\s*CREATE VIEW +(?<ViewName>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var createViewAsMatcher = new Regex(@"\s*CREATE VIEW +(?<ViewName>.+) +AS *$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var stringConcatenationMatcher1 = new Regex(@"'[\t ]*\+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var stringConcatenationMatcher2 = new Regex(@"\+[\t ]*'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var leadingTabReplacer = new Regex(@"^\t+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Look for any use of CROSS APPLY or OUTER APPLY
            foreach (var dataLine in cachedLines)
            {
                if (dataLine.IndexOf("cross apply", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    writer.WriteLine("-- This view uses CROSS APPLY, which is not supported by PostgreSQL");
                    writer.WriteLine("-- Consider using INNER JOIN LATERAL instead");
                    writer.WriteLine("-- See also https://stackoverflow.com/a/35873193/1179467 and https://www.postgresql.org/docs/current/sql-select.html");
                    writer.WriteLine();
                    break;
                }

                if (dataLine.IndexOf("outer apply", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    writer.WriteLine("-- This view uses OUTER APPLY, which is not supported by PostgreSQL");
                    writer.WriteLine("-- Consider using LEFT JOIN LATERAL instead");
                    writer.WriteLine("-- See also https://stackoverflow.com/a/35873193/1179467 and https://www.postgresql.org/docs/current/sql-select.html");
                    writer.WriteLine();
                    break;
                }
            }

            var viewNames = new List<string>();
            var viewComments = new List<string>();

            // Look for table names in cachedLines, updating as appropriate
            foreach (var dataLine in cachedLines)
            {
                var match1 = createViewAsMatcher.Match(dataLine);
                if (match1.Success)
                {
                    AppendCreateView(match1, writer, matchedViews, out var viewName);
                    writer.WriteLine("AS");
                    viewNames.Add(viewName);
                    continue;
                }

                var match2 = createViewMatcher.Match(dataLine);
                if (match2.Success)
                {
                    AppendCreateView(match2, writer, matchedViews, out var viewName);
                    viewNames.Add(viewName);
                    continue;
                }

                var updatedLine = NameUpdater.FindAndUpdateTableNames(tableNameMap, referencedTables, dataLine, true);

                updatedLines.Add(new KeyValuePair<string, string>(dataLine, updatedLine));
            }

            // Look for column names in updatedLines, updating as appropriate
            // Also look for comments
            foreach (var dataLine in updatedLines)
            {
                var originalLine = dataLine.Key;

                var trimmedLine = originalLine.Trim();
                string commentText;

                if (viewNames.Count > 0 && trimmedLine.StartsWith("--"))
                {
                    if (trimmedLine.Length <= 2)
                    {
                        // Skip this line
                        continue;
                    }

                    // Cache the comment
                    commentText = trimmedLine.Substring(2).Trim();
                    viewComments.Add(commentText.Replace('\'', '"'));
                    continue;
                }

                var commentStartIndex = trimmedLine.IndexOf("--", StringComparison.Ordinal);
                if (viewNames.Count > 0 && commentStartIndex > 0 && commentStartIndex + 2 < trimmedLine.Length)
                {
                    // Cache the comment, but also include in the view DDL for reference
                    commentText = trimmedLine.Substring(commentStartIndex + 2).Trim();
                    viewComments.Add(commentText.Replace('\'', '"'));
                }

                var workingCopy = NameUpdater.UpdateColumnNames(columnNameMap, referencedTables, dataLine.Value, true);

                // Use || for string concatenation, instead of +
                if (stringConcatenationMatcher1.IsMatch(workingCopy))
                {
                    workingCopy = stringConcatenationMatcher1.Replace(workingCopy, "' ||");
                }

                if (stringConcatenationMatcher2.IsMatch(workingCopy))
                {
                    workingCopy = stringConcatenationMatcher2.Replace(workingCopy, "|| '");
                }

                // Replace leading tabs with spaces
                var leadingTabMatch = leadingTabReplacer.Match(workingCopy);
                if (leadingTabMatch.Success)
                {
                    workingCopy = new string(' ', leadingTabMatch.Length * 4) + workingCopy.TrimStart('\t');
                }

                workingCopy = workingCopy.TrimEnd().TrimEnd('\t').TrimEnd();

                if (originalLine.Equals(workingCopy))
                {
                    writer.WriteLine(originalLine);
                    continue;
                }

                if (mOptions.VerboseOutput)
                {
                    OnDebugEvent("Updating {0} \n        to {1}", originalLine, workingCopy);
                }

                writer.WriteLine(workingCopy);
            }

            if (viewComments.Count == 0)
                return true;

            foreach (var viewName in viewNames)
            {
                var viewComment = string.Join(". ", viewComments);

                writer.WriteLine("COMMENT ON VIEW {0} IS '{1}';", viewName, viewComment);
                writer.WriteLine();
            }

            return true;
        }
    }
}
