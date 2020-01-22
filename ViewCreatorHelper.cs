using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PRISM;

namespace PgSqlViewCreatorHelper
{
    public class ViewCreatorHelper : EventNotifier
    {
        private readonly ViewCreatorHelperOptions mOptions;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public ViewCreatorHelper(ViewCreatorHelperOptions options)
        {
            mOptions = options;
        }

        private void AppendCreateView(Match match, TextWriter writer, ICollection<string> matchedViews)
        {
            var viewName = match.Groups["ViewName"].Value;
            var newCreateViewLine = "CREATE OR REPLACE VIEW " + viewName.Trim();
            OnDebugEvent(newCreateViewLine);
            writer.WriteLine(newCreateViewLine);
            matchedViews.Add(viewName);
        }


        /// <summary>
        /// Create a merged column name map file
        /// </summary>
        /// <param name="inputDirectory"></param>
        /// <param name="mapFile">Source Column name map file</param>
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
            FileSystemInfo mapFile,
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            IReadOnlyDictionary<string, Dictionary<string, WordReplacer>> columnNameMap)
        {
            try
            {
                if (inputDirectory == null)
                {
                    return;
                }

                var mergedFileName = Path.GetFileNameWithoutExtension(mapFile.Name) + "_merged" + mapFile.Extension;
                var outputFilePath = Path.Combine(inputDirectory.FullName, mergedFileName);

                OnStatusEvent("Creating " + outputFilePath);

                using (var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    var headerColumns = new List<string> {
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

            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in CreateMergedColumnNameMapFile", ex);
            }

        }

        public bool ProcessInputFile()
        {
            var cachedLines = new List<string>();

            try
            {
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

                var outputFilePath = Path.Combine(inputFile.DirectoryName,
                                                  Path.GetFileNameWithoutExtension(inputFile.Name) + "_updated" + inputFile.Extension);

                var mapFile = new FileInfo(mOptions.ColumnNameMapFile);
                if (!mapFile.Exists)
                {
                    OnErrorEvent("Column name map file not found: " + mapFile.FullName);
                    return false;
                }

                var mapFileLoaded = LoadMapFile(mapFile,
                                                out var tableNameMap,
                                                out var columnNameMap);
                if (!mapFileLoaded)
                    return false;

                if (!string.IsNullOrWhiteSpace(mOptions.ColumnNameMapFile2))
                {
                    var mapFile2 = new FileInfo(mOptions.ColumnNameMapFile2);
                    if (!mapFile2.Exists)
                    {
                        OnErrorEvent("Secondary column name map file not found: " + mapFile2.FullName);
                        return false;
                    }

                    var secondaryMapFileLoaded = LoadSecondaryMapFile(mapFile2, tableNameMap, columnNameMap);
                    if (!secondaryMapFileLoaded)
                        return false;
                }

                var matchedViews = new List<string>();

                using (var reader = new StreamReader(new FileStream(inputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                using (var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    writer.WriteLine("SET search_path TO \"$user\", public, mc;");
                    writer.WriteLine("SHOW search_path;");
                    writer.WriteLine();

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
                            if (cachedLines.Count > 0)
                            {
                                var success = ProcessCachedLines(cachedLines, tableNameMap, columnNameMap, writer, matchedViews);
                                if (!success)
                                    return false;

                                cachedLines.Clear();
                            }

                            cachedLines.Add(dataLine);
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

                    foreach (var viewName in matchedViews)
                    {
                        writer.WriteLine("SELECT * FROM {0};", viewName);
                    }
                }

                CreateMergedColumnNameMapFile(inputFile.Directory, mapFile, tableNameMap, columnNameMap);

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ProcessInputFile", ex);
                return false;
            }

        }

        /// <summary>
        /// Read the column name map file
        /// </summary>
        /// <param name="mapFile">Tab-delimited text file to read</param>
        /// <param name="tableNameMap">Dictionary mapping the original (source) table names to new table names in PostgreSQL</param>
        /// <param name="columnNameMap">Dictionary where keys are new table names, and values are a Dictionary of mappings of original column names to new column names in PostgreSQL; names should not have double quotes around them</param>
        /// <returns></returns>
        private bool LoadMapFile(
            FileSystemInfo mapFile,
            out Dictionary<string, WordReplacer> tableNameMap,
            out Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap)
        {
            var linesRead = 0;

            tableNameMap = new Dictionary<string, WordReplacer>();
            columnNameMap = new Dictionary<string, Dictionary<string, WordReplacer>>();

            try
            {
                using (var reader = new StreamReader(new FileStream(mapFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    while (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();
                        linesRead++;

                        if (string.IsNullOrWhiteSpace(dataLine))
                            continue;

                        var lineParts = dataLine.Split('\t');

                        if (lineParts.Length < 5)
                            continue;

                        if (linesRead == 1 &&
                            lineParts[0].Equals("SourceTable", StringComparison.OrdinalIgnoreCase) &&
                            lineParts[1].Equals("SourceName", StringComparison.OrdinalIgnoreCase))
                        {
                            // Header line; skip it
                            continue;
                        }

                        var sourceTableName = lineParts[0];
                        var sourceColumnName = lineParts[1];
                        var newSchema = lineParts[2];
                        var newTableName = PossiblyUnquote(lineParts[3]);
                        var newColumnName = PossiblyUnquote(lineParts[4]);

                        if (!tableNameMap.ContainsKey(sourceTableName))
                        {
                            string newSchemaToUse;
                            if (string.IsNullOrEmpty(newSchema))
                                newSchemaToUse = mOptions.DefaultSchema;
                            else
                                newSchemaToUse = newSchema;

                            var replacer = new WordReplacer(sourceTableName, newTableName, newSchemaToUse);
                            tableNameMap.Add(sourceTableName, replacer);
                        }

                        if (!columnNameMap.TryGetValue(newTableName, out var targetTableColumnMap))
                        {
                            targetTableColumnMap = new Dictionary<string, WordReplacer>();
                            columnNameMap.Add(newTableName, targetTableColumnMap);
                        }

                        if (targetTableColumnMap.Values.Any(item => item.ReplacementText.Equals(newColumnName)))
                        {
                            OnWarningEvent(string.Format("Table {0} has multiple columns with new name {1}", newTableName, newColumnName));
                        }

                        var columnNameReplacer = new WordReplacer(sourceColumnName, newColumnName);
                        targetTableColumnMap.Add(sourceColumnName, columnNameReplacer);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent(string.Format("Error in LoadMapFile, reading line {0}", linesRead), ex);
                return false;
            }

        }

        /// <summary>
        /// Load a secondary map file
        /// </summary>
        /// <param name="mapFile">Tab-delimited text file to read</param>
        /// <param name="tableNameMap">
        /// Dictionary where keys are the original (source) table names
        /// and values are WordReplacer classes that track the new table names and new column names in PostgreSQL
        /// </param>
        /// <param name="columnNameMap">
        /// Dictionary where keys are new table names
        /// and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
        /// names should not have double quotes around them
        /// </param>
        /// <returns></returns>
        private bool LoadSecondaryMapFile(
            FileSystemInfo mapFile,
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            IDictionary<string, Dictionary<string, WordReplacer>> columnNameMap
            )
        {
            var linesRead = 0;

            var missingTablesWarned = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var reader = new StreamReader(new FileStream(mapFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    while (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();
                        linesRead++;

                        if (string.IsNullOrWhiteSpace(dataLine))
                            continue;

                        var lineParts = dataLine.Split('\t');

                        if (lineParts.Length < 3)
                            continue;

                        if (linesRead == 1 &&
                            lineParts[0].Equals("SourceTableName", StringComparison.OrdinalIgnoreCase) &&
                            lineParts[1].Equals("SourceColumnName", StringComparison.OrdinalIgnoreCase))
                        {
                            // Header line; skip it
                            continue;
                        }

                        var sourceTableName = lineParts[0];
                        var sourceColumnName = lineParts[1];
                        var newColumnName = PossiblyUnquote(lineParts[2]);

                        // Look for the table in tableNameMap
                        if (!tableNameMap.TryGetValue(sourceTableName, out var replacer))
                        {
                            if (missingTablesWarned.Contains(sourceTableName))
                                continue;

                            OnWarningEvent(string.Format(
                                "Table {0} not found in tableNameMap; ignoring column map info for column {1}",
                                sourceTableName, sourceColumnName));

                            missingTablesWarned.Add(sourceTableName);
                            continue;
                        }

                        var newTableName = PossiblyUnquote(replacer.ReplacementText);

                        if (!columnNameMap.TryGetValue(newTableName, out var targetTableColumnMap))
                        {
                            targetTableColumnMap = new Dictionary<string, WordReplacer>();
                            columnNameMap.Add(newTableName, targetTableColumnMap);
                        }

                        if (targetTableColumnMap.ContainsKey(sourceColumnName))
                        {
                            // The column rename map has already been defined; this is OK
                            OnDebugEvent(string.Format("Column mapping already defined for {0} in table {1}", sourceColumnName, sourceTableName));
                            continue;
                        }

                        var columnNameReplacer = new WordReplacer(sourceColumnName, newColumnName);
                        targetTableColumnMap.Add(sourceColumnName, columnNameReplacer);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent(string.Format("Error in LoadSecondaryMapFile, reading line {0}", linesRead), ex);
                return false;
            }

        }

        /// <summary>
        /// If objectName does not contain any spaces, remove the double quotes surrounding it
        /// </summary>
        /// <param name="objectName"></param>
        /// <returns></returns>
        private string PossiblyUnquote(string objectName)
        {
            if (objectName.Contains(' '))
                return objectName;

            return objectName.Trim('"');
        }

        /// <summary>
        /// Looks for table names in cachedLines, then uses that information to update column names
        /// Writes the updated text to disk
        /// </summary>
        /// <param name="cachedLines"></param>
        /// <param name="tableNameMap">Dictionary mapping the original (source) table names to new table names in PostgreSQL</param>
        /// <param name="columnNameMap">Dictionary where keys are new table names, and values are a Dictionary of mappings of original column names to new column names in PostgreSQL</param>
        /// <param name="writer"></param>
        /// <param name="matchedViews">List of matched view names</param>
        /// <returns></returns>
        private bool ProcessCachedLines(
            IEnumerable<string> cachedLines,
            Dictionary<string, WordReplacer> tableNameMap,
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

            var aliasMatcher = new Regex(@"\[(?<AliasName>[^]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var stringConcatenationMatcher1 = new Regex(@"' *\+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var stringConcatenationMatcher2 = new Regex(@"\+ *'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Look for table names in cachedLines, updating as appropriate
            foreach (var dataLine in cachedLines)
            {
                var match1 = createViewAsMatcher.Match(dataLine);
                if (match1.Success)
                {
                    AppendCreateView(match1, writer, matchedViews);
                    writer.WriteLine("AS");
                    continue;
                }

                var match2 = createViewMatcher.Match(dataLine);
                if (match2.Success)
                {
                    AppendCreateView(match2, writer, matchedViews);
                    continue;
                }

                var workingCopy = string.Copy(dataLine);

                foreach (var item in tableNameMap)
                {
                    if (!item.Value.ProcessLine(workingCopy, out var updatedLine))
                        continue;

                    // A match to a table name was found
                    workingCopy = updatedLine;

                    var updatedTableName = item.Value.ReplacementText;
                    if (!referencedTables.Contains(updatedTableName))
                    {
                        referencedTables.Add(updatedTableName);
                    }
                }

                updatedLines.Add(new KeyValuePair<string, string>(dataLine, workingCopy));
            }

            // Look for column names in updatedLines, updating as appropriate
            foreach (var dataLine in updatedLines)
            {
                var originalLine = dataLine.Key;
                var workingCopy = string.Copy(dataLine.Value);

                foreach (var updatedTableName in referencedTables)
                {
                    foreach (var item in columnNameMap)
                    {
                        if (!item.Key.Equals(updatedTableName))
                            continue;

                        foreach (var columnNameMatcher in item.Value)
                        {
                            if (columnNameMatcher.Value.ProcessLine(workingCopy, out var updatedLine))
                            {
                                workingCopy = updatedLine;
                            }

                        }
                    }
                }

                // Replace square bracket delimited names with double quote delimited names
                // For example, change
                // value as [The Value]
                // to
                // value as "The Value"
                if (aliasMatcher.IsMatch(workingCopy))
                {
                    workingCopy = aliasMatcher.Replace(workingCopy, "\"${AliasName}\"");
                }

                // Use || for string concatenation, instead of +
                if (stringConcatenationMatcher1.IsMatch(workingCopy))
                {
                    workingCopy = stringConcatenationMatcher1.Replace(workingCopy, "' ||");
                }

                if (stringConcatenationMatcher2.IsMatch(workingCopy))
                {
                    workingCopy = stringConcatenationMatcher2.Replace(workingCopy, "|| '");
                }

                if (originalLine.Equals(workingCopy))
                {
                    writer.WriteLine(originalLine);
                    continue;
                }

                OnDebugEvent(string.Format("Updating {0} \n        to {1}", originalLine, workingCopy));
                writer.WriteLine(workingCopy);
            }

            return true;
        }

    }
}
