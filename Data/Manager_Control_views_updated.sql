\set ON_ERROR_STOP
BEGIN;
ALTER TABLE "mc"."t_auth_database_logins_and_roles" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_auth_database_logins_and_roles" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_auth_database_permissions" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_auth_database_permissions" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_auth_server_logins" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_auth_server_logins" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_event_log" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_event_log" ALTER COLUMN "entered_by" SET DEFAULT suser_sname();
ALTER TABLE "mc"."t_log_entries" ALTER COLUMN "entered_by" SET DEFAULT suser_sname();
ALTER TABLE "mc"."t_log_entries" ALTER COLUMN "posting_time" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_mgr_type_param_type_map" ALTER COLUMN "entered_by" SET DEFAULT suser_sname();
ALTER TABLE "mc"."t_mgr_type_param_type_map" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_param_value" ALTER COLUMN "entered_by" SET DEFAULT suser_sname();
ALTER TABLE "mc"."t_param_value" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_usage_log" ALTER COLUMN "posting_time" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_usage_stats" ALTER COLUMN "last_posting_time" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."x_t_mgr_state" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
CREATE VIEW "mc"."v_param_value" 
AS
SELECT M.m_name,
       PT.param_name,
       PV.entry_id,
       PV.type_id,
       PV.value,
       PV.mgr_id,
       PV.comment,
       PV.last_affected,
       PV.entered_by,
       M.m_type_id
	FROM t_param_value PV
     INNER JOIN t_mgrs M
       ON PV.mgr_id = M.m_id
     INNER JOIN t_param_type PT
       ON PV.type_id = PT.param_id

;

CREATE VIEW "mc"."v_mgr_work_dir" AS
-- This database does not keep track of the server name that a given manager is running on
-- Thus, this query includes the generic text ServerName for the WorkDir path, unless the WorkDir is itself a network share
SELECT M_Name,
       CASE
           WHEN VALUE LIKE '\\%' THEN VALUE
           ELSE '\\ServerName\' + Replace(VALUE, ':\', '$\')
       END AS WorkDir_AdminShare
FROM V_ParamValue
WHERE (ParamName = 'workdir')



;

CREATE VIEW "mc"."v_manager_list_by_type" 
AS
SELECT M.m_id AS ID, M.m_name AS [Manager Name],
    MT.mt_type_name AS [Manager Type], COALESCE(ActiveQ.Active,
    'not defined') AS Active, M.m_type_id,
    ActiveQ.last_affected AS [State Last Changed],
    ActiveQ.entered_by AS [Changed By],
	M.m_comment AS [comment]
FROM mc.t_mgrs AS M INNER JOIN
    mc.t_mgr_types AS MT ON
    M.m_type_id = MT.mt_type_id LEFT OUTER JOIN
        (SELECT PV.mgr_id, PV.value AS Active, PV.last_affected,
           PV.entered_by

	FROM mc.t_param_value AS PV INNER JOIN
           mc.t_param_type AS PT ON
           PV.type_id = PT.param_id
      WHERE (PT.param_name = 'mgractive')) AS ActiveQ ON
    M.m_id = ActiveQ.mgr_id
WHERE (M.m_control_from_website > 0)

;

CREATE VIEW "mc"."v_manager_type_report_ex" 
AS
SELECT MT.mt_type_name AS [Manager Type], MT.mt_type_id AS ID, COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS [Manager Count Active],
                      COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS [Manager Count Inactive]
FROM         mc.t_mgr_types AS MT LEFT OUTER JOIN
                          (SELECT     M_TypeID, [Manager Type], SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                                                   SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive

	FROM mc.V_Manager_List_By_Type
                            GROUP BY M_TypeID, [Manager Type]) AS ActiveManagersQ ON MT.mt_type_id = ActiveManagersQ.M_TypeID
;

