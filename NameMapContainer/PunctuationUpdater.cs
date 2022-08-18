namespace TableColumnNameMapContainer
{
    /// <summary>
    /// This class replaces non-standard punctuation marks (including "smart quotes") with normal punctuation marks
    /// </summary>
    /// <remarks>
    /// From https://stackoverflow.com/a/30262676/1179467
    /// </remarks>
    public static class PunctuationUpdater
    {
        /// <summary>
        /// Replace "smart quotes" and other non-standard punctuation marks with normal punctuation marks
        /// </summary>
        /// <param name="dataLine">Text to process</param>
        /// <param name="replaceEllipsis">When true, replace '…' with "..."</param>
        /// <returns>Updated text</returns>
        public static string ProcessLine(string dataLine, bool replaceEllipsis = true)
        {
            //                                                                                    Punctuation description                 Symbol
            //                                                                                    -----------------------                 ------
            if (dataLine.IndexOf('\u2013') > -1) dataLine = dataLine.Replace('\u2013', '-');   // en dash                                 –
            if (dataLine.IndexOf('\u2014') > -1) dataLine = dataLine.Replace('\u2014', '-');   // em dash                                 —
            if (dataLine.IndexOf('\u2015') > -1) dataLine = dataLine.Replace('\u2015', '-');   // horizontal bar                          ―
            if (dataLine.IndexOf('\u2017') > -1) dataLine = dataLine.Replace('\u2017', '_');   // double low line                         ‗
            if (dataLine.IndexOf('\u2018') > -1) dataLine = dataLine.Replace('\u2018', '\'');  // left single quotation mark              ‘
            if (dataLine.IndexOf('\u2019') > -1) dataLine = dataLine.Replace('\u2019', '\'');  // right single quotation mark             ’
            if (dataLine.IndexOf('\u201a') > -1) dataLine = dataLine.Replace('\u201a', ',');   // single low-9 quotation mark             ‚
            if (dataLine.IndexOf('\u201b') > -1) dataLine = dataLine.Replace('\u201b', '\'');  // single high-reversed-9 quotation mark   ‛
            if (dataLine.IndexOf('\u201c') > -1) dataLine = dataLine.Replace('\u201c', '\"');  // left double quotation mark              “
            if (dataLine.IndexOf('\u201d') > -1) dataLine = dataLine.Replace('\u201d', '\"');  // right double quotation mark             ”
            if (dataLine.IndexOf('\u201e') > -1) dataLine = dataLine.Replace('\u201e', '\"');  // double low-9 quotation mark             „
            if (replaceEllipsis && dataLine.IndexOf('\u2026') > -1) dataLine = dataLine.Replace("\u2026", "..."); // horizontal ellipsis  …
            if (dataLine.IndexOf('\u2032') > -1) dataLine = dataLine.Replace('\u2032', '\'');  // prime                                   ′
            if (dataLine.IndexOf('\u2033') > -1) dataLine = dataLine.Replace('\u2033', '\"');  // double prime                            ″

            return dataLine;
        }
    }
}
