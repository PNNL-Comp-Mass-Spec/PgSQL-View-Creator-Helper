using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TableColumnNameMapContainer
{
    public static class NameUpdater
    {
        /// <summary>
        /// This is used to find names surrounded by square brackets
        /// </summary>
        /// <remarks>The brackets will be changed to double quotes</remarks>
        private static readonly Regex mAliasMatcher = new(@"\[(?<AliasName>[^]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// This is used to match expressions of the form
        /// WHERE identifier LIKE '[0-9]%'
        /// </summary>
        /// <remarks>If a match is found, switch from LIKE to SIMILAR TO</remarks>
        private static readonly Regex mLikeMatcher = new(@"\bLIKE(?<ComparisonSpec>\s*'.*\[[^]]+\].*')", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Look for known table names in the data line
        /// Any that are found are added to referencedTables
        /// </summary>
        /// <param name="tableNameMap">
        /// Dictionary where keys are the original (source) table names
        /// and values are WordReplacer classes that track the new table names and new column names in PostgreSQL
        /// </param>
        /// <param name="referencedTables">Table names found in the region that contains the data line (using new table names, not the source table name)</param>
        /// <param name="dataLine">Text to examine</param>
        /// <param name="updateSchema">When true, add or update the schema associated with the ReplacementText</param>
        /// <returns>Updated line with new table names</returns>
        public static string FindAndUpdateTableNames(
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            SortedSet<string> referencedTables,
            string dataLine,
            bool updateSchema)
        {
            var workingCopy = string.Copy(dataLine);

            foreach (var item in tableNameMap)
            {
                if (!item.Value.ProcessLine(workingCopy, updateSchema, out var updatedLine))
                    continue;

                // A match to a table name was found
                workingCopy = updatedLine;

                var updatedTableName = item.Value.ReplacementText;
                if (!referencedTables.Contains(updatedTableName))
                {
                    referencedTables.Add(updatedTableName);
                }
            }

            return workingCopy;
        }

        /// <summary>
        /// Update column names in dictionary columnNameMap
        /// </summary>
        /// <param name="columnNameMap">
        /// Dictionary where keys are new table names
        /// and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
        /// names should not have double quotes around them
        /// </param>
        /// <param name="referencedTables">Table names found in the region that contains the data line (using new table names, not the source table name)</param>
        /// <param name="dataLine">Text to examine</param>
        /// <param name="updateSchema">When true, add or update the schema associated with the ReplacementText</param>
        public static string UpdateColumnNames(
            Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            SortedSet<string> referencedTables,
            string dataLine,
            bool updateSchema)
        {
            var workingCopy = string.Copy(dataLine);

            foreach (var updatedTableName in referencedTables)
            {
                if (!columnNameMap.TryGetValue(updatedTableName, out var nameMapping))
                {
                    // Column not found; this will happen if the ColumnNameMapFile does not contain every column in the target database
                    continue;
                }

                foreach (var columnNameMatcher in nameMapping)
                {
                    if (!columnNameMatcher.Value.ProcessLine(workingCopy, updateSchema, out var updatedLine))
                        continue;

                    if (updatedLine.Contains(TableNameMapContainer.NameMapReader.SKIP_FLAG))
                    {
                        // The data line contains a skipped column (meaning the column does not exist in the target database)
                        // Change the line to a SQL comment

                        var leadingWhitespaceMatcher = new Regex("^[\t ]+", RegexOptions.Compiled);
                        var match = leadingWhitespaceMatcher.Match(workingCopy);

                        workingCopy = string.Format("{0}-- Remove or update since skipped column: {1}", match.Success ? match.Value : "    ", workingCopy.Trim());
                        break;
                    }

                    workingCopy = updatedLine;
                }
            }

            if (mLikeMatcher.IsMatch(workingCopy))
            {
                // Switch from LIKE to SIMILAR TO
                return mLikeMatcher.Replace(workingCopy, "SIMILAR TO${ComparisonSpec}");
            }

            // Replace square bracket delimited names with double quote delimited names
            // For example, change
            // value as [The Value]
            // to
            // value as "The Value"
            if (mAliasMatcher.IsMatch(workingCopy))
            {
                workingCopy = mAliasMatcher.Replace(workingCopy, "\"${AliasName}\"");
            }

            return workingCopy;
        }
    }
}
