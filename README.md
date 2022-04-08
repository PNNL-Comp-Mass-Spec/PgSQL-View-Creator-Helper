# PgSQL View Creator Helper

This program processes a SQL DDL file with CREATE VIEW commands and renames the
column and table names referenced by the views to use new names defined in a
mapping file (or files).

In addition, it creates a new `ColumnNameMap_merged.txt` file that merges the contents of the `/Map` and `/Map2` files
* The five columns in the merged file are the same as in the `/Map` file

## Console Switches

PgSqlViewCreatorHelper is a console application, and must be run from the Windows command prompt.

```
PgSqlViewCreatorHelper.exe
  /I:InputFilePath
  /Map:MapFilePath
  [/Map2:SecondaryMapFilePath]
  [/Schema:DefaultSchemaName]
  [/ParamFile:ParamFileName.conf] [/CreateParamFile]
```

The input file should be a SQL text file with CREATE VIEW statements

The `/Map` file is is a tab-delimited text file with five columns
* The Map file matches the format of the NameMap file created by sqlserver2pgsql (https://github.com/PNNL-Comp-Mass-Spec/sqlserver2pgsql)
* Example data:

| SourceTable   | SourceName           | Schema | NewTable        | NewName                 |
|---------------|----------------------|--------|-----------------|-------------------------|
| T_Log_Entries | Entry_ID             | mc     | "t_log_entries" | "entry_id"              |
| T_Log_Entries | posted_by            | mc     | "t_log_entries" | "posted_by"             |
| T_Log_Entries | posting_time         | mc     | "t_log_entries" | "posting_time"          |
| T_Log_Entries | type                 | mc     | "t_log_entries" | "type"                  |
| T_Log_Entries | message              | mc     | "t_log_entries" | "message"               |
| T_Log_Entries | Entered_By           | mc     | "t_log_entries" | "entered_by"            |
| T_Mgrs        | m_id                 | mc     | "t_mgrs"        | "mgr_id"                  |
| T_Mgrs        | m_name               | mc     | "t_mgrs"        | "mgr_name"                |
| T_Mgrs        | mgr_type_id          | mc     | "t_mgrs"        | "mgr_type_id"           |
| T_Mgrs        | param_value_changed  | mc     | "t_mgrs"        | "param_value_changed"   |
| T_Mgrs        | control_from_website | mc     | "t_mgrs"        | "control_from_website"  |
| T_Mgrs        | comment              | mc     | "t_mgrs"        | "comment"               |


Use `/Map2` to optionally specify a secondary map file, which is a tab-delimited text file with three columns
* The Secondary Map file matches the file defined for the `ColumnMap` parameter when using the DB Schema Export Tool (https://github.com/PNNL-Comp-Mass-Spec/DB-Schema-Export-Tool) to pre-process an existing DDL file
* Example data:

| SourceTableName         | SourceColumnName     | TargetColumnName     |
|-------------------------|----------------------|----------------------|
| T_MgrTypes              | mt_typeid            | mgr_type_id          |
| T_MgrTypes              | mt_typename          | mgr_type_name        |
| T_MgrTypes              | mt_active            | mgr_type_active      |
| T_MgrType_ParamType_Map | MgrTypeID            | mgr_type_id          |
| T_MgrType_ParamType_Map | ParamTypeID          | param_type_id        |
| T_Mgrs                  | M_ID                 | mgr_id               |
| T_Mgrs                  | M_Name               | mgr_name             |
| T_Mgrs                  | M_TypeID             | mgr_type_id          |
| T_Mgrs                  | M_ParmValueChanged   | param_value_changed  |
| T_Mgrs                  | M_ControlFromWebsite | control_from_website |
| T_Mgrs                  | M_Comment            | comment              |


Use `/Schema` to specify a default schema name to add before all table names (that don't already have a schema name prefix)

The processing options can be specified in a parameter file using `/ParamFile:Options.conf` or `/Conf:Options.conf`
* Define options using the format `ArgumentName=Value`
* Lines starting with `#` or `;` will be treated as comments
* Additional arguments on the command line can supplement or override the arguments in the parameter file

Use `/CreateParamFile` to create an example parameter file
* By default, the example parameter file content is shown at the console
* To create a file named Options.conf, use `/CreateParamFile:Options.conf`

## Contacts

Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov\
Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://panomics.pnnl.gov/ or https://www.pnnl.gov/integrative-omics/
Source code: https://github.com/PNNL-Comp-Mass-Spec/PgSQL-View-Creator-Helper

## License

Licensed under the 2-Clause BSD License; you may not use this program except
in compliance with the License.  You may obtain a copy of the License at
https://opensource.org/licenses/BSD-2-Clause

Copyright 2019 Battelle Memorial Institute