CREATE VIEW "mc"."v_tuning_query_execution_stats" 
AS
SELECT QS.total_worker_time / QS.execution_count AS Avg_CPU_Time,
        SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
           ((CASE qs.statement_end_offset
             WHEN -1
             THEN DATALENGTH(st.text)
             ELSE qs.statement_end_offset
             END - qs.statement_start_offset) / 2) + 1) AS SqlText,
       QS.creation_time,
       QS.last_execution_time,
       QS.Execution_Count,
       CAST(QS.total_worker_time / 1000000.0 AS decimal(18, 3)) AS total_worker_time_sec,
       CAST(QS.last_worker_time / 1000000.0 AS decimal(18, 3)) AS last_worker_time_sec,
       CAST(QS.min_worker_time / 1000000.0 AS decimal(18, 3)) AS min_worker_time_sec,
       CAST(QS.max_worker_time / 1000000.0 AS decimal(18, 3)) AS max_worker_time_sec,
       CAST(QS.total_elapsed_time / 1000000.0 AS decimal(18, 3)) AS total_elapsed_time_sec,
       CAST(QS.last_elapsed_time / 1000000.0 AS decimal(18, 3)) AS last_elapsed_time_sec,
       CAST(QS.min_elapsed_time / 1000000.0 AS decimal(18, 3)) AS min_elapsed_time_sec,
       CAST(QS.max_elapsed_time / 1000000.0 AS decimal(18, 3)) AS max_elapsed_time_sec,
       QS.sql_handle,
       QS.plan_handle,
       ST.DBID,
       SD.Name AS DatabaseName,
       ST.Encrypted
	FROM sys.dm_exec_query_stats AS QS
     CROSS APPLY sys.dm_exec_sql_text ( qs.sql_handle ) AS st
     LEFT OUTER JOIN sys.databases SD
       ON ST.DBID = SD.database_ID
WHERE NOT COALESCE(SD.Name, '') IN ('master', 'msdb')

;

CREATE VIEW "mc"."v_tuning_query_execution_stats_with_plan_stats" 
AS
SELECT QES.*,
       CP.RefCounts AS Plan_RefCount,
       CP.UseCounts AS Plan_UseCount,
       CP.CacheObjType AS Plan_CacheObjType,
       CP.ObjType AS Plan_ObjType,
       QP.query_plan
	FROM V_Tuning_QueryExecutionStats QES
     INNER JOIN sys.dm_exec_cached_plans AS CP WITH ( NoLock )
       ON QES.plan_handle = CP.plan_handle
     CROSS APPLY sys.dm_exec_query_plan ( QES.plan_handle ) QP

;

CREATE VIEW "mc"."v_tuning_exec_requests" AS
WITH ExecRequests ( session_id, request_id, start_time, status, command, DBName, wait_type,
       wait_time, blocking_session_id, last_wait_type, wait_resource, open_transaction_count,
       open_resultset_count, database_id, user_id, connection_id, transaction_id, sql_handle,
       statement_start_offset, statement_end_offset, plan_handle )
AS
( SELECT DER.session_id,
         DER.request_id,
         DER.start_time,
         DER.status,
         DER.command,
         SD.Name AS DBName,
         DER.wait_type,
         DER.wait_time,
         DER.blocking_session_id,
         DER.last_wait_type,
         DER.wait_resource,
         DER.open_transaction_count,
         DER.open_resultset_count,
         DER.database_id,
         DER.user_id,
         DER.connection_id,
         DER.transaction_id,
         DER.sql_handle,
         DER.statement_start_offset,
         DER.statement_end_offset,
         DER.plan_handle
  FROM sys.dm_exec_requests DER
       INNER JOIN sys.databases SD
         ON DER.database_id = SD.database_id )
SELECT '' AS QueryText,
       ExecRequests.*
FROM ExecRequests
WHERE ExecRequests.sql_handle IS NULL
UNION
SELECT s2.text AS QueryText,
       ExecRequests.*
FROM ExecRequests
     CROSS APPLY sys.dm_exec_sql_text ( ExecRequests.sql_handle ) AS s2
WHERE NOT ExecRequests.sql_handle IS NULL

;

CREATE VIEW "mc"."v_tuning_sessions" 
AS
SELECT S.session_id,
       S.login_name,
       S.Status,
       S.[program_name] AS Application,
       S.host_name,
       C.IP_Address,
       S.login_time,
       S.last_request_start_time AS "Last Batch",
       C.Last_Read,
       C.Last_Write,
       S.client_interface_name,
       C.most_recent_sql_handle,
       S.Deadlock_Priority,
       S.Row_Count,
       QueryStats.Avg_CPU_Time,
       QueryStats.SqlText,
       QueryStats.creation_time,
       QueryStats.last_execution_time,
       QueryStats.Execution_Count,
       QueryStats.DatabaseName,
       R.command,
       R.DBName,
       R.wait_type,
       R.wait_time,
       R.blocking_session_id,
       R.last_wait_type,
       R.wait_resource,
       R.open_transaction_count,
       R.open_resultset_count,
       R.querytext
