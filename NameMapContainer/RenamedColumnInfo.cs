namespace TableColumnNameMapContainer
{
    public class RenamedColumnInfo
    {
        public bool IsColumnAlias { get; }

        public string NewColumnName { get; }

        public string OriginalColumnName { get; }

        public RenamedColumnInfo(string originalColumnName, string newColumnName, bool isColumnAlias = false)
        {
            OriginalColumnName = originalColumnName;

            NewColumnName = newColumnName;

            IsColumnAlias = isColumnAlias;
        }

        public override string ToString()
        {
            return string.Format("{0} -> {1}", OriginalColumnName, NewColumnName);
        }
    }
}
