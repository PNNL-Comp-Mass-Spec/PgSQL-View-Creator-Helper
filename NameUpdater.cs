using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PgSqlViewCreatorHelper
{
    public class NameUpdater
    {

        private static readonly Regex mAliasMatcher = new Regex(
            @"\[(?<AliasName>[^]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string FindAndUpdateTableNames(
            IReadOnlyDictionary<string, WordReplacer> tableNameMap,
            SortedSet<string> referencedTables,
            string dataLine)
        {

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

            return workingCopy;
        }

        public static string UpdateColumnNames(
            Dictionary<string, Dictionary<string, WordReplacer>> columnNameMap,
            SortedSet<string> referencedTables,
            string dataLine)
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
            if (mAliasMatcher.IsMatch(workingCopy))
            {
                workingCopy = mAliasMatcher.Replace(workingCopy, "\"${AliasName}\"");
            }

            return workingCopy;
        }

    }
}