FROM sys.dm_exec_sessions S
     LEFT OUTER JOIN ( SELECT ExC.session_id,
                              ExC.most_recent_sql_handle,
                              Max(ExC.client_net_address) AS IP_Address,
                              Min(ExC.connect_time) AS Connect_Time,
                              Max(ExC.last_read) AS Last_Read,
                              Max(ExC.last_write) AS Last_Write
                       FROM sys.dm_exec_connections ExC
                       GROUP BY session_id, most_recent_sql_handle ) AS C
       ON S.Session_ID = C.Session_ID
     LEFT OUTER JOIN V_Tuning_ExecRequests R
       ON S.Session_ID = R.Session_ID
     LEFT OUTER JOIN ( SELECT QS.total_worker_time / QS.execution_count AS Avg_CPU_Time,
                              SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
                                ((CASE qs.statement_end_offset
                                      WHEN - 1 THEN DATALENGTH(st.text)
                                      ELSE qs.statement_end_offset
                                  END - qs.statement_start_offset) / 2) + 1) AS SqlText,
                              QS.creation_time,
                              QS.last_execution_time,
                              QS.Execution_Count,
                              QS.sql_handle,
                              QS.plan_handle,
                              ST.DBID,
                              SD.Name AS DatabaseName,
                              ST.Encrypted

	FROM sys.dm_exec_query_stats AS QS
                            CROSS APPLY sys.dm_exec_sql_text ( qs.sql_handle ) AS st
                                        LEFT OUTER JOIN sys.databases SD
                                          ON ST.DBID = SD.database_ID ) AS QueryStats
       ON QueryStats.Sql_Handle = C.most_recent_sql_handle
WHERE is_user_process <> 0

;

CREATE VIEW "mc"."v_manager_type_report" 
AS
SELECT MT.mt_type_name AS [Manager Type], MT.mt_type_id AS ID, COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS [Manager Count Active],
                      COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS [Manager Count Inactive]
FROM         mc.t_mgr_types AS MT LEFT OUTER JOIN
                          (SELECT     m_type_id, [Manager Type], SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                                                   SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive
                            FROM          mc.V_Manager_List_By_Type
                            GROUP BY m_type_id, [Manager Type]) AS ActiveManagersQ ON MT.mt_type_id = ActiveManagersQ.m_type_id
WHERE     (MT.mt_type_id IN
                          (SELECT     m_type_id

	FROM mc.t_mgrs
                            WHERE      (m_control_from_website > 0)))
;

CREATE VIEW "mc"."v_table_index_sizes" AS
WITH Table_Space_Usage
( Schema_Name, Table_Name, Index_Name, Space_Used_KB, Space_Reserved_KB, Index_Row_Count, Table_Row_Count, fill_factor, is_disabled )
AS (
 SELECT  s.Name,
         o.Name,
         COALESCE (i.Name, 'HEAP'),
         p.used_page_count * 8,
         p.reserved_page_count * 8,
         CASE WHEN i.index_id IN ( 0, 1 ) THEN p.row_count ELSE 0 END,
         p.row_count,
         i.fill_factor,
         i.is_disabled
 FROM sys.dm_db_partition_stats p
	INNER JOIN sys.objects o ON o.object_id = p.object_id
	INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
	LEFT OUTER JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
 WHERE o.type_desc = 'USER_TABLE' AND o.is_ms_shipped = 0
)
 SELECT TOP 100 PERCENT
        t.Schema_Name, t.Table_Name, t.Index_Name,
        SUM(t.Space_Used_KB) / 1024.0 AS Space_Used_MB,
        SUM(t.Space_Reserved_KB) / 1024.0 AS Space_Reserved_MB,
        SUM(t.Index_Row_Count) AS Index_Row_Count,
        SUM(t.Table_Row_Count) AS Table_Row_Count,
        fill_factor,
        is_disabled
 FROM Table_Space_Usage as t
 GROUP BY t.Schema_Name, t.Table_Name, t.Index_Name,fill_factor, is_disabled
 ORDER BY t.Schema_Name, t.Table_Name, t.Index_Name

