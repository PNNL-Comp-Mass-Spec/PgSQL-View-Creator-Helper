namespace TableColumnNameMapContainer
{
    public class RenamedColumnInfo
    {
        public string NewColumnName { get; }

        public string OriginalColumnName { get; }

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
