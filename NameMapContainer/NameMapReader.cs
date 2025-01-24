using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PRISM;

namespace TableColumnNameMapContainer
{
    public class NameMapReader : EventNotifier
    {
        // Ignore Spelling: Sql

        /// <summary>
        /// Match any character that is not a letter, number, or underscore
        /// </summary>
        private readonly Regex mColumnCharNonStandardMatcher = new("[^a-z0-9_]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>
        /// Read a five column name map file, which is typically created by sqlserver2pgsql.pl
        /// It is a tab-delimited file with five columns:
        /// SourceTable  SourceName  Schema  NewTable  NewName
        /// </summary>
        /// <param name="columnMapFile">Tab-delimited text file to read</param>
        /// <param name="defaultSchema">Default schema name</param>
        /// <param name="warnDuplicateTargetColumnNames">
        /// If true, warn the user at the console if multiple columns in a table have the same target column name
        /// </param>
        /// <param name="tableNameMap">
        /// Dictionary where keys are the original (source) table names
        /// and values are WordReplacer classes that track the new table names and new column names in PostgreSQL
        /// </param>
        /// <param name="columnNameMap">
        /// Dictionary where keys are new table names
        /// and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
        /// names should not have double quotes around them
        /// </param>
        /// <returns>True if successful, false if an error</returns>
        public bool LoadSqlServerToPgSqlColumnMapFile(
            FileSystemInfo columnMapFile,
            string defaultSchema,
            bool warnDuplicateTargetColumnNames,
            out Dictionary<string, WordReplacer> tableNameMap,
            out Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap)
        {
            var linesRead = 0;

            tableNameMap = new Dictionary<string, WordReplacer>(StringComparer.OrdinalIgnoreCase);
            columnNameMap = new Dictionary<string, Dictionary<string, WordReplacer>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var reader = new StreamReader(new FileStream(columnMapFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

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
                        var newSchemaToUse = string.IsNullOrEmpty(newSchema) ? defaultSchema : newSchema;

                        var replacer = new WordReplacer(sourceTableName, newTableName, newSchemaToUse);
                        tableNameMap.Add(sourceTableName, replacer);
                    }

                    if (!columnNameMap.TryGetValue(newTableName, out var targetTableColumnMap))
                    {
                        targetTableColumnMap = new Dictionary<string, WordReplacer>(StringComparer.OrdinalIgnoreCase);
                        columnNameMap.Add(newTableName, targetTableColumnMap);
                    }

                    if (targetTableColumnMap.ContainsKey(sourceColumnName))
                    {
                        OnWarningEvent(
                            "In file {0}, table {1} has multiple columns with the same source name, {2}",
                            columnMapFile.Name, newTableName, sourceColumnName);

                        continue;
                    }

                    if (targetTableColumnMap.Values.Any(item => item.ReplacementText.Equals(newColumnName))
                        && warnDuplicateTargetColumnNames)
                    {
                        OnWarningEvent(
                            "In file {0}, table {1} has multiple columns with the same new name, {2}",
                            columnMapFile.Name, newTableName, newColumnName);
                    }

                    var columnNameReplacer = new WordReplacer(sourceColumnName, newColumnName);
                    targetTableColumnMap.Add(sourceColumnName, columnNameReplacer);
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
        /// Read a tab-delimited name map file, which is typically sent to DB_Schema_Export_Tool.exe via the ColumnMap parameter when using the ExistingDDL option
        /// It should have three columns:
        /// SourceTableName  SourceColumnName  TargetColumnName
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
        /// <param name="tableNameMapSynonyms">Dictionary mapping original table names to new table names</param>
        /// <returns>True if successful, false if an error</returns>
        public bool LoadTableColumnMapFile(
            FileSystemInfo mapFile,
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            IDictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            IReadOnlyDictionary<string, string> tableNameMapSynonyms)
        {
            var linesRead = 0;

            var missingTablesWarned = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var reader = new StreamReader(new FileStream(mapFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

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

                    string sourceTableNameToUse;

                    if (tableNameMap.ContainsKey(sourceTableName) ||
                        !tableNameMapSynonyms.TryGetValue(sourceTableName, out var alternateTableName))
                    {
                        sourceTableNameToUse = sourceTableName;
                    }
                    else
                    {
                        sourceTableNameToUse = alternateTableName;
                    }

                    // Look for the table in tableNameMap
                    if (!tableNameMap.TryGetValue(sourceTableNameToUse, out var replacer))
                    {
                        if (missingTablesWarned.Contains(sourceTableName))
                            continue;

                        OnWarningEvent(
                            "Table {0} not found in tableNameMap; ignoring column map info for column {1}",
                            sourceTableName, sourceColumnName);

                        missingTablesWarned.Add(sourceTableName);
                        continue;
                    }

                    var newTableName = PossiblyUnquote(replacer.ReplacementText);

                    if (!columnNameMap.TryGetValue(newTableName, out var targetTableColumnMap))
                    {
                        targetTableColumnMap = new Dictionary<string, WordReplacer>(StringComparer.OrdinalIgnoreCase);
                        columnNameMap.Add(newTableName, targetTableColumnMap);
                    }

                    if (targetTableColumnMap.TryGetValue(sourceColumnName, out var existingReplacer))
                    {
                        // The column rename map has already been defined
                        // Show a warning if the new column names differ

                        if (!existingReplacer.ReplacementText.Equals(newColumnName))
                        {
                            OnWarningEvent(
                                "Column mapping already defined for {0} in table {1}; differing new column names: {2} vs. {3}",
                                sourceColumnName, sourceTableName, existingReplacer.ReplacementText, newColumnName);
                        }

                        continue;
                    }

                    var columnNameReplacer = new WordReplacer(sourceColumnName, newColumnName);
                    targetTableColumnMap.Add(sourceColumnName, columnNameReplacer);
                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent(string.Format("Error in LoadTableColumnMapFile, reading line {0}", linesRead), ex);
                return false;
            }
        }

        /// <summary>
        /// If objectName only has letters, numbers, or underscores, remove any double quotes surrounding the name
        /// </summary>
        /// <param name="objectName"></param>
        private string PossiblyUnquote(string objectName)
        {
            var cleanName = objectName.Trim().Trim('"');

            return mColumnCharNonStandardMatcher.IsMatch(cleanName) ? objectName : cleanName;
        }
    }
}