;

CREATE VIEW "mc"."v_tuning_unused_indices" AS
	-- Note: Stats from sys.dm_db_index_usage_stats are as-of the last time the Database started up
	-- Thus, make sure the database has been running for a while before you consider deleting an apparently unused index
SELECT OBJECT_NAME(i.[object_id]) AS Table_Name,
       CASE i.[index_id]
           WHEN 0 THEN N'HEAP'
           ELSE i.[name]
       END AS Index_Name,
       i.index_id AS Index_ID,
       IdxSizes.Space_Reserved_MB,
       IdxSizes.Space_Used_MB,
       IdxSizes.Index_Row_Count,
       IdxSizes.Table_Row_Count
FROM sys.indexes AS i
     INNER JOIN sys.objects AS o
       ON i.[object_id] = o.[object_id]
     LEFT OUTER JOIN V_Table_Index_Sizes AS IdxSizes
       ON i.[name] = IdxSizes.Index_Name
WHERE NOT EXISTS ( SELECT *
                   FROM sys.dm_db_index_usage_stats AS u
                   WHERE u.[object_id] = i.[object_id] AND
                         u.[index_id] = i.[index_id] AND
                         [database_id] = DB_ID() ) AND
      OBJECTPROPERTY(i.[object_id], 'IsUserTable') = 1

;

CREATE VIEW "mc"."v_manager_type_report_defaults" 
AS
SELECT MT.mt_type_name AS [Manager Type], MT.mt_type_id AS ID, COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS [Manager Count Active],
                      COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS [Manager Count Inactive]
FROM         mc.t_mgr_types AS MT LEFT OUTER JOIN
                          (SELECT     M_TypeID, [Manager Type], SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                                                   SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive

	FROM mc.V_Manager_List_By_Type
                            GROUP BY M_TypeID, [Manager Type]) AS ActiveManagersQ ON MT.mt_type_id = ActiveManagersQ.M_TypeID
;

CREATE VIEW "mc"."v_table_size_summary" AS
WITH Table_Space_Summary
(Schema_Name, Table_Name, Space_Used_MB, Space_Reserved_MB, Table_Row_Count)
AS
(
 SELECT Schema_Name, Table_Name,
       	SUM(Space_Used_MB),
		SUM(Space_Reserved_MB),
		MAX(Table_Row_Count)
 FROM mc.V_Table_Index_Sizes
 GROUP BY Schema_Name, Table_Name
)
SELECT TOP 100 PERCENT S.Schema_Name, S.Table_Name,
	S.Space_Used_MB,
    ROUND(S.Space_Used_MB / CONVERT(real, TotalsQ.TotalUsedMB) * 100, 2) AS Percent_Total_Used_MB,
	S.Space_Reserved_MB,
    ROUND(S.Space_Reserved_MB / CONVERT(real, TotalsQ.TotalReservedMB) * 100, 2) AS Percent_Total_Reserved_MB,
    S.Table_Row_Count,
    ROUND(S.Table_Row_Count / CONVERT(real, TotalsQ.TotalRows) * 100, 2) AS Percent_Total_Rows
FROM Table_Space_Summary S CROSS JOIN
        (SELECT SUM(Space_Used_MB) AS TotalUsedMB,
				SUM(Space_Reserved_MB) AS TotalReservedMB,
				SUM(Table_Row_Count) AS TotalRows
		 FROM Table_Space_Summary) TotalsQ
ORDER BY S.Space_Used_MB DESC

;

CREATE VIEW "mc"."v_all_mgr_params_by_mgr_type" 
AS
SELECT DISTINCT TPT.mt_type_id as ID, CASE WHEN PM.mgr_type_id IS NOT NULL THEN 'TRUE' ELSE '' END as Selected, TPT.param_id, TPT.param_name, TPT.comment
FROM (
		SELECT DISTINCT param_id, param_name, comment, mt_type_id, mt_type_name

	FROM t_param_type, t_mgr_types
	 ) TPT
	left join t_mgr_type_param_type_map PM on TPT.param_id = PM.param_type_id and TPT.mt_type_id = PM.mgr_type_id
;

