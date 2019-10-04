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

        private readonly Regex mColumnAliasMatcher;

        private readonly Dictionary<KeyValuePair<string, string>, Regex> mNameReplacer;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public ViewCreatorHelper(ViewCreatorHelperOptions options)
        {
            mOptions = options;

            mColumnAliasMatcher = new Regex("(?<ColumnInfo>.+ )(?<AliasInfo>AS .+)$", RegexOptions.Compiled);

            mNameReplacer = new Dictionary<KeyValuePair<string, string>, Regex>();
        }

        public bool ProcessInputFile()
        {
            try
            {
                var inputFile = new FileInfo(mOptions.InputScriptFile);
                if (!inputFile.Exists)
                {
                    OnErrorEvent("Input file not found: " + inputFile.FullName);
                    return false;
                }

                if (inputFile.DirectoryName == null)
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

                using (var reader = new StreamReader(new FileStream(inputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                using (var writer = new StreamWriter(new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    while (!reader.EndOfStream)
                    {
                        var dataLine = reader.ReadLine();

                        if (string.IsNullOrWhiteSpace(dataLine))
                        {
                            writer.WriteLine();
                            continue;
                        }


                        if (dataLine.Trim().IndexOf("Create View", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            writer.WriteLine(dataLine);
                            continue;
                        }

                        var success = ProcessViewDDL(reader, writer, dataLine, tableNameMap, columnNameMap);

                        if (!success)
                            return false;
                    }
                }

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
        /// <param name="tableNameMap">Dictionary mapping the original (source) table name to new table name in PostgreSQL</param>
        /// <param name="columnNameMap">Dictionary where keys are new table names, and values are a mapping of original column name to new column name in PostgreSQL</param>
        /// <returns></returns>
        private bool LoadMapFile(
            FileSystemInfo mapFile,
            out Dictionary<string, string> tableNameMap,
            out Dictionary<string, Dictionary<string, string>> columnNameMap)
        {
            var linesRead = 0;

            tableNameMap = new Dictionary<string, string>();
            columnNameMap = new Dictionary<string, Dictionary<string, string>>();

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

                        if (linesRead == 1 && lineParts[0].Equals("SourceTable", StringComparison.OrdinalIgnoreCase) &&
                            lineParts[1].Equals("SourceName", StringComparison.OrdinalIgnoreCase))
                        {
                            // Header line; skip it
                            continue;
                        }

                        var sourceTableName = lineParts[0];
                        var sourceColumnName = lineParts[1];
                        // var schema = lineParts[2];
                        var newTableName = lineParts[3];
                        var newColumnName = lineParts[4];


                        if (!tableNameMap.ContainsKey(sourceTableName))
                        {
                            tableNameMap.Add(sourceTableName, newTableName);
                        }

                        if (!columnNameMap.TryGetValue(newTableName, out var targetTableColumnMap))
                        {
                            targetTableColumnMap = new Dictionary<string, string>();
                            columnNameMap.Add(newTableName, targetTableColumnMap);
                        }

                        if (targetTableColumnMap.Values.Contains(newColumnName))
                        {
                            OnWarningEvent(string.Format("Table {0} has multiple columns with new name {1}", newTableName, newColumnName));
                        }

                        targetTableColumnMap.Add(sourceColumnName, newColumnName);


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

        private bool ProcessViewDDL(
            StreamReader reader,
            TextWriter writer,
            string createViewLine,
            Dictionary<string, string> tableNameMap,
            Dictionary<string, Dictionary<string, string>> columnNameMap
            )
        {

            try
            {
                AppendUpdatedLine(writer, createViewLine, tableNameMap, columnNameMap);

                var parsingSourceColumns = true;
                var parsingJoins = false;
                var parsingWhere = false;

                while (!reader.EndOfStream)
                {
                    var dataLine = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(dataLine))
                    {
                        writer.WriteLine();
                        continue;
                    }

                    var dataLineTrimEnd = dataLine.TrimEnd();

                    if (dataLineTrimEnd.Trim().IndexOf("FROM", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        parsingSourceColumns = false;
                        parsingJoins = true;
                    } else if (dataLineTrimEnd.Trim().IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        parsingSourceColumns = false;
                        parsingJoins = false;
                        parsingWhere = true;
                    }

                    if (parsingSourceColumns)
                    {
                        // Look for a column alias
                        var aliasMatch = mColumnAliasMatcher.Match(dataLineTrimEnd);
                        if (aliasMatch.Success)
                        {
                            AppendUpdatedLine(writer,
                                              aliasMatch.Groups["ColumnInfo"].Value,
                                              tableNameMap,
                                              columnNameMap,
                                              aliasMatch.Groups["AliasInfo"].Value);
                        }
                        else
                        {
                            AppendUpdatedLine(writer,
                                              dataLineTrimEnd,
                                              tableNameMap,
                                              columnNameMap);
                        }

                    }
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    else if (parsingJoins || parsingWhere)
                    {
                        AppendUpdatedLine(writer,
                                          dataLineTrimEnd,
                                          tableNameMap,
                                          columnNameMap);
                    }
                    else {
                        writer.WriteLine(dataLine);
                    }


                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in ProcessViewDDL", ex);
                return false;
            }

        }

        private void AppendUpdatedLine(
            TextWriter writer,
            string ddlText,
            Dictionary<string, string> tableNameMap,
            Dictionary<string, Dictionary<string, string>> columnNameMap,
            string textToAppend = "")
        {
            var updatedLine = string.Copy(ddlText);

            foreach (var item in tableNameMap)
            {
                FindAndReplace(ref updatedLine, item.Key, item.Value);
            }

            foreach (var targetTableName in columnNameMap.Keys)
            {
                foreach (var item in columnNameMap[targetTableName])
                {
                    FindAndReplace(ref updatedLine, item.Key, item.Value);
                }
            }

            if (!string.Equals(ddlText, updatedLine))
            {
                OnDebugEvent(string.Format("Updating {0} to \n         {1}", ddlText, updatedLine));
            }

            if (string.IsNullOrWhiteSpace(textToAppend))
                writer.WriteLine(updatedLine);
            else
                writer.WriteLine(updatedLine + textToAppend);
        }

        private void FindAndReplace(ref string dataLine, string textToFind, string replacementText)
        {

            var keyToFind = new KeyValuePair<string, string>(textToFind, replacementText.Trim('"'));

            if (!mNameReplacer.TryGetValue(keyToFind, out var wordReplacer))
            {
                wordReplacer = new Regex(@"\b" + textToFind + @"\b", RegexOptions.Compiled);
                mNameReplacer.Add(keyToFind, wordReplacer);
            }

            if (wordReplacer.IsMatch(dataLine))
            {
                dataLine = wordReplacer.Replace(dataLine, replacementText.Trim('"'));
            }
        }
    }
}
