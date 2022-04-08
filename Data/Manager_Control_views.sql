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
CREATE VIEW "mc"."v_param_value" 
AS
SELECT M.M_Name as Mgr_Name,
       PT.ParamName As Param_Name,
       PV.Entry_ID,
       PV.TypeID As Type_ID,
       PV.Value,
       PV.MgrID As Mgr_ID,
       PV.Comment,
       PV.Last_Affected,
       PV.Entered_By,
       M.M_TypeID As Mgr_Type_ID
	FROM T_ParamValue PV
     INNER JOIN T_Mgrs M
       ON PV.MgrID = M.M_ID
     INNER JOIN T_ParamType PT
       ON PV.TypeID = PT.ParamID

;

CREATE VIEW "mc"."v_mgr_work_dir" AS
-- This database does not keep track of the server name that a given manager is running on
-- Thus, this query includes the generic text ServerName for the WorkDir path, unless the WorkDir is itself a network share
SELECT Mgr_Name,
       CASE
           WHEN Value LIKE '\\%' THEN Value
           ELSE '\\ServerName\' + Replace(Value, ':\', '$\')
       END AS WorkDir_AdminShare
FROM V_Param_Value
WHERE (Param_Name = 'workdir')
;

CREATE VIEW "mc"."v_manager_list_by_type" 
AS
SELECT M.M_ID AS ID,
       M.M_Name AS Manager_Name,
       MT.MT_TypeName AS Manager_Type,
       COALESCE(ActiveQ.Active, 'not defined') AS Active,
       M.M_TypeID As Mgr_Type_ID,
       ActiveQ.Last_Affected AS State_Last_Changed,
       ActiveQ.Entered_By AS Changed_By,
       M.M_Comment AS Comment
FROM mc.T_Mgrs AS M
     INNER JOIN mc.T_MgrTypes AS MT
       ON M.M_TypeID = MT.MT_TypeID
     LEFT OUTER JOIN ( SELECT PV.MgrID,
                              PV.VALUE AS Active,
                              PV.Last_Affected,
                              PV.Entered_By
                      
	FROM mc.T_ParamValue AS PV
                            INNER JOIN mc.T_ParamType AS PT
                              ON PV.TypeID = PT.ParamID
                       WHERE (PT.ParamName = 'mgractive') ) AS ActiveQ
       ON M.M_ID = ActiveQ.MgrID
WHERE (M.M_ControlFromWebsite > 0)

;

CREATE VIEW "mc"."v_manager_type_report_ex" 
AS
SELECT MT.MT_TypeName AS Manager_Type,
       MT.MT_TypeID AS ID,
       COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS Manager_Count_Active,
       COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS Manager_Count_Inactive
FROM mc.T_MgrTypes AS MT
     LEFT OUTER JOIN ( SELECT Mgr_Type_ID,
                              Manager_Type,
                              SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                              SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive
                      
	FROM mc.V_Manager_List_By_Type
                       GROUP BY Mgr_Type_ID, Manager_Type ) AS ActiveManagersQ
       ON MT.MT_TypeID = ActiveManagersQ.Mgr_Type_ID

;

CREATE VIEW "mc"."v_manager_type_report" 
AS
SELECT MT.MT_TypeName AS Manager_Type,
       MT.MT_TypeID AS ID,
       COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS Manager_Count_Active,
       COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS Manager_Count_Inactive
FROM mc.T_MgrTypes AS MT
     LEFT OUTER JOIN ( SELECT Mgr_Type_ID,
                              Manager_Type,
                              SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                              SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive
                       FROM mc.V_Manager_List_By_Type
                       GROUP BY Mgr_Type_ID, Manager_Type ) AS ActiveManagersQ
       ON MT.MT_TypeID = ActiveManagersQ.Mgr_Type_ID
WHERE (MT.MT_TypeID IN ( SELECT M_TypeID
                        
	FROM mc.T_Mgrs
                         WHERE (M_ControlFromWebsite > 0) ))
;

CREATE VIEW "mc"."v_manager_type_report_defaults" 
AS
SELECT MT.MT_TypeName AS Manager_Type,
       MT.MT_TypeID AS ID,
       COALESCE(ActiveManagersQ.ManagerCountActive, 0) AS Manager_Count_Active,
       COALESCE(ActiveManagersQ.ManagerCountInactive, 0) AS Manager_Count_Inactive
FROM mc.T_MgrTypes AS MT
     LEFT OUTER JOIN ( SELECT Mgr_Type_ID,
                              Manager_Type,
                              SUM(CASE WHEN active = 'True' THEN 1 ELSE 0 END) AS ManagerCountActive,
                              SUM(CASE WHEN active <> 'True' THEN 1 ELSE 0 END) AS ManagerCountInactive
                      
	FROM mc.V_Manager_List_By_Type
                       GROUP BY Mgr_Type_ID, Manager_Type ) AS ActiveManagersQ
       ON MT.MT_TypeID = ActiveManagersQ.Mgr_Type_ID

