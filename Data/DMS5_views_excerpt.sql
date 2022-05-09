-- The following set commands are specific to psql
\set ON_ERROR_STOP
\set ECHO all
BEGIN;
ALTER TABLE "public"."t_analysis_job" ADD CONSTRAINT "ck_t_analysis_job_propagation_mode" CHECK (((aj_propagation_mode=(1) OR aj_propagation_mode=(0))));
ALTER TABLE "public"."t_analysis_job_processor_group_membership" ADD CONSTRAINT "ck_t_analysis_job_processor_group_membership_enabled" CHECK ((membership_enabled] = 'n' or [membership_enabled = 'Y'));
ALTER TABLE "public"."t_analysis_job_processors" ADD CONSTRAINT "ck_t_analysis_job_processors_state" CHECK (((state='D' OR state='E')));
ALTER TABLE "public"."t_analysis_job" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_analysis_job_batches" ALTER COLUMN "batch_created" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_analysis_job_id" ALTER COLUMN "created" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_analysis_job_psm_stats" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_analysis_job_psm_stats_phospho" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_dataset" ALTER COLUMN "date_sort_key" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_dataset" ALTER COLUMN "last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_dataset_qc_instruments" ALTER COLUMN "last_updated" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_dataset_storage_move_log" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_dataset_archive" ALTER COLUMN "archive_state_last_affected" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_experiments" ALTER COLUMN "last_used" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_organisms" ALTER COLUMN "created" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_organisms_change_history" ALTER COLUMN "entered" SET DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE "public"."t_organisms_change_history" ALTER COLUMN "entered_by" SET DEFAULT session_user;
CREATE VIEW "public"."v_dataset_disposition"
AS
SELECT DS.Dataset_ID AS ID,
       '' AS [Sel.],
       DS.Dataset_Num AS Dataset,
       SPath.SP_URL_HTTPS + COALESCE(DS.DS_folder_name,DS.Dataset_Num)||'/QC/'||DS.Dataset_Num||'_BPI_MS.png' AS QC_Link,'http://prismsupport.pnl.gov/smaqc/index.php/smaqc/instrument/'||InstName.In_Name AS SMAQC,
       LCC.Cart_Name AS [LC Cart],
       RRH.RDS_BatchID AS Batch,
       RRH.ID AS Request,
       DRN.DRN_name AS Rating,
       DS.DS_comment AS [Comment],
       DSN.DSS_name AS State,
       InstName.IN_name AS Instrument,
       DS.DS_created AS Created,
       DS.DS_Oper_PRN AS [Oper.]
	FROM T_LC_Cart AS LCC
     INNER JOIN T_Requested_Run AS RRH
       ON LCC.ID = RRH.RDS_Cart_ID
     RIGHT OUTER JOIN t_dataset_state_name AS DSN
                      INNER JOIN t_dataset AS DS
                        ON DSN.Dataset_state_ID = DS.DS_state_ID
                      INNER JOIN T_Instrument_Name AS InstName
                        ON DS.DS_instrument_name_ID = InstName.Instrument_ID
                      INNER JOIN t_dataset_rating_name AS DRN
                        ON DS.DS_rating = DRN.DRN_state_ID
       ON RRH.DatasetID = DS.Dataset_ID
     INNER JOIN t_storage_path AS SPath
       ON SPath.SP_path_ID = DS.DS_storage_path_ID
WHERE (DS.DS_rating = -10)

;

CREATE VIEW "public"."v_dataset_disposition_lite"
AS
SELECT ID,
       [Sel.],
       Dataset,
       SMAQC,
       [LC Cart],
       Batch,
       Request,
       Rating,
       [Comment],
       State,
       Instrument,
       Created,
       [Oper.]
	FROM V_Dataset_Disposition

;

