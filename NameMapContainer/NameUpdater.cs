using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TableColumnNameMapContainer
{
    public class NameUpdater
    {
        /// <summary>
        /// This is used to find names surrounded by square brackets
        /// </summary>
        private static readonly Regex mAliasMatcher = new Regex(
            @"\[(?<AliasName>[^]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        ///
        /// </summary>
        /// <param name="columnNameMap">
        /// Dictionary where keys are new table names
        /// and values are a Dictionary of mappings of original column names to new column names in PostgreSQL;
        /// names should not have double quotes around them
        /// </param>
        /// <param name="referencedTables">Table names found in the region that contains the data line (using new table names, not the source table name)</param>
        /// <param name="dataLine">Text to examine</param>
        /// <param name="updateSchema">When true, add or update the schema associated with the ReplacementText</param>
        /// <returns></returns>
        public static string UpdateColumnNames(
            Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            SortedSet<string> referencedTables,
            string dataLine,
            bool updateSchema)
        {

            var workingCopy = string.Copy(dataLine);

            foreach (var updatedTableName in referencedTables)
            {
                foreach (var item in columnNameMap)
                {
                    if (!item.Key.Equals(updatedTableName))
                        continue;

                    foreach (var columnNameMatcher in item.Value)
                    {
                        if (columnNameMatcher.Value.ProcessLine(workingCopy, updateSchema, out var updatedLine))
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
            if (mAliasMatcher.IsMatch(workingCopy))
            {
                workingCopy = mAliasMatcher.Replace(workingCopy, "\"${AliasName}\"");
            }

            return workingCopy;
        }

    }
}
