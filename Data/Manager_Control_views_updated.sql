SET search_path TO public, sw, cap, dpkg, mc, ont;
SHOW search_path;

-- PostgreSQL stores views as Parse Trees, meaning any whitespace that is present in the CREATE VIEW statements will be lost
--
-- The PgSQL View Creator Helper will convert any comments on views to COMMENT ON VIEW statements

\set ON_ERROR_STOP
BEGIN;
ALTER TABLE "mc"."t_event_log" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_event_log" ALTER COLUMN "entered_by" SET DEFAULT session_user;
ALTER TABLE "mc"."t_log_entries" ALTER COLUMN "entered_by" SET DEFAULT session_user;
ALTER TABLE "mc"."t_log_entries" ALTER COLUMN "posting_time" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_mgr_type_param_type_map" ALTER COLUMN "entered_by" SET DEFAULT session_user;
ALTER TABLE "mc"."t_mgr_type_param_type_map" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "mc"."t_param_value" ALTER COLUMN "entered_by" SET DEFAULT session_user;
ALTER TABLE "mc"."t_param_value" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;

CREATE OR REPLACE VIEW "mc"."v_param_value"
AS
SELECT M.mgr_name as mgr_name,
       PT.param_name As Param_Name,
       PV.entry_id,
       PV.type_id As Type_ID,
       PV.value,
       PV.mgr_id As mgr_id,
       PV.comment,
       PV.last_affected,
       PV.entered_by,
       M.mgr_type_id As mgr_type_id
    FROM mc.t_param_value PV
     INNER JOIN mc.t_mgrs M
       ON PV.mgr_id = M.mgr_id
     INNER JOIN mc.t_param_type PT
       ON PV.type_id = PT.param_id

;

CREATE OR REPLACE VIEW "mc"."v_mgr_work_dir"
AS
SELECT Mgr_Name,
       CASE
           WHEN Value LIKE '\\%' THEN Value
           ELSE '\\ServerName\' || Replace(Value, ':\', '$\')
       END AS WorkDir_AdminShare
FROM V_Param_Value
WHERE (Param_Name = 'workdir')
;

COMMENT ON VIEW "mc"."v_mgr_work_dir" IS 'This database does not keep track of the server name that a given manager is running on. Thus, this query includes the generic text ServerName for the WorkDir path, unless the WorkDir is itself a network share';

CREATE OR REPLACE VIEW "mc"."v_manager_list_by_type"
AS
SELECT M.mgr_id AS ID,
       M.mgr_name AS Manager_Name,
       MT.mgr_type_name AS Manager_Type,
       COALESCE(ActiveQ.Active, 'not defined') AS Active,
       M.mgr_type_id As mgr_type_id,
       ActiveQ.last_affected AS State_Last_Changed,
       ActiveQ.entered_by AS Changed_By,
       M.comment AS comment
FROM mc.t_mgrs AS M
     INNER JOIN mc.t_mgr_types AS MT
       ON M.mgr_type_id = MT.mgr_type_id
     LEFT OUTER JOIN ( SELECT PV.mgr_id,
                              PV.value AS Active,
                              PV.last_affected,
                              PV.entered_by

    FROM mc.t_param_value AS PV
                            INNER JOIN mc.t_param_type AS PT
                              ON PV.type_id = PT.param_id
                       WHERE (PT.param_name = 'mgractive') ) AS ActiveQ
       ON M.mgr_id = ActiveQ.mgr_id
WHERE (M.control_from_website > 0)

;

CREATE OR REPLACE VIEW "mc"."v_manager_type_report_ex"
AS
SELECT MT.mgr_type_name AS Manager_Type,
       MT.mgr_type_id AS ID,
       COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS Manager_Count_Active,
       COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS Manager_Count_Inactive
FROM mc.t_mgr_types AS MT
     LEFT OUTER JOIN ( SELECT mgr_type_id,
                              Manager_Type,
                              SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                              SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive

    FROM mc.V_Manager_List_By_Type
                       GROUP BY mgr_type_id, Manager_Type ) AS ActiveManagersQ
       ON MT.mgr_type_id = ActiveManagersQ.mgr_type_id

;

CREATE OR REPLACE VIEW "mc"."v_manager_type_report"
AS
SELECT MT.mgr_type_name AS Manager_Type,
       MT.mgr_type_id AS ID,
       COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS Manager_Count_Active,
       COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS Manager_Count_Inactive
