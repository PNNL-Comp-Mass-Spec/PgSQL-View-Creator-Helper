using System.Text.RegularExpressions;

namespace TableColumnNameMapContainer
{
    public class WordReplacer
    {
        // Ignore Spelling: mc, Regex

        /// <summary>
        /// Text to find
        /// </summary>
        public string TextToFind { get; }

        /// <summary>
        /// Replacement text
        /// </summary>
        public string ReplacementText { get; }

        /// <summary>
        /// Schema name to place before the word (if no schema exists)
        /// </summary>
        /// <remarks>
        /// <para>
        /// For example, given
        ///   TextToFind=T_MgrType_ParamType_Map
        ///   ReplacementText="t_mgr_type_param_type_map"
        ///   DefaultSchema=mc
        /// </para>
        /// <para>
        /// If the line being analyzed has: LEFT JOIN T_MgrType_ParamType_Map
        /// it will be changed to:          LEFT JOIN mc."t_mgr_type_param_type_map"
        /// with the mc. added by this class
        /// </para>
        /// <para>
        /// However, if the line has:       mc.T_MgrType_ParamType_Map
        /// It will be changed to:          mc."t_mgr_type_param_type_map"
        /// where the mc. portion is recognized to already be defined
        /// </para>
        /// </remarks>
        public string DefaultSchema { get; }

        /// <summary>
        /// Regex matcher to determine if ReplacementText is preceded by a schema
        /// </summary>
        /// <remarks>This will be null if the class was instantiated with an empty schema name</remarks>
        private readonly Regex mSchemaMatcher;

        /// <summary>
        /// Regex matcher to find TextToFind
        /// </summary>
        private readonly Regex mWordMatcher;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="textToFind"></param>
        /// <param name="replacementText"></param>
        /// <param name="defaultSchema"></param>
        public WordReplacer(string textToFind, string replacementText, string defaultSchema = "")
        {
            TextToFind = textToFind;
            ReplacementText = replacementText;
            DefaultSchema = defaultSchema;

            // Configure the matcher to match whole words, and to be case insensitive
            mWordMatcher = new Regex(@"\b" + textToFind + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            if (string.IsNullOrWhiteSpace(defaultSchema))
            {
                // Default schema is empty
                mSchemaMatcher = null;
            }
            else if (replacementText.Contains("."))
            {
                // The replacement text already has a schema
                mSchemaMatcher = null;
            }
            else
            {
                mSchemaMatcher = new Regex(@"""?[^ ]+""?\.""?" + replacementText + @"""?", RegexOptions.Compiled);
            }
        }

        /// <summary>
        /// Look for TextToFind in dataLine (matching whole words, case sensitive)
        /// If found, replace with ReplacementText
        /// </summary>
        /// <param name="dataLine">Text to search</param>
        /// <param name="updateSchema">When true, add or update the schema associated with the ReplacementText</param>
        /// <param name="updatedLine">Updated line if TextToFind was found; otherwise, an empty string</param>
        /// <returns>True if the line was updated, otherwise false</returns>
        public bool ProcessLine(string dataLine, bool updateSchema, out string updatedLine)
        {
            if (!mWordMatcher.IsMatch(dataLine))
            {
                updatedLine = string.Empty;
                return false;
            }

            updatedLine = mWordMatcher.Replace(dataLine, ReplacementText);

            if (mSchemaMatcher == null || !updateSchema)
                return true;

            if (mSchemaMatcher.Match(updatedLine).Success)
            {
                // A schema is already defined
                return true;
            }

            // Add schema
            updatedLine = updatedLine.Replace(ReplacementText, DefaultSchema + "." + ReplacementText);
            return true;
        }

        /// <summary>
        /// Description of the search and replace text
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} -> {1}", TextToFind, ReplacementText);
        }
    }
}
