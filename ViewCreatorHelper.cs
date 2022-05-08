﻿using System;
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

        /// <summary>
        /// Match any lowercase letter
        /// </summary>
        private readonly Regex mAnyLowerMatcher;

        /// <summary>
        /// Match a lowercase letter followed by an uppercase letter
        /// </summary>
        private readonly Regex mCamelCaseMatcher;

        /// <summary>
        /// Match any character that is not a letter, number, or underscore
        /// </summary>
        private readonly Regex mColumnCharNonStandardMatcher;

        /// <summary>
        /// This matches alias names surrounded by double quotes
        /// </summary>
        private readonly Regex mQuotedAliasNameMatcher;

        /// <summary>
        /// This matches alias names with characters a-z, 0-9, or underscore
        /// </summary>
        private readonly Regex mUnquotedAliasNameMatcher;

        private readonly ViewCreatorHelperOptions mOptions;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public ViewCreatorHelper(ViewCreatorHelperOptions options)
        {
            mOptions = options;

            mAnyLowerMatcher = new Regex("[a-z]", RegexOptions.Compiled | RegexOptions.Singleline);

            mCamelCaseMatcher = new Regex("(?<LowerLetter>[a-z])(?<UpperLetter>[A-Z])", RegexOptions.Compiled);

            mColumnCharNonStandardMatcher = new Regex("[^a-z0-9_]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

            mQuotedAliasNameMatcher = new Regex("(?<ColumnName>[a-z_]+)?(?<As>[ \t]+AS[ \t]+)\"(?<AliasName>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            mUnquotedAliasNameMatcher = new Regex("(?<ColumnName>[a-z_]+)?(?<As>[ \t]+AS[ \t]+)(?<AliasName>[a-z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
            aliasName = ConvertNameToSnakeCase(aliasName);

            // Return the updated name, quoting if it contains characters other than a-z, 0-9, or underscore
            return mColumnCharNonStandardMatcher.IsMatch(aliasName)
                ? string.Format("\"{0}\"", aliasName)
                : aliasName;
        }

        /// <summary>
        /// Convert the object name to snake_case
        /// </summary>
        /// <param name="objectName"></param>
        private string ConvertNameToSnakeCase(string objectName)
        {
            if (!mAnyLowerMatcher.IsMatch(objectName))
            {
                // objectName contains no lowercase letters; simply change to lowercase and return
                return objectName.ToLower();
            }

            var match = mCamelCaseMatcher.Match(objectName);

            var updatedName = match.Success
                ? mCamelCaseMatcher.Replace(objectName, "${LowerLetter}_${UpperLetter}")
                : objectName;

            return updatedName.ToLower();
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


        /// <summary>
        /// Get the object name, without the schema
        /// </summary>
        /// <remarks>
        /// Simply looks for the first period and assumes the schema name is before the period and the object name is after it
        /// </remarks>
        /// <param name="objectName"></param>
        private static string GetNameWithoutSchema(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return string.Empty;

            var periodIndex = objectName.IndexOf('.');
            if (periodIndex > 0 && periodIndex < objectName.Length - 1)
                return objectName.Substring(periodIndex + 1);

            return objectName;
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

                            var checkConstraintMatch = addCheckConstraintMatcher.Match(updatedLine);

                            var setDefaultMatch = setDefaultMatcher.Match(updatedLine);

                            if (checkConstraintMatch.Success)
                            {
                                var tableName = GetNameWithoutSchema(checkConstraintMatch.Groups["TableName"].Value.Trim()).Trim('"');
                                referencedTables.Clear();
                                referencedTables.Add(tableName);

                                updatedLine = NameUpdater.UpdateColumnNames(columnNameMap, referencedTables, updatedLine, false);

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
                                referencedTables.Add(tableName);

                                updatedLine = NameUpdater.UpdateColumnNames(columnNameMap, referencedTables, updatedLine, false);
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


                if (mOptions.SnakeCaseColumnAliases && !fromTableFound)
                {
                    workingCopy = SnakeCaseColumnAliases(workingCopy, out var renamedColumnAliasesInView);

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

        private string SnakeCaseColumnAliases(string dataLine, out List<KeyValuePair<string, string>> renamedColumnAliasesInView)
        {
            renamedColumnAliasesInView = new List<KeyValuePair<string, string>>();

            dataLine = SnakeCaseColumnAliasMatches(dataLine, mQuotedAliasNameMatcher.Matches(dataLine), renamedColumnAliasesInView);

            dataLine = SnakeCaseColumnAliasMatches(dataLine, mUnquotedAliasNameMatcher.Matches(dataLine), renamedColumnAliasesInView);

            return dataLine;
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
    }
}