FROM mc.t_mgr_types AS MT
     LEFT OUTER JOIN ( SELECT mgr_type_id,
                              Manager_Type,
                              SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                              SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive
                       FROM mc.V_Manager_List_By_Type
                       GROUP BY mgr_type_id, Manager_Type ) AS ActiveManagersQ
       ON MT.mgr_type_id = ActiveManagersQ.mgr_type_id
WHERE (MT.mgr_type_id IN ( SELECT mgr_type_id

    FROM mc.t_mgrs
                         WHERE (control_from_website > 0) ))
;

CREATE OR REPLACE VIEW "mc"."v_manager_type_report_defaults"
AS
SELECT MT.mgr_type_name AS Manager_Type,
       MT.mgr_type_id AS ID,
       COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS Manager_Count_Active,
       COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS Manager_Count_Inactive
FROM mc.t_mgr_types AS MT
     LEFT OUTER JOIN ( SELECT mgr_type_id,
                              Manager_Type,
                              SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                              SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive

    FROM mc.V_Manager_List_By_Type
                       GROUP BY mgr_type_id, Manager_Type ) AS ActiveManagersQ
       ON MT.mgr_type_id = ActiveManagersQ.mgr_type_id

;

CREATE OR REPLACE VIEW "mc"."v_active_connections"
AS
SELECT Rtrim(Cast(hostname AS nvarchar(128))) AS Host,
       Rtrim(Cast(program_name AS nvarchar(128))) AS Application,
       Rtrim(Cast(loginame AS nvarchar(128))) AS LoginName,
       DB_NAME(dbid) AS DBName,
       spid,
       login_time,
       last_batch,
       Rtrim(Cast(cmd AS nvarchar(32))) AS Cmd,
       Rtrim(Cast(Status AS nvarchar(32))) Status
    FROM sys.sysprocesses
WHERE dbid > 0 AND
      COALESCE(hostname, '') <> ''

;

CREATE OR REPLACE VIEW "mc"."v_all_mgr_params_by_mgr_type"
AS
SELECT DISTINCT TPT.mgr_type_id AS ID,
                CASE
                    WHEN PM.mgr_type_id IS NOT NULL THEN 'TRUE'
                    ELSE ''
                END AS Selected,
                TPT.param_id,
                TPT.param_name,
                TPT.comment
FROM ( SELECT DISTINCT param_id,
                       param_name,
                       comment,
                       mgr_type_id,
                       mgr_type_name

    FROM mc.t_param_type,
            mc.t_mgr_types ) TPT
     LEFT JOIN mc.t_mgr_type_param_type_map PM
       ON TPT.param_id = PM.param_type_id AND
          TPT.mgr_type_id = PM.mgr_type_id
;

CREATE OR REPLACE VIEW "mc"."v_analysis_job_processors_list_report"
AS
SELECT M.mgr_id AS ID,
       M.mgr_name AS Name,
       MT.mgr_type_name AS Type
    FROM mc.t_mgrs M
     INNER JOIN mc.t_mgr_types MT
       ON M.mgr_type_id = MT.mgr_type_id
;

CREATE OR REPLACE VIEW "mc"."v_analysis_mgr_params_active_and_debug_level"
AS
SELECT PV.mgr_id,
       M.mgr_name as Manager,
       PT.param_name,
       PV.type_id AS ParamTypeID,
       PV.value,
       PV.last_affected,
       PV.entered_by
    FROM mc.t_param_value AS PV
     INNER JOIN mc.t_param_type AS PT
       ON PV.type_id = PT.param_id
     INNER JOIN mc.t_mgrs AS M
       ON PV.mgr_id = M.mgr_id
WHERE PT.param_name IN ('mgractive', 'debuglevel', 'ManagerErrorCleanupMode') AND
      M.mgr_type_id IN (11, 15)

;

CREATE OR REPLACE VIEW "mc"."v_analysis_mgr_params_update_required"
AS
SELECT PV.mgr_id,
       M.mgr_name as Manager,
       PT.param_name,
       PV.type_id AS ParamTypeID,
       PV.value,
       PV.last_affected,
       PV.entered_by
    FROM mc.t_param_value AS PV
     INNER JOIN mc.t_param_type AS PT
       ON PV.type_id = PT.param_id
     INNER JOIN mc.t_mgrs AS M
       ON PV.mgr_id = M.mgr_id