CREATE VIEW "public"."v_dataset_list_report_2"
AS
SELECT DS.Dataset_ID AS ID,
       DS.Dataset_Num AS Dataset,
       E.Experiment_Num AS Experiment,
       C.Campaign_Num AS Campaign,
       DSN.DSS_name AS State,
       DSInst.Instrument,
       DS.DS_created AS Created,
       DS.DS_comment AS [Comment],
       DSRating.DRN_name AS Rating,
       DTN.DST_name AS [Dataset Type],
       DS.DS_Oper_PRN AS Operator,
       DL.Dataset_Folder_Path AS [Dataset Folder Path],
       DL.Archive_Folder_Path AS [Archive Folder Path],
       DL.QC_Link AS QC_Link,
       COALESCE(DS.Acq_Time_Start, RR.RDS_Run_Start) AS [Acq Start],
       COALESCE(DS.Acq_Time_End, RR.RDS_Run_Finish) AS [Acq. End],
       DS.Acq_Length_Minutes AS [Acq Length],
       DS.Scan_Count AS [Scan Count],
       Cast(DS.File_Size_Bytes / 1024.0 / 1024 AS decimal(9,2)) AS [File Size MB],
       CartConfig.Cart_Config_Name AS [Cart Config],
       LC.SC_Column_Number AS [LC Column],
       DS.DS_sec_sep AS [Separation Type],
       -- Deprecated: RR.RDS_Blocking_Factor AS [Blocking Factor],
       -- Deprecated: RR.RDS_Block AS [Block],
       -- Deprecated: RR.RDS_Run_Order AS [Run Order],
       RR.ID AS Request,
       RR.RDS_BatchID AS Batch,
       RR.RDS_EUS_Proposal_ID AS [EMSL Proposal],
       -- Deprecated to improve performance: EPT.Abbreviation AS [EUS Proposal Type],
       RR.RDS_WorkPackage AS [Work Package],
       RR.RDS_Requestor_PRN AS Requester,
       -- Deprecated: DASN.DASN_StateName AS [Archive State],
       -- Deprecated: T_YesNo.Description AS [Inst. Data Purged],
       Org.OG_name AS Organism,
       BTO.Tissue,
       DS.DateSortKey AS #DateSortKey
	FROM t_dataset_state_name DSN
     INNER JOIN t_dataset DS
       ON DSN.Dataset_state_ID = DS.DS_state_ID
     INNER JOIN t_dataset_type_name DTN
       ON DS.DS_type_ID = DTN.DST_Type_ID
     LEFT OUTER JOIN T_Cached_Dataset_Instruments DSInst
       ON DS.Dataset_ID = DSInst.Dataset_ID
     INNER JOIN t_dataset_rating_name DSRating
       ON DS.DS_rating = DSRating.DRN_state_ID
     INNER JOIN t_experiments E
       ON DS.Exp_ID = E.Exp_ID
     INNER JOIN t_campaign C
       ON E.EX_campaign_ID = C.Campaign_ID
     LEFT OUTER JOIN T_Cached_Dataset_Links AS DL
       ON DS.Dataset_ID = DL.Dataset_ID
     INNER JOIN T_LC_Column LC
       ON DS.DS_LC_column_ID = LC.ID
     INNER JOIN t_organisms Org
       ON Org.Organism_ID = E.EX_organism_ID
     LEFT OUTER JOIN T_LC_Cart_Configuration CartConfig
       ON DS.Cart_Config_ID = CartConfig.Cart_Config_ID
     LEFT OUTER JOIN T_Requested_Run RR
       ON DS.Dataset_ID = RR.DatasetID













     LEFT OUTER JOIN S_V_BTO_ID_to_Name AS BTO
       ON BTO.Identifier = E.EX_Tissue_ID

;

CREATE VIEW "public"."v_dataset_load"
AS
SELECT t_dataset.Dataset_Num AS Dataset,
   t_experiments.Experiment_Num AS Experiment,
   T_Instrument_Name.IN_name AS Instrument,
   t_dataset.DS_created AS Created,
   t_dataset_state_name.DSS_name AS State,
   t_dataset_type_name.DST_name AS Type,
   t_dataset.DS_comment AS Comment,
   t_dataset.DS_Oper_PRN AS Operator,
   t_dataset.DS_well_num AS [Well Number],
   t_dataset.DS_sec_sep AS [Secondary Sep],
   t_dataset.DS_folder_name AS [Folder Name],
   t_dataset_rating_name.DRN_name AS Rating
	FROM t_dataset INNER JOIN
   t_dataset_state_name ON
   t_dataset.DS_state_ID = t_dataset_state_name.Dataset_state_ID INNER
    JOIN
   T_Instrument_Name ON
   t_dataset.DS_instrument_name_ID = T_Instrument_Name.Instrument_ID
    INNER JOIN
   t_dataset_type_name ON
   t_dataset.DS_type_ID = t_dataset_type_name.DST_Type_ID INNER
    JOIN
   t_experiments ON
   t_dataset.Exp_ID = t_experiments.Exp_ID INNER JOIN
   t_dataset_rating_name ON
   t_dataset.DS_rating = t_dataset_rating_name.DRN_state_ID
