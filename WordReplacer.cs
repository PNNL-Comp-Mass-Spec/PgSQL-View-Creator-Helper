using System.Text.RegularExpressions;

namespace PgSqlViewCreatorHelper
{
    internal class WordReplacer
    {
        /// <summary>
        /// Text to find
        /// </summary>
        public string TextToFind { get; }

        /// <summary>
        /// Replacement text
        /// </summary>
        public string ReplacementText { get; }

        /// <summary>
        /// Regex matcher
        /// </summary>
        private readonly Regex mMatcher;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="textToFind"></param>
        /// <param name="replacementText"></param>
        public WordReplacer(string textToFind, string replacementText)
        {
            TextToFind = textToFind;
            ReplacementText = replacementText;

            // Configure the matcher to match whole words, and to be case sensitive
            mMatcher = new Regex(@"\b" + textToFind + @"\b", RegexOptions.Compiled);
        }

        /// <summary>
        /// Look for TextToFind in dataLine (matching whole words, case sensitive)
        /// If found, replace with ReplacementText
        /// </summary>
        /// <param name="dataLine">Text to search</param>
        /// <param name="updatedLine">Updated line if TextToFind was found; otherwise, an empty string</param>
        /// <returns>True if the line was updated, otherwise false</returns>
        public bool ProcessLine(string dataLine, out string updatedLine)
        {
            if (!mMatcher.IsMatch(dataLine))
            {
                updatedLine = string.Empty;
                return false;
            }

            updatedLine = mMatcher.Replace(dataLine, ReplacementText);
            return true;

        }

        /// <summary>
        /// Description of the search and replace text
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("{0} -> {1}", TextToFind, ReplacementText);
        }
    }
}