CREATE VIEW "mc"."v_analysis_job_processors_list_report" 
AS
SELECT t_mgrs.m_id AS ID, t_mgrs.m_name AS Name, t_mgr_types.mt_type_name AS Type
	FROM t_mgrs INNER JOIN
                      t_mgr_types ON t_mgrs.m_type_id = t_mgr_types.mt_type_id
;

CREATE VIEW "mc"."v_analysis_mgr_params_active_and_debug_level" 
AS
SELECT PV.mgr_id,
       M.m_name as Manager,
       PT.param_name,
       PV.type_id AS ParamTypeID,
       PV.value,
       PV.last_affected,
       pv.entered_by
	FROM mc.t_param_value AS PV
     INNER JOIN mc.t_param_type AS PT
       ON PV.type_id = PT.param_id
     INNER JOIN mc.t_mgrs AS M
       ON PV.mgr_id = M.m_id
WHERE (PT.param_name IN ('mgractive', 'debuglevel', 'ManagerErrorCleanupMode')) AND
      (M.m_type_id IN (11, 15))

;

CREATE VIEW "mc"."v_analysis_mgr_params_update_required" 
AS
SELECT PV.mgr_id,
       M.m_name as Manager,
       PT.param_name,
       PV.type_id AS ParamTypeID,
       PV.value,
       PV.last_affected,
       pv.entered_by
	FROM mc.t_param_value AS PV
     INNER JOIN mc.t_param_type AS PT
       ON PV.type_id = PT.param_id
     INNER JOIN mc.t_mgrs AS M
       ON PV.mgr_id = M.m_id
WHERE (PT.param_name IN ('ManagerUpdateRequired')) AND
      (M.m_type_id IN (11,  15))


;

CREATE VIEW "mc"."v_manager_entry" 
AS
SELECT m_id AS ManagerID, m_name AS ManagerName, m_control_from_website AS ControlFromWebsite
	FROM t_mgrs
;

CREATE VIEW "mc"."v_manager_list_by_type_picklist" 
AS
SELECT M.m_id AS ID, M.m_name AS ManagerName, MT.mt_type_name AS ManagerType
	FROM t_mgrs AS M
    JOIN t_mgr_types AS MT ON M.m_type_id = MT.mt_type_id
;

CREATE VIEW "mc"."v_manager_type_detail" 
AS
SELECT MT_TYPEID AS ID, '' AS manager_List
	FROM t_mgr_types
;

CREATE VIEW "mc"."v_manager_type_report_all" 
AS
SELECT Distinct MT.mt_type_name AS [Manager Type], MT.mt_type_id AS ID
	FROM t_mgr_types MT
     JOIN t_mgrs M on M.m_type_id = MT.mt_type_id
     JOIN t_param_value PV on PV.mgr_id = M.m_id and M.m_type_id = MT.mt_type_id
;

CREATE VIEW "mc"."v_manager_update_required" 
AS
SELECT mc.t_mgrs.m_name,
       mc.t_param_type.param_name,
       mc.t_param_value.value
	FROM mc.t_mgrs
     INNER JOIN mc.t_param_value
       ON mc.t_mgrs.m_id = mc.t_param_value.mgr_id
     INNER JOIN mc.t_param_type
       ON mc.t_param_value.type_id = mc.t_param_type.param_id
WHERE (mc.t_param_type.param_name = 'ManagerUpdateRequired')

;

CREATE VIEW "mc"."v_managers_by_broadcast_queue_topic" 
AS
SELECT mc.t_mgrs.m_name AS MgrName, mc.t_mgr_types.mt_type_name AS MgrType, TB.BroadcastQueueTopic AS BroadcastTopic,
                      TM.MessageQueueURI AS MsgQueueURI
FROM         mc.t_mgrs INNER JOIN
                          (SELECT     mgr_id, value AS BroadcastQueueTopic
                            FROM          mc.t_param_value
                            WHERE      (type_id = 117)) AS TB ON mc.t_mgrs.m_id = TB.mgr_id INNER JOIN
                      mc.t_mgr_types ON mc.t_mgrs.m_type_id = mc.t_mgr_types.mt_type_id INNER JOIN
                          (SELECT     mgr_id, CAST(value AS VARCHAR(128)) AS MessageQueueURI

	FROM mc.t_param_value AS T_ParamValue_1
                            WHERE      (type_id = 105)) AS TM ON mc.t_mgrs.m_id = TM.mgr_id
;

