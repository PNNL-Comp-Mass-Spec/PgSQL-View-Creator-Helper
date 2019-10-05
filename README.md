# PgSQL View Creator Helper

This program process a SQL DDL file with CREATE VIEW commands and renames the
column and table names referenced by the views to use new names defined in a
mapping file.

## Console Switches

PgSqlViewCreatorHelper. is a console application, and must be run from the Windows command prompt.

```
PgSqlViewCreatorHelper.exe
  /I:InputFilePath
  /M:MapFilePath
```

The input file should be a SQL text file with CREATE VIEW statements

The Map file is a tab-delimited text file with five columns, for example:

| SourceTable | SourceName  | Schema      | NewTable    | NewName     |
|-------------|-------------|-------------|-------------|-------------|
| T_Log_Entries | Entry_ID | mc | "t_log_entries" | "entry_id" |
| T_Log_Entries | posted_by | mc | "t_log_entries" | "posted_by" |
| T_Log_Entries | posting_time | mc | "t_log_entries" | "posting_time" |
| T_Log_Entries | type | mc | "t_log_entries" | "type" |
| T_Log_Entries | message | mc | "t_log_entries" | "message" |
| T_Log_Entries | Entered_By | mc | "t_log_entries" | "entered_by" |
| T_Mgrs | M_ID | mc | "t_mgrs" | "m_id" |
| T_Mgrs | M_Name | mc | "t_mgrs" | "m_name" |
| T_Mgrs | M_TypeID | mc | "t_mgrs" | "m_type_id" |
| T_Mgrs | M_ParmValueChanged | mc | "t_mgrs" | "m_parm_value_changed" |
| T_Mgrs | M_ControlFromWebsite | mc | "t_mgrs" | "m_control_from_website" |
| T_Mgrs | M_Comment | mc | "t_mgrs" | "m_comment" |

The Map file matches the format created by https://github.com/PNNL-Comp-Mass-Spec/sqlserver2pgsql

## Contacts

Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com\
Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/

## License

Licensed under the 2-Clause BSD License; you may not use this file except
in compliance with the License.  You may obtain a copy of the License at
https://opensource.org/licenses/BSD-2-Clause

Copyright 2019 Battelle Memorial Institute