;

CREATE VIEW "public"."v_analysis_job"
AS
SELECT AJ.AJ_jobID AS Job,
       AnTool.AJT_toolName AS Tool,
       DS.Dataset_Num AS Dataset,
       DFP.Dataset_Folder_Path AS Dataset_Storage_Path,DFP.Dataset_Folder_Path||'\'||AJ.AJ_resultsFolderName As Results_Folder_Path,
       AJ.AJ_parmFileName AS ParmFileName,
       AJ.AJ_settingsFileName AS SettingsFileName,
       AnTool.AJT_parmFileStoragePath AS ParmFileStoragePath,
       AJ.AJ_organismDBName AS OrganismDBName,
       AJ.AJ_proteinCollectionList AS ProteinCollectionList,
       AJ.AJ_proteinOptionsList AS ProteinOptions,
       O.OG_organismDBPath AS OrganismDBStoragePath,
       AJ.AJ_StateID AS StateID,
       AJ.AJ_priority AS priority,
       AJ.AJ_comment AS [Comment],
       DS.DS_Comp_State AS CompState,
       InstName.IN_class AS InstClass,
       AJ.AJ_datasetID AS DatasetID,
       AJ.AJ_requestID AS RequestID,
       DFP.Archive_Folder_Path,
       DFP.MyEMSL_Path_Flag,
       DFP.Instrument_Data_Purged,
	   E.Experiment_Num As Experiment,
	   C.Campaign_Num As Campaign,
	   InstName.IN_name AS Instrument,
	   AJ.AJ_StateNameCached AS State,
	   AJ.AJ_jobID,
	   AJ.AJ_datasetID,
	   DS.DS_rating AS Rating,
       AJ.AJ_created AS Created,
       AJ.AJ_start AS Started,
       AJ.AJ_finish AS Finished,
	   CAST(AJ.AJ_ProcessingTimeMinutes AS DECIMAL(9, 2)) AS Runtime,
	   AJ.AJ_specialProcessing AS SpecialProcessing,
	   AJ.AJ_batchID
	FROM T_Analysis_Job AJ
     INNER JOIN t_dataset DS
       ON AJ.AJ_datasetID = DS.Dataset_ID
     INNER JOIN t_organisms O
       ON AJ.AJ_organismID = O.Organism_ID
     INNER JOIN T_Analysis_Tool AnTool
       ON AJ.AJ_analysisToolID = AnTool.AJT_toolID
     INNER JOIN T_Instrument_Name InstName
       ON DS.DS_instrument_name_ID = InstName.Instrument_ID
     INNER JOIN V_Dataset_Folder_Paths DFP
       ON DS.Dataset_ID = DFP.Dataset_ID
     INNER JOIN t_experiments E
       ON DS.Exp_ID = E.Exp_ID
     INNER JOIN t_campaign C
       ON E.EX_campaign_ID = C.Campaign_ID


;

