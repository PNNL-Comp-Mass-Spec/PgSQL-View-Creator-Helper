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
  /Map:ColumnNameMapFile
  [/Map2:SecondaryColumnMapFile]
  [/TableNameMap:TableNameMapFile]
  [/Schema:DefaultSchemaName]
  [/V]
  [/ParamFile:ParamFileName.conf] [/CreateParamFile]
```

The input file should be a SQL text file with CREATE VIEW statements
* The program is primarily designed to update the DBName_unsure.sql file created by Perl script sqlserver2pgsql.pl (https://github.com/PNNL-Comp-Mass-Spec/sqlserver2pgsql) 
* Example input file statements:

```PLpgSQL
ALTER TABLE "public"."t_campaign" ADD CONSTRAINT "ck_t_campaign_campaign_name_white_space" CHECK ((dbo].[udf_whitespace_chars]([campaign_num,(1))=(0)));
ALTER TABLE "public"."t_dataset" ADD CONSTRAINT "ck_t_dataset_dataset_name_not_empty" CHECK ((COALESCE(dataset_num,'')<>''));
ALTER TABLE "public"."t_dataset" ADD CONSTRAINT "ck_t_dataset_dataset_name_white_space" CHECK ((dbo].[udf_whitespace_chars]([dataset_num,(0))=(0)));
ALTER TABLE "public"."t_dataset" ADD CONSTRAINT "ck_t_dataset_ds_folder_name_not_empty" CHECK ((COALESCE(ds_folder_name,'')<>''));

CREATE VIEW "public"."v_user_entry" 
AS
SELECT U_PRN AS Username,
       U_HID AS HanfordIDNum,
       'Last Name, First Name, and Email are auto-updated when "User Update" = Y' As EntryNote,
       -- Obsolete: U_Payroll AS Payroll,
       U_Name AS LastNameFirstName,
       U_email as Email,
       U_Status AS UserStatus,
       U_update AS UserUpdate,
       public.GetUserOperationsList(ID) AS OperationsList,
       U_comment AS Comment
	FROM public.T_Users
;
```

* Example output file statements:
```PLpgSQL
ALTER TABLE "public"."t_campaign" ADD CONSTRAINT "ck_t_campaign_campaign_name_white_space" CHECK (("udf_whitespace_chars"(campaign,(1))=(0)));
ALTER TABLE "public"."t_dataset" ADD CONSTRAINT "ck_t_dataset_dataset_name_not_empty" CHECK ((COALESCE(dataset,'')<>''));
ALTER TABLE "public"."t_dataset" ADD CONSTRAINT "ck_t_dataset_dataset_name_white_space" CHECK (("udf_whitespace_chars"(dataset,(0))=(0)));
ALTER TABLE "public"."t_dataset" ADD CONSTRAINT "ck_t_dataset_ds_folder_name_not_empty" CHECK ((COALESCE(folder_name,'')<>''));

CREATE OR REPLACE VIEW "public"."v_user_entry"
AS
SELECT u_prn AS Username,
       u_hid AS HanfordIDNum,
       'Last Name, First Name, and Email are auto-updated when "User Update" = Y' As EntryNote,
       u_name AS LastNameFirstName,
       u_email as Email,
       u_status AS UserStatus,
       u_update AS UserUpdate,
       public.GetUserOperationsList(user_id) AS OperationsList,
       u_comment AS Comment
    FROM public.t_users
;

COMMENT ON VIEW "public"."v_user_entry" IS 'Obsolete: U_Payroll AS Payroll,';
```


The `/Map` file is is a tab-delimited text file with five columns
* The Map file matches the format of the NameMap file created by Perl script sqlserver2pgsql.pl
* Example data:

| SourceTable   | SourceName           | Schema | NewTable        | NewName                 |
|---------------|----------------------|--------|-----------------|-------------------------|
| T_Log_Entries | Entry_ID             | mc     | "t_log_entries" | "entry_id"              |
| T_Log_Entries | posted_by            | mc     | "t_log_entries" | "posted_by"             |
| T_Log_Entries | posting_time         | mc     | "t_log_entries" | "posting_time"          |
| T_Log_Entries | type                 | mc     | "t_log_entries" | "type"                  |
| T_Log_Entries | message              | mc     | "t_log_entries" | "message"               |
| T_Log_Entries | Entered_By           | mc     | "t_log_entries" | "entered_by"            |
| T_Mgrs        | m_id                 | mc     | "t_mgrs"        | "mgr_id"                |
| T_Mgrs        | m_name               | mc     | "t_mgrs"        | "mgr_name"              |
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


Use `/TableNameMap` (or `/TableNames`) to optionally specify a tab-delimited text file listing old and new names for renamed tables
* The Table Name Map file matches the file defined for the `DataTables` parameter when using the DB Schema Export Tool (https://github.com/PNNL-Comp-Mass-Spec/DB-Schema-Export-Tool) to pre-process an existing DDL file
  * The text file must include columns `SourceTableName` and `TargetTableName`
* Example data (showing additional columns that are used by the DB Schema Export Tool, but are ignored by this program)

| SourceTableName        | TargetSchemaName | TargetTableName       | PgInsert  | KeyColumn(s)      |
|------------------------|------------------|-----------------------|-----------|-------------------|
| T_Analysis_State_Name  | public           | t_analysis_job_state  | true      | job_state_id      |
| T_DatasetRatingName    | public           | t_dataset_rating_name | true      | dataset_rating_id |
| T_Log_Entries          | public           | t_log_entries         | false     |                   |
| T_Job_Events           | cap              | t_job_Events          | false     |                   |
| T_Job_State_Name       | cap              | t_job_state_name      | true      | job               |
| T_Users                | public           | t_users               | true      | user_id           |


Use `/Schema` to specify a default schema name to add before all table names (that don't already have a schema name prefix)

Use `/V` to enable verbose mode, displaying the old and new version of each updated line

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
