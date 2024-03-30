namespace TableColumnNameMapContainer
{
    public class RenamedColumnInfo
    {
        /// <summary>
        /// Original column name
        /// </summary>
        public string NewColumnName { get; }

        /// <summary>
        /// New column name
        /// </summary>
        public string OriginalColumnName { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="originalColumnName">Original column name</param>
        /// <param name="newColumnName">New column name</param>
        public RenamedColumnInfo(string originalColumnName, string newColumnName)
        {
            OriginalColumnName = originalColumnName;

            NewColumnName = newColumnName;
        }

        public override string ToString()
        {
            return string.Format("{0} -> {1}", OriginalColumnName, NewColumnName);
        }
    }
}