;

CREATE VIEW "mc"."v_active_connections" 
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

CREATE VIEW "mc"."v_all_mgr_params_by_mgr_type" 
AS
SELECT DISTINCT TPT.MT_TypeID AS ID,
                CASE
                    WHEN PM.MgrTypeID IS NOT NULL THEN 'TRUE'
                    ELSE ''
                END AS Selected,
                TPT.ParamID,
                TPT.ParamName,
                TPT.Comment
FROM ( SELECT DISTINCT ParamID,
                       ParamName,
                       Comment,
                       MT_TypeID,
                       MT_TypeName
      
	FROM T_ParamType,
            T_MgrTypes ) TPT
     LEFT JOIN T_MgrType_ParamType_Map PM
       ON TPT.ParamID = PM.ParamTypeID AND
          TPT.MT_TypeID = PM.MgrTypeID
;

CREATE VIEW "mc"."v_analysis_job_processors_list_report" 
AS
SELECT M.M_ID AS ID,
       M.M_Name AS Name,
       MT.MT_TypeName AS Type
	FROM T_Mgrs M
     INNER JOIN T_MgrTypes MT
       ON M.M_TypeID = MT.MT_TypeID
;

CREATE VIEW "mc"."v_analysis_mgr_params_active_and_debug_level" 
AS
SELECT PV.MgrID,
       M.M_Name as Manager,
       PT.ParamName,
       PV.TypeID AS ParamTypeID,
       PV.Value,
       PV.Last_Affected,
       PV.Entered_By
	FROM mc.T_ParamValue AS PV
     INNER JOIN mc.T_ParamType AS PT
       ON PV.TypeID = PT.ParamID
     INNER JOIN mc.T_Mgrs AS M
       ON PV.MgrID = M.M_ID
WHERE PT.ParamName IN ('mgractive', 'debuglevel', 'ManagerErrorCleanupMode') AND
      M.M_TypeID IN (11, 15)

;

CREATE VIEW "mc"."v_analysis_mgr_params_update_required" 
AS
SELECT PV.MgrID,
       M.M_Name as Manager,
       PT.ParamName,
       PV.TypeID AS ParamTypeID,
       PV.Value,
       PV.Last_Affected,
       PV.Entered_By
	FROM mc.T_ParamValue AS PV
     INNER JOIN mc.T_ParamType AS PT
       ON PV.TypeID = PT.ParamID
     INNER JOIN mc.T_Mgrs AS M
       ON PV.MgrID = M.M_ID
WHERE PT.ParamName IN ('ManagerUpdateRequired') AND
      M.M_TypeID IN (11, 15)

;

CREATE VIEW "mc"."v_manager_entry" 
AS
SELECT M_ID AS ManagerID,
       M_Name AS ManagerName,
       M_ControlFromWebsite AS ControlFromWebsite
	FROM T_Mgrs
;

CREATE VIEW "mc"."v_manager_list_by_type_picklist" 
AS
SELECT M.M_ID AS ID,
       M.M_Name AS ManagerName,
       MT.MT_TypeName AS ManagerType
	FROM T_Mgrs AS M
     JOIN T_MgrTypes AS MT
       ON M.M_TypeID = MT.MT_TypeID
;

CREATE VIEW "mc"."v_manager_type_detail" 
AS
SELECT MT_TYPEID AS ID, '' AS manager_List
	FROM T_MgrTypes
;

CREATE VIEW "mc"."v_manager_type_report_all" 
AS
SELECT DISTINCT MT.MT_TypeName AS Manager_Type,
                MT.MT_TypeID AS ID
	FROM T_MgrTypes MT
     JOIN T_Mgrs M
       ON M.M_TypeID = MT.MT_TypeID
     JOIN T_ParamValue PV
       ON PV.MgrID = M.M_ID AND
          M.M_TypeID = MT.MT_TypeID
;

CREATE VIEW "mc"."v_manager_update_required" 
AS
SELECT M.M_Name,
       PT.ParamName,
       PV.Value
	FROM T_Mgrs As M
     INNER JOIN T_ParamValue PV
       ON M.M_ID = PV.MgrID
     INNER JOIN T_ParamType PT
       ON PV.TypeID = PT.ParamID
WHERE PT.ParamName = 'ManagerUpdateRequired'

;