CREATE VIEW "mc"."v_mgr_param_defaults" 
AS
SELECT mgr_type_id, mt_type_name as ManagerType, param_type_id as [Param ID], param_name as Param, default_value as Value, COALESCE(mc.t_param_type.picklist_name, '') as picklist_name
	FROM t_mgr_type_param_type_map
     join t_param_type on param_type_id = param_id
     join t_mgr_types on mgr_type_id = mt_type_id
;

CREATE VIEW "mc"."v_mgr_params" 
AS
SELECT mc.t_param_value.mgr_id AS ManagerID,
       mc.t_mgrs.m_name AS ManagerName,
       mc.t_mgr_types.mt_type_name AS ManagerType,
       mc.t_param_type.param_name AS ParameterName,
       mc.t_param_value.value AS ParameterValue,
       mc.t_param_value.comment,
       mc.t_param_value.entry_id,
       mc.t_param_value.last_affected,
       mc.t_param_value.entered_by
	FROM mc.t_mgrs
     INNER JOIN mc.t_mgr_types
       ON mc.t_mgrs.m_type_id = mc.t_mgr_types.mt_type_id
     INNER JOIN mc.t_param_value
       ON mc.t_mgrs.m_id = mc.t_param_value.mgr_id
     INNER JOIN mc.t_param_type
       ON mc.t_param_value.type_id = mc.t_param_type.param_id

;

CREATE VIEW "mc"."v_mgr_params_by_mgr_type" 
AS
SELECT TOP 100 PERCENT mc.t_mgr_types.mt_type_name AS MgrType, mc.t_param_type.param_name AS param_name
	FROM mc.t_param_type INNER JOIN
                      mc.t_mgr_type_param_type_map ON mc.t_param_type.param_id = mc.t_mgr_type_param_type_map.param_type_id INNER JOIN
                      mc.t_mgr_types ON mc.t_mgr_type_param_type_map.mgr_type_id = mc.t_mgr_types.mt_type_id
ORDER BY mc.t_mgr_types.mt_type_name
;

CREATE VIEW "mc"."v_mgr_type_list_by_param" 
AS
SELECT DISTINCT PT.param_name, mc.GetMgrTypeListByParamName(PT.param_name) AS MgrTypeList
	FROM t_mgr_type_param_type_map MP
       JOIN t_mgr_types MT ON MP.mgr_type_id = MT.mt_type_id
       JOIN t_param_type PT ON MP.param_type_id = PT.param_id

;

CREATE VIEW "mc"."v_mgr_types_by_param" 
AS
SELECT DISTINCT PT.param_name, MT.mt_type_name
	FROM t_mgr_type_param_type_map MP
       JOIN t_mgr_types MT ON MP.mgr_type_id = MT.mt_type_id
       JOIN t_param_type PT ON MP.param_type_id = PT.param_id


;

CREATE VIEW "mc"."v_old_param_value" 
AS
SELECT M.m_name,
       PT.param_name,
       PV.entry_id,
       PV.type_id,
       PV.value,
       PV.mgr_id,
       PV.comment,
       PV.last_affected,
       PV.entered_by,
       M.m_type_id,
	   PT.param_name as ParamType
	FROM t_param_value_old_managers PV
     INNER JOIN t_old_managers M
       ON PV.mgr_id = M.m_id
     INNER JOIN t_param_type PT
       ON PV.type_id = PT.param_id


;

CREATE VIEW "mc"."v_param_name_picklist" 
AS
SELECT param_name AS val, param_name AS ex, mgr_type_id AS M_TypeID
	FROM t_param_type
		JOIN t_mgr_type_param_type_map ON param_id = param_type_id
;

CREATE VIEW "mc"."v_param_id_entry" 
AS
SELECT param_id, param_name, picklist_name, comment
	FROM t_param_type
;

CREATE VIEW "mc"."v_table_row_counts" 
AS
SELECT TOP 100 PERCENT o.name AS TableName,
    i.rowcnt AS TableRowCount
	FROM mc.sysobjects o INNER JOIN
    mc.sysindexes i ON o.id = i.id
WHERE (o.type = 'u') AND (i.indid < 2) AND
    (o.name <> 'dtproperties')
ORDER BY o.name

;