WHERE PT.param_name IN ('ManagerUpdateRequired') AND
      M.mgr_type_id IN (11, 15)

;

CREATE OR REPLACE VIEW "mc"."v_manager_entry"
AS
SELECT mgr_id AS ManagerID,
       mgr_name AS ManagerName,
       control_from_website AS ControlFromWebsite
    FROM mc.t_mgrs
;

CREATE OR REPLACE VIEW "mc"."v_manager_list_by_type_picklist"
AS
SELECT M.mgr_id AS ID,
       M.mgr_name AS ManagerName,
       MT.mgr_type_name AS ManagerType
    FROM mc.t_mgrs AS M
     JOIN mc.t_mgr_types AS MT
       ON M.mgr_type_id = MT.mgr_type_id
;

CREATE OR REPLACE VIEW "mc"."v_manager_type_detail"
AS
SELECT mgr_type_id AS ID, '' AS manager_List
    FROM mc.t_mgr_types
;

CREATE OR REPLACE VIEW "mc"."v_manager_type_report_all"
AS
SELECT DISTINCT MT.mgr_type_name AS Manager_Type,
                MT.mgr_type_id AS ID
    FROM mc.t_mgr_types MT
     JOIN mc.t_mgrs M
       ON M.mgr_type_id = MT.mgr_type_id
     JOIN mc.t_param_value PV
       ON PV.mgr_id = M.mgr_id AND
          M.mgr_type_id = MT.mgr_type_id
;

CREATE OR REPLACE VIEW "mc"."v_manager_update_required"
AS
SELECT M.mgr_name,
       PT.param_name,
       PV.value
    FROM mc.t_mgrs As M
     INNER JOIN mc.t_param_value PV
       ON M.mgr_id = PV.mgr_id
     INNER JOIN mc.t_param_type PT
       ON PV.type_id = PT.param_id
WHERE PT.param_name = 'ManagerUpdateRequired'

;

CREATE OR REPLACE VIEW "mc"."v_managers_by_broadcast_queue_topic"
AS
SELECT M.mgr_name AS MgrName,
       MT.mgr_type_name AS MgrType,
       TB.BroadcastQueueTopic AS BroadcastTopic,
       TM.MessageQueueURI AS MsgQueueURI
FROM mc.t_mgrs M
     INNER JOIN ( SELECT mgr_id,
                         value AS BroadcastQueueTopic
                  FROM mc.t_param_value PV
                  WHERE type_id = 117 ) AS TB
       ON M.mgr_id = TB.mgr_id
     INNER JOIN mc.t_mgr_types MT
       ON M.mgr_type_id = MT.mgr_type_id
     INNER JOIN ( SELECT mgr_id,
                         CAST(value AS varchar(128)) AS MessageQueueURI

    FROM mc.t_param_value AS PV
                  WHERE type_id = 105 ) AS TM
       ON M.mgr_id = TM.mgr_id
;

CREATE OR REPLACE VIEW "mc"."v_mgr_params"
AS
SELECT PV.mgr_id AS ManagerID,
       M.mgr_name AS ManagerName,
       MT.mgr_type_name AS ManagerType,
       PT.param_name AS ParameterName,
       PV.value AS ParameterValue,
       PV.comment,
       PV.entry_id,
       PV.last_affected,
       PV.entered_by
    FROM mc.t_mgrs M
     INNER JOIN mc.t_mgr_types MT
       ON M.mgr_type_id = MT.mgr_type_id
     INNER JOIN mc.t_param_value PV
       ON M.mgr_id = PV.mgr_id
     INNER JOIN mc.t_param_type PT
       ON PV.type_id = PT.param_id

;

CREATE OR REPLACE VIEW "mc"."v_mgr_param_defaults"
AS
SELECT MTPM.mgr_type_id,
       MT.mgr_type_name AS ManagerType,
       MTPM.param_type_id AS "Param ID",
       PT.param_name AS Param,
       MTPM.default_value AS Value,
       COALESCE(PT.picklist_name, '') AS picklist_name
    FROM mc.t_mgr_type_param_type_map MTPM
     INNER JOIN mc.t_param_type PT
       ON MTPM.param_type_id = PT.param_id
     INNER JOIN mc.t_mgr_types MT
       ON MTPM.mgr_type_id = MT.mgr_type_id
