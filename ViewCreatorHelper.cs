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
        // ReSharper disable CommentTypo

        // Ignore Spelling: dbo, dms, dpkg, logdms, logcap, logsw, mc, nvarchar, ont, pc, sw, varchar

        // ReSharper restore CommentTypo

        /// <summary>
        /// Match any character that is not a letter, number, or underscore
        /// </summary>
        private readonly Regex mColumnCharNonStandardMatcher = new("[^a-z0-9_]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>
        /// This matches all of the commas in a line
        /// </summary>
        private readonly Regex mCommaMatcher = new(",", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This matches spaces and tabs at the start of a line
        /// </summary>
        private readonly Regex mLeadingWhitespaceMatcher = new("^[\t ]+", RegexOptions.Compiled);

        /// <summary>
        /// This is used to find rows in view definitions with multiple columns, separated by a comma
        /// </summary>
        private readonly Regex mMultiColumnMatcher = new(@"(?<FirstColumn>[ \t]AS[ \t]+[^,\r\n]+,)(?<SecondColumn>.+,)(?<AdditionalText>.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This matches alias names surrounded by double quotes
        /// </summary>
        private readonly Regex mQuotedAliasNameMatcher = new("(?<ColumnName>[a-z_]+)?(?<As>[ \t]+AS[ \t]+)\"(?<AliasName>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This matches column names surrounded by square brackets or double quotes
        /// </summary>
        private readonly Regex mQuotedColumnNameMatcher = new(@"[""\[](?<ColumnName>[^]""]+)[""\]][ \t]*(?<Comma>,?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This matches the word SELECT
        /// </summary>
        private readonly Regex mSelectKeywordMatcher = new(@"\bSELECT\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This matches alias names with characters a-z, 0-9, or underscore
        /// </summary>
        private readonly Regex mUnquotedAliasNameMatcher = new("(?<ColumnName>[a-z_]+)?(?<As>[ \t]+AS[ \t]+)(?<AliasName>[a-z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This matches unquoted column names
        /// </summary>
        private readonly Regex mUnquotedColumnNameMatcher = new(@"(?<ColumnName>[a-z_0-9]+)[ \t]*(?<Comma>,?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            TextWriter writer,
            string viewName,
            string createViewLine,
            ICollection<string> matchedViews,
            ICollection<string> viewNames,
            ICollection<string> viewsInCurrentBlock,
            List<string> warningMessages,
            bool skipAsKeyword)
        {
            if (warningMessages == null)
                throw new ArgumentNullException(nameof(warningMessages));

            foreach (var message in warningMessages)
            {
                writer.WriteLine(message);
            }

            OnDebugEvent(createViewLine);

            writer.WriteLine();
            writer.WriteLine(createViewLine);

            if (!skipAsKeyword)
            {
                writer.WriteLine("AS");
            }

            matchedViews.Add(viewName);

            viewNames.Add(viewName);
            viewsInCurrentBlock.Add(viewName);
        }

        private void AppendFormattingComment(TextWriter writer)
        {
            writer.WriteLine("-- PostgreSQL stores views as Parse Trees, meaning any whitespace that is present in the CREATE VIEW statements will be lost");
            writer.WriteLine("--");
            writer.WriteLine("-- The PgSQL View Creator Helper will convert any comments on views to COMMENT ON VIEW statements");
            writer.WriteLine();
        }

        private void AppendRenamedColumns(
            ICollection<RenamedColumnInfo> renamedColumnsInView,
            List<KeyValuePair<string, string>> renamedColumnAliasesInView)
        {
            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var renamedColumn in renamedColumnAliasesInView)
            {
                if (renamedColumn.Key.Equals(renamedColumn.Value))
                    continue;

                if (!mOptions.IncludeCaseChangeInRenamedColumnMapFile &&
                    renamedColumn.Key.Equals(renamedColumn.Value, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                renamedColumnsInView.Add(new RenamedColumnInfo(renamedColumn.Key, renamedColumn.Value));
            }
        }

        /// <summary>
        /// Convert a column alias to snake_case, including replacing / removing symbols
        /// </summary>
        /// <param name="aliasName"></param>
        private string ConvertColumnAliasToSnakeCase(string aliasName)
        {
            // Replace spaces and dashes with underscores
            aliasName = aliasName.Replace(' ', '_');
            aliasName = aliasName.Replace('-', '_');

            // Remove periods at the end of words
            aliasName = aliasName.TrimEnd('.').Replace("._", "_");

            // Remove parentheses
            var openParenthesesIndex = aliasName.IndexOf('(');
            if (openParenthesesIndex > 0)
            {
                var closeParenthesesIndex = aliasName.IndexOf(')', openParenthesesIndex);

                if (closeParenthesesIndex > 0)
                {
                    // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                    if (aliasName.Substring(openParenthesesIndex - 1, 1).Equals("_"))
                    {
                        aliasName = aliasName.Replace("_(", "_");
                    }
                    else
                    {
                        aliasName = aliasName.Replace("(", "_");
                    }

                    aliasName = aliasName.Replace(")", string.Empty);
                }
            }

            // Replace percent sign with pct
            aliasName = aliasName.Replace("%", "pct");

            // Convert to snake case
            aliasName = NameUpdater.ConvertNameToSnakeCase(aliasName);

            // Return the updated name, quoting if it contains characters other than a-z, 0-9, or underscore
            return mColumnCharNonStandardMatcher.IsMatch(aliasName)
                ? string.Format("\"{0}\"", aliasName)
                : aliasName;
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

        private void CreateRenamedColumnMapFile(FileInfo inputFile, Dictionary<string, List<RenamedColumnInfo>> updatedColumnNamesAndAliases)
        {
            try
            {
                if (inputFile.Directory == null)
                {
                    return;
                }

                var renamedColumnMapFile = Path.GetFileNameWithoutExtension(inputFile.Name) + "_RenamedColumns.txt";
                var outputFilePath = Path.Combine(inputFile.Directory.FullName, renamedColumnMapFile);

                OnStatusEvent("Creating " + outputFilePath);

                using var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read));

                var headerColumns = new List<string>
                {
                    "View", "SourceColumnName", "NewColumnName"
                };

                writer.WriteLine(string.Join("\t", headerColumns));

                // This sorted set is used to prevent duplicate lines in the output file
                var columnRenames = new SortedSet<string>();

                foreach (var currentView in updatedColumnNamesAndAliases)
                {
                    columnRenames.Clear();

                    foreach (var item in currentView.Value)
                    {
                        var keyName = string.Format("{0}_{1}", item.OriginalColumnName, item.NewColumnName);

                        if (columnRenames.Contains(keyName))
                            continue;

                        columnRenames.Add(keyName);

                        writer.WriteLine(
                            "{0}\t{1}\t{2}",
                            currentView.Key, item.OriginalColumnName, item.NewColumnName);
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in CreateRenamedColumnMapFile", ex);
            }
        }

        private string GetCreateViewLine(
            Match match,
            out string viewName)
        {
            viewName = match.Groups["ViewName"].Value.Trim();

            return "CREATE OR REPLACE VIEW " + viewName.Trim();
        }

        /// <summary>
        /// Get the object name, without the schema
        /// </summary>
        /// <remarks>
        /// Simply looks for the first period and assumes the schema name is before the period and the object name is after it
        /// </remarks>
        /// <param name="objectName"></param>
        /// <param name="removeDoubleQuotes"></param>
        private static string GetNameWithoutSchema(string objectName, bool removeDoubleQuotes = false)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return string.Empty;

            var trimChars = removeDoubleQuotes ? new[] { '"' } : Array.Empty<char>();

            var periodIndex = objectName.IndexOf('.');
            if (periodIndex > 0 && periodIndex < objectName.Length - 1)
                return objectName.Substring(periodIndex + 1).Trim(trimChars);

            return objectName.Trim(trimChars);
        }

        /// <summary>
        /// Looks for the word SELECT in dataLine
        /// </summary>
        /// <param name="dataLine">Text to search</param>
        /// <param name="selectKeywordIndex">Index of the word SELECT in the line if found, otherwise -1</param>
        /// <returns>True if found, otherwise false</returns>
        private bool HasSelectKeyword(string dataLine, out int selectKeywordIndex)
        {
            var match = mSelectKeywordMatcher.Match(dataLine);

            if (match.Success)
                selectKeywordIndex = match.Index;
            else
                selectKeywordIndex = -1;

            return match.Success;
        }

        private bool LoadNameMapFiles(
            FileSystemInfo columnMapFile,
            out Dictionary<string, WordReplacer> tableNameMap,
            out Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            out SortedSet<string> viewsToSkip)
        {
            var mapReader = new NameMapReader();
            RegisterEvents(mapReader);

            // In dictionary tableNameMap, keys are the original (source) table names
            // and values are WordReplacer classes that track the new table names and new column names in PostgreSQL

            // In dictionary columnNameMap, keys are new table names
            // and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
            // names should not have double quotes around them

            // Dictionary tableNameMapSynonyms has original table names to new table names

            var columnMapFileLoaded = mapReader.LoadSqlServerToPgSqlColumnMapFile(
                columnMapFile,
                mOptions.DefaultSchema,
                true,
                out tableNameMap,
                out columnNameMap);

            viewsToSkip = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

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

                foreach (var item in tableNameMapSynonyms)
                {
                    if (item.Key.StartsWith("V_", StringComparison.OrdinalIgnoreCase) &&
                        item.Value.Equals("<skip>", StringComparison.OrdinalIgnoreCase) &&
                        !viewsToSkip.Contains(item.Key))
                    {
                        viewsToSkip.Add(item.Key);
                    }
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

            var secondaryMapFileLoaded = mapReader.LoadTableColumnMapFile(columnMapFile2, tableNameMap, columnNameMap, tableNameMapSynonyms);

            if (!secondaryMapFileLoaded)
                return false;

            // When Perl script sqlserver2pgsql.pl writes out CREATE INDEX lines, the include columns are converted to snake case but are not renamed
            // To account for this, step through the tables and columns in columnNameMap and add snake case mappings
            foreach (var tableItem in columnNameMap)
            {
                var currentTable = tableItem.Key;

                var columnsToAdd = new Dictionary<string, WordReplacer>();

                foreach (var columnItem in tableItem.Value)
                {
                    var updatedColumnName = NameUpdater.ConvertNameToSnakeCase(columnItem.Key);

                    if (updatedColumnName.Equals(columnItem.Key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (updatedColumnName.Equals(columnItem.Value.ReplacementText))
                        continue;

                    columnsToAdd.Add(updatedColumnName,
                        new WordReplacer(updatedColumnName, columnItem.Value.ReplacementText, columnItem.Value.DefaultSchema));
                }

                foreach (var newColumn in columnsToAdd)
                {
                    if (!tableItem.Value.ContainsKey(newColumn.Key))
                    {
                        tableItem.Value.Add(newColumn.Key, newColumn.Value);
                    }
                    else
                    {
                        OnDebugEvent("Table {0} already has the mapping {1} -> {2}", currentTable, newColumn.Key, newColumn.Value);
                    }
                }
            }

            return true;
        }

        private List<string> MoveViewColumnsToNextLine(List<string> cachedLines)
        {
            var updatedLines = new List<string>();

            foreach (var dataLine in cachedLines)
            {
                MoveViewColumnsToNextLine(dataLine, updatedLines);
            }

            return updatedLines;
        }

        private void MoveViewColumnsToNextLine(string dataLine, ICollection<string> updatedLines)
        {
            var startIndex = 0;

            while (!string.IsNullOrWhiteSpace(dataLine))
            {
                var match = mMultiColumnMatcher.Match(dataLine, startIndex);

                if (!match.Success)
                {
                    updatedLines.Add(dataLine);
                    return;
                }

                var firstColumn = match.Groups["FirstColumn"].Value;

                if (firstColumn.IndexOf(" char", StringComparison.OrdinalIgnoreCase) > 0 ||
                    firstColumn.IndexOf(" decimal", StringComparison.OrdinalIgnoreCase) > 0 ||
                    firstColumn.IndexOf(" float", StringComparison.OrdinalIgnoreCase) > 0 ||
                    firstColumn.IndexOf(" nvarchar", StringComparison.OrdinalIgnoreCase) > 0 ||
                    firstColumn.IndexOf(" varchar", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    startIndex = match.Index + 2;
                    if (startIndex >= dataLine.Length)
                    {
                        updatedLines.Add(dataLine);
                        return;
                    }

                    continue;
                }

                startIndex = 0;

                if (match.Index == 0)
                {
                    updatedLines.Add(firstColumn);
                }
                else
                {
                    updatedLines.Add(dataLine.Substring(0, match.Index) + firstColumn);
                }

                var whitespaceMatch = mLeadingWhitespaceMatcher.Match(dataLine);

                var secondColumn = match.Groups["SecondColumn"].Value;
                var additionalText = match.Groups["AdditionalText"].Value;

                if (whitespaceMatch.Success)
                {
                    dataLine = whitespaceMatch.Value + secondColumn + additionalText;
                }
                else
                {
                    dataLine = "       " + secondColumn + additionalText;
                }
            }
        }

        private bool PreserveColumnNamesInView(IEnumerable<string> matchedViews)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var viewName in matchedViews)
            {
                var cleanName = TrimQuotes(GetNameWithoutSchema(viewName));

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var suffix in mOptions.SnakeCaseDisableViewSuffixes.Split(','))
                {
                    if (cleanName.EndsWith(suffix.Trim(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
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

                var addCheckConstraintMatcher = new Regex(@"\s*ALTER TABLE(?<TableName>.+?)ADD CONSTRAINT(?<ConstraintName>.+?)CHECK(?<CheckExpression>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var setDefaultMatcher = new Regex(@"\s*ALTER TABLE(?<TableName>.+?)ALTER COLUMN(?<ConstraintName>.+?)SET DEFAULT(?<DefaultValue>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

                if (!LoadNameMapFiles(columnMapFile, out var tableNameMap, out var columnNameMap, out var viewsToSkip))
                    return false;

                var matchedViews = new List<string>();

                // Keys in this dictionary are table names; values are the order that the table names appear in the view definition
                var referencedTables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Keys in this dictionary are view names
                // Values are the list of updated column names (or column aliases)
                var updatedColumnNamesAndAliases = new Dictionary<string, List<RenamedColumnInfo>>();

                using (var reader = new StreamReader(new FileStream(inputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                using (var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    // ReSharper disable CommentTypo
                    // ReSharper disable StringLiteralTypo

                    // The default search path is: "$user", public
                    // The DMS database customizes this, as shown here

                    // Command "SET search_path" updates the search path for the current session

                    // To update permanently, use:

                    // ALTER DATABASE dms SET search_path=public, sw, cap, dpkg, mc, ont, pc, logdms, logcap, logsw;

                    writer.WriteLine("SET search_path TO public, sw, cap, dpkg, mc, ont, pc, logdms, logcap, logsw;");

                    // ReSharper restore StringLiteralTypo
                    // ReSharper restore CommentTypo

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

                        if (dataLine.IndexOf("Text to find", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Console.WriteLine("Check this code");
                        }

                        if (dataLine.Trim().StartsWith("Create View", StringComparison.OrdinalIgnoreCase))
                        {
                            if (viewsProcessed == 0)
                                AppendFormattingComment(writer);

                            if (cachedLines.Count > 0)
                            {
                                var success = ProcessCachedLines(cachedLines, tableNameMap, columnNameMap, writer, matchedViews, updatedColumnNamesAndAliases, viewsToSkip);
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

                            var checkConstraintMatch = addCheckConstraintMatcher.Match(updatedLine);

                            var setDefaultMatch = setDefaultMatcher.Match(updatedLine);

                            if (checkConstraintMatch.Success)
                            {
                                var tableName = GetNameWithoutSchema(checkConstraintMatch.Groups["TableName"].Value.Trim()).Trim('"');
                                referencedTables.Clear();
                                referencedTables.Add(tableName, 1);

                                updatedLine = NameUpdater.UpdateColumnNames(columnNameMap, referencedTables, updatedLine, false, false);

                                if (updatedLine.Contains("udf_whitespace_chars"))
                                {
                                    // Use the new function name and compare to false instead of 0
                                    updatedLine = updatedLine.Replace("udf_whitespace_chars", "has_whitespace_chars").TrimEnd();

                                    // Switch from: (("has_whitespace_chars"(cart_name,(0))=(0)));
                                    // To:          (("has_whitespace_chars"(cart_name,(0))=false));

                                    if (updatedLine.EndsWith("=(0)));"))
                                    {
                                        updatedLine = updatedLine.Replace("=(0)));", "=false));");
                                    }
                                }
                            }
                            else if (setDefaultMatch.Success)
                            {
                                var tableName = GetNameWithoutSchema(setDefaultMatch.Groups["TableName"].Value.Trim()).Trim('"');
                                referencedTables.Clear();
                                referencedTables.Add(tableName, 1);

                                updatedLine = NameUpdater.UpdateColumnNames(columnNameMap, referencedTables, updatedLine, false, false);
                            }

                            cachedLines.Add(updatedLine);
                            continue;
                        }

                        cachedLines.Add(dataLine);
                    }

                    if (cachedLines.Count > 0)
                    {
                        var success = ProcessCachedLines(cachedLines, tableNameMap, columnNameMap, writer, matchedViews, updatedColumnNamesAndAliases, viewsToSkip);
                        if (!success)
                            return false;

                        cachedLines.Clear();
                    }

                    writer.WriteLine();

                    foreach (var viewName in matchedViews)
                    {
                        writer.WriteLine("SELECT * FROM {0} limit 2;", viewName);
                    }

                    if (mOptions.SnakeCaseColumnAliases && mOptions.CreateRenamedColumnMapFile)
                    {
                        CreateRenamedColumnMapFile(inputFile, updatedColumnNamesAndAliases);
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
        /// <param name="updatedColumnNamesAndAliases">
        /// Dictionary where keys are view names and values are the list of updated column names (or column aliases)
        /// </param>
        /// <param name="viewsToSkip">Names of views to not write to the output file</param>
        private bool ProcessCachedLines(
            List<string> cachedLines,
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            TextWriter writer,
            ICollection<string> matchedViews,
            IDictionary<string, List<RenamedColumnInfo>> updatedColumnNamesAndAliases,
            ICollection<string> viewsToSkip)
        {
            const int MINIMUM_COLUMN_NAME_LENGTH_TO_RENAME = 3;

            // Keys in this list are the original version of the line
            // Values are the updated version
            var updatedLines = new List<KeyValuePair<string, string>>();

            // Keys in this dictionary are table names; values are the order that the table names appear in the view definition
            var referencedTables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var caseKeywordMatcher = new Regex(@"\b(WHEN|THEN|ELSE)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var createViewMatcher = new Regex(@"\s*CREATE VIEW +(?<ViewName>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var createViewAsMatcher = new Regex(@"\s*CREATE VIEW +(?<ViewName>.+) +AS *$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var commaMatcher = new Regex(",", RegexOptions.Compiled);

            var stringConcatenationMatcher1 = new Regex(@"'[\t ]*\+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var stringConcatenationMatcher2 = new Regex(@"\+[\t ]*'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var leadingTabReplacer = new Regex(@"^\t+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var warningMessages = new List<string>();

            // Look for any use of CROSS APPLY or OUTER APPLY
            foreach (var dataLine in cachedLines)
            {
                if (dataLine.IndexOf("cross apply", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    warningMessages.Add("-- This view uses CROSS APPLY, which is not supported by PostgreSQL");
                    warningMessages.Add("-- Consider using INNER JOIN LATERAL instead");
                    warningMessages.Add("-- See also https://stackoverflow.com/a/35873193/1179467 and https://www.postgresql.org/docs/current/sql-select.html");
                    warningMessages.Add(string.Empty);
                    break;
                }

                // ReSharper disable once InvertIf
                if (dataLine.IndexOf("outer apply", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    warningMessages.Add("-- This view uses OUTER APPLY, which is not supported by PostgreSQL");
                    warningMessages.Add("-- Consider using LEFT JOIN LATERAL instead");
                    warningMessages.Add("-- See also https://stackoverflow.com/a/35873193/1179467 and https://www.postgresql.org/docs/current/sql-select.html");
                    warningMessages.Add(string.Empty);
                    break;
                }
            }

            cachedLines = MoveViewColumnsToNextLine(cachedLines);

            var viewNames = new List<string>();
            var viewComments = new List<string>();

            // Look for table names in cachedLines, updating as appropriate
            // First look for the line that starts with "FROM" and then start looking for table names
            // Next, process all of the cached lines

            var fromTableFound = false;

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var dataLine in cachedLines)
            {
                if (dataLine.Trim().StartsWith("--"))
                {
                    continue;
                }

                if (dataLine.Trim().StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    // Found the FROM keyword
                    fromTableFound = true;
                }

                if (fromTableFound)
                {
                    NameUpdater.FindAndUpdateTableNames(tableNameMap, referencedTables, dataLine, true);
                }
            }

            var viewsInCurrentBlock = new List<string>();

            foreach (var dataLine in cachedLines)
            {
                var match1 = createViewAsMatcher.Match(dataLine);
                if (match1.Success)
                {
                    var createViewLine = GetCreateViewLine(match1, out var viewName);

                    if (viewsToSkip.Contains(viewName) || viewsToSkip.Contains(GetNameWithoutSchema(viewName, true)))
                    {
                        OnDebugEvent("Skipping view " + viewName);
                        return true;
                    }

                    AppendCreateView(writer, viewName, createViewLine, matchedViews, viewNames, viewsInCurrentBlock, warningMessages, false);
                    continue;
                }

                var match2 = createViewMatcher.Match(dataLine);
                if (match2.Success)
                {
                    var createViewLine = GetCreateViewLine(match2, out var viewName);

                    if (viewsToSkip.Contains(viewName) || viewsToSkip.Contains(GetNameWithoutSchema(viewName, true)))
                    {
                        OnDebugEvent("Skipping view " + viewName);
                        return true;
                    }

                    AppendCreateView(writer, viewName, createViewLine, matchedViews, viewNames, viewsInCurrentBlock, warningMessages, true);
                    continue;
                }

                if (dataLine.Trim().StartsWith("--"))
                {
                    updatedLines.Add(new KeyValuePair<string, string>(dataLine, dataLine));
                    continue;
                }

                var updatedLine = NameUpdater.FindAndUpdateTableNames(tableNameMap, referencedTables, dataLine, true);

                updatedLines.Add(new KeyValuePair<string, string>(dataLine, updatedLine));
            }

            var renamedColumnsInView = new List<RenamedColumnInfo>();

            var preserveColumnNames = PreserveColumnNamesInView(viewsInCurrentBlock);

            var mostRecentSelectIndices = new Stack<int>();

            fromTableFound = false;

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

                int maxNumberOfRenames;

                if (viewNames.Count > 0 && !fromTableFound)
                {
                    if (caseKeywordMatcher.IsMatch(dataLine.Value))
                    {
                        // Do not limit the number of renames for case statements
                        maxNumberOfRenames = 0;
                    }
                    else if (dataLine.Value.Contains("||"))
                    {
                        // Do not limit the number of renames when text is being concatenated
                        maxNumberOfRenames = 0;
                    }
                    else
                    {
                        var commaMatches = commaMatcher.Matches(dataLine.Value);
                        maxNumberOfRenames = commaMatches.Count + 1;
                    }
                }
                else
                {
                    maxNumberOfRenames = 0;
                }

                var workingCopy = NameUpdater.UpdateColumnNames(
                    columnNameMap, referencedTables, dataLine.Value, true, true,
                    maxNumberOfRenames, MINIMUM_COLUMN_NAME_LENGTH_TO_RENAME, out var renamedColumns);

                workingCopy = PunctuationUpdater.ProcessLine(workingCopy);

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

                if (!fromTableFound && workingCopy.Trim().StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    // Found the FROM keyword; stop looking for column name aliases in this view
                    fromTableFound = true;

                    // Trim the leading whitespace
                    workingCopy = workingCopy.Trim();

                    if (HasSelectKeyword(workingCopy, out var selectKeywordIndex))
                    {
                        mostRecentSelectIndices.Push(selectKeywordIndex);
                    }
                }
                else if (fromTableFound)
                {
                    if (workingCopy.Trim().StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                    {
                        if (mostRecentSelectIndices.Count > 0)
                        {
                            // Align the FROM keyword with the most recent SELECT keyword
                            workingCopy = new string(' ', mostRecentSelectIndices.Pop()) + workingCopy.Trim();
                        }
                    }
                    else if (HasSelectKeyword(workingCopy, out var selectKeywordIndex))
                    {
                        mostRecentSelectIndices.Push(selectKeywordIndex);
                    }
                }

                bool hasColumnAlias;

                if (viewNames.Count > 0 &&
                    mOptions.SnakeCaseColumnAliases &&
                    !fromTableFound &&
                    !workingCopy.Trim().Equals("AS", StringComparison.OrdinalIgnoreCase))
                {
                    var originalColumnName = renamedColumns.Count == 1 ? renamedColumns[0].Key : string.Empty;

                    workingCopy = SnakeCaseColumnAliases(workingCopy, preserveColumnNames, originalColumnName, out hasColumnAlias, out var renamedColumnAliasesInView);

                    AppendRenamedColumns(renamedColumnsInView, renamedColumnAliasesInView);
                }
                else
                {
                    hasColumnAlias = false;
                }

                if (viewNames.Count > 0 && !fromTableFound && !hasColumnAlias)
                {
                    AppendRenamedColumns(renamedColumnsInView, renamedColumns);
                }

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

            if (viewNames.Count > 0)
            {
                List<RenamedColumnInfo> renamedColumnList;

                if (updatedColumnNamesAndAliases.TryGetValue(viewNames[0], out var existingRenamedColumnList))
                {
                    renamedColumnList = existingRenamedColumnList;
                }
                else
                {
                    renamedColumnList = new List<RenamedColumnInfo>();
                    updatedColumnNamesAndAliases.Add(viewNames[0], renamedColumnList);
                }

                renamedColumnList.AddRange(renamedColumnsInView);
            }

            if (viewComments.Count == 0)
                return true;

            foreach (var viewName in viewNames)
            {
                var viewComment = string.Join(". ", viewComments);

                writer.WriteLine("COMMENT ON VIEW {0} IS '{1}';", viewName, viewComment);
            }

            return true;
        }

        private string SnakeCaseColumnAliases(
            string dataLine,
            bool preserveColumnNames,
            string originalColumnName,
            out bool hasColumnAlias,
            out List<KeyValuePair<string, string>> renamedColumnAliasesInView)
        {
            renamedColumnAliasesInView = new List<KeyValuePair<string, string>>();

            var quotedAliasNameMatches = mQuotedAliasNameMatcher.Matches(dataLine);

            var unquotedAliasNameMatches = mUnquotedAliasNameMatcher.Matches(dataLine);

            hasColumnAlias = quotedAliasNameMatches.Count > 0 || unquotedAliasNameMatches.Count > 0;

            if (preserveColumnNames)
            {
                if (quotedAliasNameMatches.Count > 0 || unquotedAliasNameMatches.Count > 0)
                {
                    // The column name already has an alias; leave it unchanged
                    return dataLine;
                }

                if (string.IsNullOrWhiteSpace(originalColumnName))
                    return dataLine;

                // Use the original column name as the alias (if it differs from the new column name)

                var commaMatches = mCommaMatcher.Matches(dataLine);

                var quotedColumnMatches = mQuotedColumnNameMatcher.Matches(dataLine);

                if (quotedColumnMatches.Count > 0)
                {
                    var quotedColumnMatch = quotedColumnMatches[quotedColumnMatches.Count - 1];

                    if (quotedColumnMatch.Groups["ColumnName"].Value.Equals(originalColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Leave the line unchanged
                        return dataLine;
                    }

                    hasColumnAlias = true;

                    if (commaMatches.Count <= 1)
                    {
                        return string.Format("{0} AS {1}{2}",
                            dataLine.TrimEnd().TrimEnd(','), originalColumnName,
                            quotedColumnMatch.Groups["Comma"].Value);
                    }

                    // The line has multiple columns; adding an alias to the end of the line could lead to invalid SQL
                    return string.Format("{0}   -- Use renamed_column AS {1}{2}",
                        dataLine, originalColumnName,
                        quotedColumnMatch.Groups["Comma"].Value);
                }

                var unquotedColumnMatches = mUnquotedColumnNameMatcher.Matches(dataLine);

                if (unquotedColumnMatches.Count == 0)
                    return dataLine;

                var unquotedColumnMatch = unquotedColumnMatches[unquotedColumnMatches.Count - 1];

                if (unquotedColumnMatch.Groups["ColumnName"].Value.Equals(originalColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    // Leave the line unchanged
                    return dataLine;
                }

                hasColumnAlias = true;
                if (commaMatches.Count <= 1)
                {
                    return string.Format("{0} AS {1}{2}",
                        dataLine.TrimEnd().TrimEnd(','), originalColumnName,
                        unquotedColumnMatch.Groups["Comma"].Value);
                }

                // The line has multiple columns; adding an alias to the end of the line could lead to invalid SQL
                return string.Format("{0}   -- Use renamed_column AS {1}{2}",
                    dataLine, originalColumnName,
                    unquotedColumnMatch.Groups["Comma"].Value);
            }

            dataLine = SnakeCaseColumnAliasMatches(dataLine, quotedAliasNameMatches, renamedColumnAliasesInView);

            return SnakeCaseColumnAliasMatches(dataLine, unquotedAliasNameMatches, renamedColumnAliasesInView);
        }

        private string SnakeCaseColumnAliasMatches(
            string dataLine,
            MatchCollection matches,
            ICollection<KeyValuePair<string, string>> renamedColumnAliasesInView)
        {
            foreach (Match aliasMatch in matches)
            {
                var originalName = aliasMatch.Groups["AliasName"].Value;

                var updatedName = ConvertColumnAliasToSnakeCase(originalName);

                if (updatedName.Equals(originalName))
                    continue;

                string replacementText;

                if (updatedName.StartsWith("\""))
                {
                    // The updated name is quoted
                    replacementText = string.Format("{0}{1}{2}", aliasMatch.Groups["ColumnName"], aliasMatch.Groups["As"], updatedName);
                }
                else
                {
                    // The updated name is not quoted

                    // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                    if (aliasMatch.Groups["ColumnName"].Value.Equals(updatedName, StringComparison.OrdinalIgnoreCase))
                    {
                        // The updated name matches the database column name
                        replacementText = aliasMatch.Groups["ColumnName"].Value;
                    }
                    else
                    {
                        replacementText = string.Format("{0}{1}{2}", aliasMatch.Groups["ColumnName"], aliasMatch.Groups["As"], updatedName);
                    }
                }

                dataLine = dataLine.Replace(aliasMatch.Value, replacementText);

                renamedColumnAliasesInView.Add(new KeyValuePair<string, string>(originalName, updatedName));
            }

            return dataLine;
        }

        /// <summary>
        /// If the object name begins and ends with double quotes, remove them
        /// </summary>
        /// <param name="objectName"></param>
        private static string TrimQuotes(string objectName)
        {
            if (objectName.StartsWith("\"") && objectName.EndsWith("\""))
            {
                return objectName.Substring(1, objectName.Length - 2);
            }

            return objectName;
        }
    }
}