CREATE VIEW "mc"."v_managers_by_broadcast_queue_topic" 
AS
SELECT M.M_Name AS MgrName,
       MT.MT_TypeName AS MgrType,
       TB.BroadcastQueueTopic AS BroadcastTopic,
       TM.MessageQueueURI AS MsgQueueURI
FROM T_Mgrs M
     INNER JOIN ( SELECT MgrID,
                         VALUE AS BroadcastQueueTopic
                  FROM T_ParamValue PV
                  WHERE TypeID = 117 ) AS TB
       ON M.M_ID = TB.MgrID
     INNER JOIN T_MgrTypes MT
       ON M.M_TypeID = MT.MT_TypeID
     INNER JOIN ( SELECT MgrID,
                         CAST(VALUE AS varchar(128)) AS MessageQueueURI
                 
	FROM T_ParamValue AS PV
                  WHERE TypeID = 105 ) AS TM
       ON M.M_ID = TM.MgrID
;

CREATE VIEW "mc"."v_mgr_params" 
AS
SELECT PV.MgrID AS ManagerID,
       M.M_Name AS ManagerName,
       MT.MT_TypeName AS ManagerType,
       PT.ParamName AS ParameterName,
       PV.Value AS ParameterValue,
       PV.Comment,
       PV.Entry_ID,
       PV.Last_Affected,
       PV.Entered_By
	FROM T_Mgrs M
     INNER JOIN T_MgrTypes MT
       ON M.M_TypeID = MT.MT_TypeID
     INNER JOIN T_ParamValue PV
       ON M.M_ID = PV.MgrID
     INNER JOIN T_ParamType PT
       ON PV.TypeID = PT.ParamID

;

CREATE VIEW "mc"."v_mgr_param_defaults" 
AS
SELECT MTPM.MgrTypeID,
       MT.MT_TypeName AS ManagerType,
       MTPM.ParamTypeID AS [Param ID],
       PT.ParamName AS Param,
       MTPM.DefaultValue AS Value,
       COALESCE(PT.PicklistName, '') AS PicklistName
	FROM T_MgrType_ParamType_Map MTPM
     INNER JOIN T_ParamType PT
       ON MTPM.ParamTypeID = PT.ParamID
     INNER JOIN T_MgrTypes MT
       ON MTPM.MgrTypeID = MT.MT_TypeID
;

CREATE VIEW "mc"."v_mgr_params_by_mgr_type" 
AS
SELECT MT.MT_TypeName AS MgrType,
       PT.ParamName AS ParamName
	FROM T_ParamType PT
     INNER JOIN T_MgrType_ParamType_Map MTPM
       ON PT.ParamID = MTPM.ParamTypeID
     INNER JOIN T_MgrTypes MT
       ON MTPM.MgrTypeID = MT.MT_TypeID
;

CREATE VIEW "mc"."v_mgr_types_by_param" 
AS
SELECT DISTINCT PT.ParamName,
                MT.MT_TypeName
	FROM T_MgrType_ParamType_Map MP
     INNER JOIN T_MgrTypes MT
       ON MP.MgrTypeID = MT.MT_TypeID
     INNER JOIN T_ParamType PT
       ON MP.ParamTypeID = PT.ParamID
  
;

CREATE VIEW "mc"."v_old_param_value" 
AS
SELECT M.M_Name,
       PT.ParamName,
       PV.Entry_ID,
       PV.TypeID,
       PV.Value,
       PV.MgrID,
       PV.Comment,
       PV.Last_Affected,
       PV.Entered_By,
       M.M_TypeID,
	   PT.ParamName as ParamType
	FROM T_ParamValue_OldManagers PV
     INNER JOIN T_OldManagers M
       ON PV.MgrID = M.M_ID
     INNER JOIN T_ParamType PT
       ON PV.TypeID = PT.ParamID


;

CREATE VIEW "mc"."v_param_name_picklist" 
AS
SELECT ParamName AS val,
       ParamName AS ex,
       MgrTypeID AS M_TypeID
	FROM T_ParamType
     Inner JOIN T_MgrType_ParamType_Map
       ON ParamID = ParamTypeID
;

CREATE VIEW "mc"."v_param_id_entry" 
AS
SELECT ParamID,
       ParamName,
       PicklistName,
       Comment
	FROM T_ParamType

;

CREATE VIEW "mc"."v_param_value" 
AS
SELECT M.M_Name,
       PT.ParamName,
       PV.Entry_ID,
       PV.TypeID,
       PV.Value,
       PV.MgrID,
       PV.Comment,
       PV.Last_Affected,
       PV.Entered_By,
       M.M_TypeID
	FROM T_ParamValue PV
     INNER JOIN T_Mgrs M
       ON PV.MgrID = M.M_ID
     INNER JOIN T_ParamType PT
       ON PV.TypeID = PT.ParamID

;

COMMIT;