CREATE VIEW "public"."v_analysis_job_detail_report_2"
AS
SELECT AJ.AJ_jobID AS JobNum,
       DS.Dataset_Num AS Dataset,
       E.Experiment_Num AS Experiment,
       DS.DS_folder_name AS [Dataset Folder],
       DFP.Dataset_Folder_Path AS [Dataset Folder Path],
       CASE
           WHEN COALESCE(DA.MyEmslState, 0) > 1 THEN ''
           ELSE DFP.Archive_Folder_Path
       END AS [Archive Folder Path],
       InstName.IN_name AS Instrument,
       AnalysisTool.AJT_toolName AS [Tool Name],
       AJ.AJ_parmFileName AS [Parm File],
       AnalysisTool.AJT_parmFileStoragePath AS [Parm File Storage Path],
       AJ.AJ_settingsFileName AS [Settings File],
       ExpOrg.OG_Name As [Organism],
       BTO.Tissue AS [Experiment Tissue],
       JobOrg.OG_name AS [Job Organism],
       AJ.AJ_organismDBName AS [Organism DB],
       public.GetFASTAFilePath(AJ.AJ_organismDBName, JobOrg.OG_name) AS [Organism DB Storage Path],
       AJ.AJ_proteinCollectionList AS [Protein Collection List],
       AJ.AJ_proteinOptionsList AS [Protein Options List],
       CASE WHEN AJ.AJ_StateID = 2 THEN ASN.AJS_name + ': ' +
              CAST(CAST(COALESCE(AJ.Progress, 0) AS DECIMAL(9,2)) AS VARCHAR(12)) + '%, ETA ' +
              CASE
                WHEN AJ.ETA_Minutes IS NULL THEN '??'
                WHEN AJ.ETA_Minutes > 3600 THEN CAST(CAST(AJ.ETA_Minutes/1440.0 AS DECIMAL(18,1)) AS VARCHAR(12)) + ' days'
                WHEN AJ.ETA_Minutes > 90 THEN CAST(CAST(AJ.ETA_Minutes/60.0 AS DECIMAL(18,1)) AS VARCHAR(12)) + ' hours'
                ELSE CAST(CAST(AJ.ETA_Minutes AS DECIMAL(18,1)) AS VARCHAR(12)) + ' minutes'
              END
           ELSE ASN.AJS_name
           END AS State,
       CAST(AJ.AJ_ProcessingTimeMinutes AS decimal(9, 2)) AS [Runtime Minutes],
       AJ.AJ_owner AS Owner,
       AJ.AJ_comment AS [Comment],
       AJ.AJ_specialProcessing AS [Special Processing],
       CASE
           WHEN AJ.AJ_Purged = 0 THEN public.udfCombinePaths(DFP.Dataset_Folder_Path, AJ.AJ_resultsFolderName)
           ELSE 'Purged: ' + public.udfCombinePaths(DFP.Dataset_Folder_Path, AJ.AJ_resultsFolderName)
       END AS [Results Folder Path],
       CASE
           WHEN AJ.AJ_MyEMSLState > 0 OR COALESCE(DA.MyEmslState, 0) > 1 THEN ''
           ELSE public.udfCombinePaths(DFP.Archive_Folder_Path, AJ.AJ_resultsFolderName)
       END AS [Archive Results Folder Path],
       CASE
           WHEN AJ.AJ_Purged = 0 THEN DFP.Dataset_URL + AJ.AJ_resultsFolderName + '/'
           ELSE DFP.Dataset_URL
       END AS [Data Folder Link],
       public.GetJobPSMStats(AJ.AJ_JobID) AS [PSM Stats],
       COALESCE(MTSPT.PT_DB_Count, 0) AS [MTS PT DB Count],
       COALESCE(MTSMT.MT_DB_Count, 0) AS [MTS MT DB Count],
       COALESCE(PMTaskCountQ.PMTasks, 0) AS [Peak Matching Results],
       AJ.AJ_created AS Created,
       AJ.AJ_start AS [Started],
       AJ.AJ_finish AS Finished,
       AJ.AJ_requestID AS Request,
       AJ.AJ_priority AS [Priority],
       AJ.AJ_assignedProcessorName AS [Assigned Processor],
       AJ.AJ_Analysis_Manager_Error AS [AM Code],
       public.GetDEMCodeString(AJ.AJ_Data_Extraction_Error) AS [DEM Code],
       CASE AJ.AJ_propagationMode
           WHEN 0 THEN 'Export'
           ELSE 'No Export'
       END AS [Export Mode],
       T_YesNo.Description AS [Dataset Unreviewed],
       t_myemsl_state.StateName AS [MyEMSL State],
      AJPG.Group_Name AS [Processor Group]