CREATE VIEW "mc"."v_table_sizes" 
AS
SELECT TOP 100 PERCENT su.tablename AS Table_Name,
    ROUND((CAST(su.tablesize AS float) * spt.low)
    / (1024 * 1024), 3) AS Table_Size_MB
FROM master.mc.spt_values spt CROSS JOIN
        (SELECT so.name tablename, SUM(si.reserved)
           tablesize

	FROM sysobjects so JOIN
           sysindexes si ON so.id = si.id
      WHERE si.indid IN (0, 1, 255) AND so.xtype = 'U'
      GROUP BY so.name) su
WHERE (spt.number = 1) AND (spt.type = 'E')
ORDER BY su.tablesize DESC, su.tablename


;

CREATE VIEW "mc"."v_tuning_index_usage" AS







	-- Note: Stats from sys.dm_db_index_usage_stats are as-of the last time the Database started up
	-- Thus, make sure the database has been running for a while before you consider deleting an apparently unused index
SELECT O.Name AS Table_Name,
       I.Name AS Index_Name,
       S.Index_ID,
       S.User_Seeks,       S.User_Scans,       S.User_Lookups,       S.User_Updates,
       S.Last_User_Seek,   S.Last_User_Scan,   S.Last_User_Lookup,   S.Last_User_Update,
       S.System_Seeks,     S.System_Scans,     S.System_Lookups,     S.System_Updates,
       S.Last_System_Seek, S.Last_System_Scan, S.Last_System_Lookup, S.Last_System_Update
FROM sys.dm_db_index_usage_stats S
     INNER JOIN sys.objects O
       ON S.Object_ID = O.Object_ID
     INNER JOIN sys.indexes I
       ON O.Object_ID = I.Object_ID AND
          S.Index_ID = I.Index_ID
WHERE S.[database_id] = DB_ID()

;

CREATE VIEW "mc"."v_tuning_missing_indices" 
AS
SELECT sys.objects.name,
       (avg_total_user_cost * avg_user_impact) * (user_seeks + user_scans) AS Impact,'CREATE NONCLUSTERED INDEX IX_' + sys.objects.name + '_IndexName ON ' + sys.objects.name + ' ( '||COALESCE(IndexDetails.equality_columns, '') +
          CASE
          WHEN IndexDetails.inequality_columns IS NULL THEN ''
          ELSE CASE
               WHEN IndexDetails.equality_columns IS NULL THEN ''
               ELSE ','
               END + IndexDetails.inequality_columns
          END + ' ) ' +
          CASE
          WHEN IndexDetails.included_columns IS NULL THEN ''
          ELSE 'INCLUDE (' + IndexDetails.included_columns + ')'
          END + ';' AS CreateIndexStatement,
       IndexDetails.equality_columns,
       IndexDetails.inequality_columns,
       IndexDetails.included_columns
FROM sys.dm_db_missing_index_group_stats AS IndexGrpStats
     INNER JOIN sys.dm_db_missing_index_groups AS IndexGroups
       ON IndexGrpStats.group_handle = IndexGroups.index_group_handle
     INNER JOIN sys.dm_db_missing_index_details AS IndexDetails
       ON IndexGroups.index_handle = IndexDetails.index_handle
     INNER JOIN sys.objects WITH ( nolock )
       ON IndexDetails.OBJECT_ID = sys.objects.OBJECT_ID
WHERE (IndexGrpStats.group_handle IN (
		SELECT TOP ( 500 ) group_handle

	FROM sys.dm_db_missing_index_group_stats WITH ( nolock )
		ORDER BY (avg_total_user_cost * avg_user_impact)
			   * (user_seeks + user_scans) DESC )
       ) AND
      OBJECTPROPERTY(sys.objects.OBJECT_ID, 'isusertable') = 1
--ORDER BY 2 DESC, 3 DESC

;

CREATE VIEW "mc"."x_v_mgr_state" 
AS
SELECT MS.MgrID, M.m_name AS [Manager Name], MT.mt_type_name AS [Manager Type],
     MS.TypeID AS [Param Type], PT.param_name AS [Param Name], MS.Value AS State
	FROM T_MgrState MS
    JOIN t_mgrs M ON M.m_id = MS.MgrID
    JOIN t_mgr_types MT ON MT.mt_type_id = M.m_type_id
	JOIN t_param_type PT ON PT.param_id = MS.TypeID
;

COMMIT;