;

CREATE OR REPLACE VIEW "mc"."v_mgr_params_by_mgr_type"
AS
SELECT MT.mgr_type_name AS MgrType,
       PT.param_name AS param_name
    FROM mc.t_param_type PT
     INNER JOIN mc.t_mgr_type_param_type_map MTPM
       ON PT.param_id = MTPM.param_type_id
     INNER JOIN mc.t_mgr_types MT
       ON MTPM.mgr_type_id = MT.mgr_type_id
;

CREATE OR REPLACE VIEW "mc"."v_mgr_types_by_param"
AS
SELECT DISTINCT PT.param_name,
                MT.mgr_type_name
    FROM mc.t_mgr_type_param_type_map MP
     INNER JOIN mc.t_mgr_types MT
       ON MP.mgr_type_id = MT.mgr_type_id
     INNER JOIN mc.t_param_type PT
       ON MP.param_type_id = PT.param_id

;

CREATE OR REPLACE VIEW "mc"."v_old_param_value"
AS
SELECT M.mgr_name,
       PT.param_name,
       PV.entry_id,
       PV.type_id,
       PV.value,
       PV.mgr_id,
       PV.comment,
       PV.last_affected,
       PV.entered_by,
       M.mgr_type_id,
       PT.param_name as ParamType
    FROM mc.t_param_value_old_managers PV
     INNER JOIN mc.t_old_managers M
       ON PV.mgr_id = M.mgr_id
     INNER JOIN mc.t_param_type PT
       ON PV.type_id = PT.param_id


;

CREATE OR REPLACE VIEW "mc"."v_param_name_picklist"
AS
SELECT param_name AS val,
       param_name AS ex,
       mgr_type_id AS M_TypeID
    FROM mc.t_param_type
     Inner JOIN mc.t_mgr_type_param_type_map
       ON param_id = param_type_id
;

CREATE OR REPLACE VIEW "mc"."v_param_id_entry"
AS
SELECT param_id,
       param_name,
       picklist_name,
       comment
    FROM mc.t_param_type

;

CREATE OR REPLACE VIEW "mc"."v_param_value"
AS
SELECT M.mgr_name,
       PT.param_name,
       PV.entry_id,
       PV.type_id,
       PV.value,
       PV.mgr_id,
       PV.comment,
       PV.last_affected,
       PV.entered_by,
       M.mgr_type_id
    FROM mc.t_param_value PV
     INNER JOIN mc.t_mgrs M
       ON PV.mgr_id = M.mgr_id
     INNER JOIN mc.t_param_type PT
       ON PV.type_id = PT.param_id

;

COMMIT;

SELECT * FROM "mc"."v_param_value";
SELECT * FROM "mc"."v_mgr_work_dir";
SELECT * FROM "mc"."v_manager_list_by_type";
SELECT * FROM "mc"."v_manager_type_report_ex";
SELECT * FROM "mc"."v_manager_type_report";
SELECT * FROM "mc"."v_manager_type_report_defaults";
SELECT * FROM "mc"."v_active_connections";
SELECT * FROM "mc"."v_all_mgr_params_by_mgr_type";
SELECT * FROM "mc"."v_analysis_job_processors_list_report";
SELECT * FROM "mc"."v_analysis_mgr_params_active_and_debug_level";
SELECT * FROM "mc"."v_analysis_mgr_params_update_required";
SELECT * FROM "mc"."v_manager_entry";
SELECT * FROM "mc"."v_manager_list_by_type_picklist";
SELECT * FROM "mc"."v_manager_type_detail";
SELECT * FROM "mc"."v_manager_type_report_all";
SELECT * FROM "mc"."v_manager_update_required";
SELECT * FROM "mc"."v_managers_by_broadcast_queue_topic";
SELECT * FROM "mc"."v_mgr_params";
SELECT * FROM "mc"."v_mgr_param_defaults";
SELECT * FROM "mc"."v_mgr_params_by_mgr_type";
SELECT * FROM "mc"."v_mgr_types_by_param";
SELECT * FROM "mc"."v_old_param_value";
SELECT * FROM "mc"."v_param_name_picklist";
SELECT * FROM "mc"."v_param_id_entry";
SELECT * FROM "mc"."v_param_value";