FROM S_V_BTO_ID_to_Name AS BTO
     RIGHT OUTER JOIN T_Analysis_Job AS AJ
                      INNER JOIN t_dataset AS DS
                        ON AJ.AJ_datasetID = DS.Dataset_ID
                      INNER JOIN t_experiments AS E
                        ON DS.Exp_ID = E.Exp_ID
                      INNER JOIN t_organisms ExpOrg
                        ON E.EX_organism_ID = ExpOrg.Organism_ID
                      LEFT OUTER JOIN V_Dataset_Folder_Paths AS DFP
                        ON DFP.Dataset_ID = DS.Dataset_ID
                      INNER JOIN T_Storage_Path AS SPath
                        ON DS.DS_storage_path_ID = SPath.SP_path_ID
                      INNER JOIN T_Analysis_Tool AS AnalysisTool
                        ON AJ.AJ_analysisToolID = AnalysisTool.AJT_toolID
                      INNER JOIN t_analysis_job_state AS ASN
                        ON AJ.AJ_StateID = ASN.AJS_stateID
                      INNER JOIN T_Instrument_Name AS InstName
                        ON DS.DS_instrument_name_ID = InstName.Instrument_ID
                      INNER JOIN t_organisms AS JobOrg
                        ON JobOrg.Organism_ID = AJ.AJ_organismID
                      INNER JOIN T_YesNo
                        ON AJ.AJ_DatasetUnreviewed = T_YesNo.Flag
                      INNER JOIN t_myemsl_state
                        ON AJ.AJ_MyEMSLState = t_myemsl_state.MyEMSLState
       ON BTO.Identifier = E.EX_Tissue_ID
     LEFT OUTER JOIN T_Analysis_Job_Processor_Group AS AJPG
                     INNER JOIN T_Analysis_Job_Processor_Group_Associations AS AJPJA
                       ON AJPG.ID = AJPJA.Group_ID
       ON AJ.AJ_jobID = AJPJA.Job_ID
     LEFT OUTER JOIN ( SELECT Job,
                              COUNT(*) AS MT_DB_Count
                       FROM T_MTS_MT_DB_Jobs_Cached
                       GROUP BY Job ) AS MTSMT
       ON AJ.AJ_jobID = MTSMT.Job
     LEFT OUTER JOIN ( SELECT Job,
                              COUNT(*) AS PT_DB_Count
                       FROM T_MTS_PT_DB_Jobs_Cached
                       GROUP BY Job ) AS MTSPT
       ON AJ.AJ_jobID = MTSPT.Job
     LEFT OUTER JOIN ( SELECT DMS_Job,
                              COUNT(*) AS PMTasks

	FROM T_MTS_Peak_Matching_Tasks_Cached AS PM
                       GROUP BY DMS_Job ) AS PMTaskCountQ
       ON PMTaskCountQ.DMS_Job = AJ.AJ_jobID
     LEFT OUTER JOIN t_dataset_archive AS DA
       ON DS.Dataset_ID = DA.AS_Dataset_ID

;

CREATE VIEW "public"."v_analysis_job_entry"
AS
SELECT CAST(AJ.AJ_jobID AS varchar(32)) AS Job,
       AJ.AJ_priority,
       AnalysisTool.AJT_toolName AS AJ_ToolName,
       DS.Dataset_Num AS AJ_Dataset,
       AJ.AJ_parmFileName AS AJ_ParmFile,
       AJ.AJ_settingsFileName AS AJ_SettingsFile,
       Org.OG_name AS AJ_Organism,
       AJ.AJ_organismDBName AS AJ_OrganismDB,
       AJ.AJ_owner,
       AJ.AJ_comment,
       AJ.AJ_specialProcessing,
       AJ.AJ_batchID,
       AJ.AJ_assignedProcessorName,
       AJ.AJ_proteinCollectionList AS protCollNameList,
       AJ.AJ_proteinOptionsList AS protCollOptionsList,
       ASN.AJS_name AS stateName,
       CASE AJ.AJ_propagationMode
           WHEN 0 THEN 'Export'
           ELSE 'No Export'
       END AS propagationMode,
       AJPG.Group_Name AS associatedProcessorGroup
	FROM T_Analysis_Job_Processor_Group AJPG
     INNER JOIN T_Analysis_Job_Processor_Group_Associations AJPGA
       ON AJPG.ID = AJPGA.Group_ID
     RIGHT OUTER JOIN T_Analysis_Job AJ
                      INNER JOIN t_dataset DS
                        ON AJ.AJ_datasetID = DS.Dataset_ID
                      INNER JOIN t_organisms Org
                        ON AJ.AJ_organismID = Org.Organism_ID
                      INNER JOIN T_Analysis_Tool AnalysisTool
                        ON AJ.AJ_analysisToolID = AnalysisTool.AJT_toolID
                      INNER JOIN t_analysis_job_state ASN
                        ON AJ.AJ_StateID = ASN.AJS_stateID
       ON AJPGA.Job_ID = AJ.AJ_jobID
;

