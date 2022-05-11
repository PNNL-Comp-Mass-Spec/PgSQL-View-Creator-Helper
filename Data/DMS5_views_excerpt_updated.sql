SET search_path TO public, sw, cap, dpkg, mc, ont;
SHOW search_path;

-- PostgreSQL stores views as Parse Trees, meaning any whitespace that is present in the CREATE VIEW statements will be lost
--
-- The PgSQL View Creator Helper will convert any comments on views to COMMENT ON VIEW statements

-- The following set commands are specific to psql
\set ON_ERROR_STOP
\set ECHO all
BEGIN;
ALTER TABLE "public"."t_analysis_job" ADD CONSTRAINT "ck_t_analysis_job_propagation_mode" CHECK (((propagation_mode=(1) OR propagation_mode=(0))));
ALTER TABLE "public"."t_analysis_job_processor_group_membership" ADD CONSTRAINT "ck_t_analysis_job_processor_group_membership_enabled" CHECK ((membership_enabled = 'n' or membership_enabled = 'Y'));
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

CREATE OR REPLACE VIEW "public"."v_organism_export"
AS
SELECT DISTINCT O.Organism_ID,
                O.organism AS Name,
                O.description AS Description,
                O.short_name AS Short_Name,
                NCBI.Name AS NCBI_Taxonomy,
                O.NCBI_Taxonomy_ID AS NCBI_Taxonomy_ID,
                NCBI.Synonyms AS NCBI_Synonyms,
                O.domain AS Domain,
                O.kingdom AS Kingdom,
                O.phylum AS Phylum,
                O.class AS Class,
                O.order AS "Order",
                O.family AS Family,
                O.genus AS Genus,
                O.species AS Species,
                O.strain AS Strain,
                O.dna_translation_table_id AS DNA_Translation_Table_ID,
                O.mito_dna_translation_table_id AS Mito_DNA_Translation_Table_ID,
                O.NCBI_Taxonomy_ID AS NEWT_ID,
                NEWT.Term_Name AS NEWT_Name,
                O.NEWT_ID_List AS NEWT_ID_List,
                O.created AS Created,
                O.active AS Active,
                O.organism_db_path AS OrganismDBPath,
                -- Remove or update since skipped column: O.OG_RowVersion
    FROM public.t_organisms O
     LEFT OUTER JOIN S_V_CV_NEWT NEWT
       ON CAST(O.NCBI_Taxonomy_ID AS varchar(24)) = NEWT.identifier
     LEFT OUTER JOIN S_V_NCBI_Taxonomy_Cached NCBI
       ON O.NCBI_Taxonomy_ID = NCBI.Tax_ID
;
CREATE OR REPLACE VIEW "public"."v_eus_export_dataset_metadata"
AS
SELECT D.Dataset_ID AS Dataset_ID,
       D.dataset AS Dataset,
       Inst.instrument AS Instrument,
       EUS_Inst.EUS_Instrument_ID AS EUS_Instrument_ID,
       DTN.dataset_type AS Dataset_Type,
       COALESCE(D.Acq_Time_Start, D.created) AS Dataset_Acq_Time_Start,
       U_DS_Operator.username AS Instrument_Operator,
       DRN.dataset_rating AS Dataset_Rating,
       E.experiment AS Experiment,
       O.organism AS Organism,
       E.reason AS Experiment_Reason,
       E.comment AS Experiment_Comment,
       U_Ex_Researcher.username AS Experiment_Researcher,
       SPR.ID AS Prep_Request_ID,
       SPR.Assigned_Personnel AS Prep_Request_Staff,
       SPRState.State_Name AS Prep_Request_State,
       C.campaign AS Campaign,
       COALESCE(U_ProjMgr.username, C.project_mgr_prn) AS Project_Manager,
       COALESCE(U_PI.username, C.pi_prn) AS Project_PI,
       COALESCE(U_TechLead.username, C.technical_lead) AS Project_Technical_Lead,
       D.operator_prn AS Instrument_Operator_PRN,
       E.researcher_prn AS Experiment_Researcher_PRN,
       C.project_mgr_prn AS Project_Manager_PRN,
       C.pi_prn AS Project_PI_PRN,
       C.technical_lead AS Project_Technical_Lead_PRN,
       EUT.eus_usage_type AS EUS_Usage,
       RR.eus_proposal_id AS EUS_Proposal,APath.AP_archive_path||'/'||D.DS_folder_name AS Dataset_Path_Aurora
    FROM public.t_campaign C
     INNER JOIN public.t_dataset D
                INNER JOIN public.t_instrument_name Inst
                  ON D.instrument_id = Inst.Instrument_ID
                INNER JOIN public.t_dataset_type_name DTN
                  ON D.dataset_type_ID = DTN.dataset_type_id
                INNER JOIN public.t_users U_DS_Operator
                  ON D.operator_prn = U_DS_Operator.prn
                INNER JOIN public.t_dataset_rating_name DRN
                  ON D.dataset_rating_id = DRN.dataset_rating_id
                INNER JOIN public.t_experiments E
                  ON D.Exp_ID = E.Exp_ID
                INNER JOIN public.t_users U_Ex_Researcher
                  ON E.researcher_prn = U_Ex_Researcher.prn
                INNER JOIN public.t_organisms O
                  ON E.organism_id = O.Organism_ID
       ON C.Campaign_ID = E.campaign_id
     LEFT OUTER JOIN public.t_users U_TechLead
       ON C.technical_lead = U_TechLead.prn
     LEFT OUTER JOIN public.t_users U_PI
       ON C.pi_prn = U_PI.prn
     LEFT OUTER JOIN public.t_users U_ProjMgr
       ON C.project_mgr_prn = U_ProjMgr.prn
     LEFT OUTER JOIN public.t_sample_prep_request SPR
       ON E.sample_prep_request_id = SPR.ID AND
          SPR.ID <> 0
     LEFT OUTER JOIN public.t_sample_prep_request_state_name SPRState
       ON SPR.State = SPRState.State_ID
     LEFT OUTER JOIN public.t_requested_run RR
       ON RR.dataset_id = D.Dataset_ID
     LEFT OUTER JOIN public.t_eus_usage_type EUT
       ON EUT.ID = RR.eus_usage_type_id
     LEFT OUTER JOIN V_EUS_Instrument_ID_Lookup EUS_Inst
       ON EUS_Inst.Instrument_Name =  Inst.instrument
     LEFT OUTER JOIN public.t_dataset_archive DA
       ON DA.dataset_id = D.Dataset_ID
     LEFT OUTER JOIN public.t_archive_path APath
       ON APath.archive_path_id = DA.storage_path_id
WHERE D.DS_State_ID=3 AND D.dataset_rating_id NOT IN (-1, -2, -5)
;
CREATE OR REPLACE VIEW "public"."v_dataset_disposition"
AS
SELECT DS.Dataset_ID AS id,
       '' AS sel,
       DS.dataset,
       SPath.url_https + COALESCE(DS.folder_name,DS.dataset)||'/QC/'||DS.dataset||'_BPI_MS.png' AS qc_link,
       'http://prismsupport.pnl.gov/smaqc/index.php/smaqc/instrument/'||InstName.instrument AS smaqc,
       LCC.Cart_Name AS lc_cart,
       RRH.batch_id AS batch,
       RRH.ID AS request,
       DRN.dataset_rating AS rating,
       DS.comment,
       DSN.dataset_state AS state,
       InstName.instrument,
       DS.created,
       DS.operator_prn AS oper
    FROM public.t_lc_cart AS LCC
     INNER JOIN public.t_requested_run AS RRH
       ON LCC.ID = RRH.cart_id
     RIGHT OUTER JOIN public.t_dataset_state_name AS DSN
                      INNER JOIN public.t_dataset AS DS
                        ON DSN.ds_state_id = DS.DS_state_ID
                      INNER JOIN public.t_instrument_name AS InstName
                        ON DS.instrument_id = InstName.Instrument_ID
                      INNER JOIN public.t_dataset_rating_name AS DRN
                        ON DS.dataset_rating_id = DRN.dataset_rating_id
       ON RRH.dataset_id = DS.Dataset_ID
     INNER JOIN public.t_storage_path AS SPath
       ON SPath.storage_path_id = DS.storage_path_ID
WHERE (DS.dataset_rating_id = -10)
;
CREATE OR REPLACE VIEW "public"."v_dataset_disposition_lite"
AS
SELECT ID,
       "Sel.",
       Dataset,
       SMAQC,
       "LC Cart",
       Batch,
       Request,
       Rating,
       "Comment",
       State,
       Instrument,
       Created,
       "Oper."
    FROM V_Dataset_Disposition
;
CREATE OR REPLACE VIEW "public"."v_dataset_list_report_2"
AS
SELECT DS.Dataset_ID AS id,
       DS.dataset,
       E.experiment,
       C.campaign,
       DSN.dataset_state AS state,
       DSInst.Instrument,
       DS.created,
       DS.comment,
       DSRating.dataset_rating AS rating,
       DTN.dataset_type,
       DS.operator_prn AS operator,
       DL.Dataset_Folder_Path,
       DL.Archive_Folder_Path,
       DL.QC_Link,
       COALESCE(DS.Acq_Time_Start, RR.request_run_start) AS acq_start,
       COALESCE(DS.Acq_Time_End, RR.request_run_finish) AS acq_end,
       DS.Acq_Length_Minutes AS acq_length,
       DS.Scan_Count,
       Cast(DS.File_Size_Bytes / 1024.0 / 1024 AS decimal(9,2)) AS file_size_mb,
       CartConfig.Cart_Config_Name AS cart_config,
       LC.lc_column,
       DS.separation_type,
       RR.ID AS request,
       RR.batch_id AS batch,
       RR.eus_proposal_id AS emsl_proposal,
       RR.work_package,
       RR.requester_prn AS requester,
       Org.organism,
       BTO.Tissue,
       DS.date_sort_key AS #DateSortKey
    FROM public.t_dataset_state_name DSN
     INNER JOIN public.t_dataset DS
       ON DSN.ds_state_id = DS.DS_state_ID
     INNER JOIN public.t_dataset_type_name DTN
       ON DS.dataset_type_ID = DTN.dataset_type_id
     LEFT OUTER JOIN public.t_cached_dataset_instruments DSInst
       ON DS.Dataset_ID = DSInst.Dataset_ID
     INNER JOIN public.t_dataset_rating_name DSRating
       ON DS.dataset_rating_id = DSRating.dataset_rating_id
     INNER JOIN public.t_experiments E
       ON DS.Exp_ID = E.Exp_ID
     INNER JOIN public.t_campaign C
       ON E.campaign_id = C.Campaign_ID
     LEFT OUTER JOIN public.t_cached_dataset_links AS DL
       ON DS.Dataset_ID = DL.Dataset_ID
     INNER JOIN public.t_lc_column LC
       ON DS.lc_column_ID = LC.ID
     INNER JOIN public.t_organisms Org
       ON Org.Organism_ID = E.organism_id
     LEFT OUTER JOIN public.t_lc_cart_configuration CartConfig
       ON DS.Cart_Config_ID = CartConfig.Cart_Config_ID
     LEFT OUTER JOIN public.t_requested_run RR
       ON DS.Dataset_ID = RR.dataset_id
     LEFT OUTER JOIN S_V_BTO_ID_to_Name AS BTO
       ON BTO.Identifier = E.tissue_id
;
COMMENT ON VIEW "public"."v_dataset_list_report_2" IS 'Deprecated: RR.RDS_Blocking_Factor AS [Blocking Factor],. Deprecated: RR.RDS_Block AS [Block],. Deprecated: RR.RDS_Run_Order AS [Run Order],. Deprecated to improve performance: EPT.Abbreviation AS [EUS Proposal Type],. Deprecated: DASN.DASN_StateName AS [Archive State],. Deprecated: T_YesNo.Description AS [Inst. Data Purged],';

CREATE OR REPLACE VIEW "public"."v_dataset_load"
AS
SELECT public.t_dataset.dataset,
   public.t_experiments.experiment,
   public.t_instrument_name.instrument,
   public.t_dataset.created,
   public.t_dataset_state_name.dataset_state AS state,
   public.t_dataset_type_name.dataset_type AS type,
   public.t_dataset.comment,
   public.t_dataset.operator_prn AS operator,
   public.t_dataset.well AS well_number,
   public.t_dataset.separation_type AS secondary_sep,
   public.t_dataset.folder_name,
   public.t_dataset_rating_name.dataset_rating AS rating
    FROM public.t_dataset INNER JOIN
   public.t_dataset_state_name ON
   public.t_dataset.DS_state_ID = public.t_dataset_state_name.ds_state_id INNER
    JOIN
   public.t_instrument_name ON
   public.t_dataset.instrument_id = public.t_instrument_name.Instrument_ID
    INNER JOIN
   public.t_dataset_type_name ON
   public.t_dataset.dataset_type_ID = public.t_dataset_type_name.dataset_type_id INNER
    JOIN
   public.t_experiments ON
   public.t_dataset.Exp_ID = public.t_experiments.Exp_ID INNER JOIN
   public.t_dataset_rating_name ON
   public.t_dataset.dataset_rating_id = public.t_dataset_rating_name.dataset_rating_id
;
CREATE OR REPLACE VIEW "public"."v_analysis_job"
AS
SELECT AJ.job,
       AnTool.analysis_tool AS tool,
       DS.dataset,
       DFP.Dataset_Folder_Path AS dataset_storage_path,
       DFP.Dataset_Folder_Path||'\'||AJ.results_folder_name As results_folder_path,
       AJ.param_file_name AS parm_file_name,
       AJ.settings_file_name,
       AnTool.param_file_storage_path AS parm_file_storage_path,
       AJ.organism_db_name AS organism_dbname,
       AJ.protein_collection_list,
       AJ.protein_options_list AS protein_options,
       O.organism_db_path AS organism_dbstorage_path,
       AJ.job_state_id AS state_id,
       AJ.priority AS priority,
       AJ.comment,
       DS.DS_Comp_State AS comp_state,
       InstName.instrument_class AS inst_class,
       AJ.dataset_id,
       AJ.request_id,
       DFP.Archive_Folder_Path,
       DFP.MyEMSL_Path_Flag,
       DFP.Instrument_Data_Purged,
       E.experiment,
       C.campaign,
       InstName.instrument,
       AJ.state_name_cached AS state,
       AJ.job,
       AJ.dataset_id,
       DS.dataset_rating_id AS rating,
       AJ.created,
       AJ.start AS started,
       AJ.finish AS finished,
       CAST(AJ.processing_time_minutes AS decimal(9, 2)) AS runtime,
       AJ.special_processing,
       AJ.batch_id
    FROM public.t_analysis_job AJ
     INNER JOIN public.t_dataset DS
       ON AJ.dataset_id = DS.Dataset_ID
     INNER JOIN public.t_organisms O
       ON AJ.organism_id = O.Organism_ID
     INNER JOIN public.t_analysis_tool AnTool
       ON AJ.analysis_tool_id = AnTool.analysis_tool_id
     INNER JOIN public.t_instrument_name InstName
       ON DS.instrument_id = InstName.Instrument_ID
     INNER JOIN V_Dataset_Folder_Paths DFP
       ON DS.Dataset_ID = DFP.Dataset_ID
     INNER JOIN public.t_experiments E
       ON DS.Exp_ID = E.Exp_ID
     INNER JOIN public.t_campaign C
       ON E.campaign_id = C.Campaign_ID
;
CREATE OR REPLACE VIEW "public"."v_analysis_job_detail_report_2"
AS
SELECT AJ.job AS job_num,
       DS.dataset,
       E.experiment,
       DS.folder_name AS dataset_folder,
       DFP.Dataset_Folder_Path,
       CASE
           WHEN COALESCE(DA.myemsl_state, 0) > 1 THEN ''
           ELSE DFP.Archive_Folder_Path
       END AS archive_folder_path,
       InstName.instrument,
       AnalysisTool.analysis_tool AS tool_name,
       AJ.param_file_name AS parm_file,
       AnalysisTool.param_file_storage_path AS parm_file_storage_path,
       AJ.settings_file_name AS settings_file,
       ExpOrg.organism,
       BTO.Tissue AS experiment_tissue,
       JobOrg.organism AS job_organism,
       AJ.organism_db_name AS organism_db,
       public.GetFASTAFilePath(AJ.organism_db_name, JobOrg.organism) AS organism_db_storage_path,
       AJ.protein_collection_list,
       AJ.protein_options_list,
       CASE WHEN AJ.job_state_id = 2 THEN ASN.job_state || ': ' ||
              CAST(CAST(COALESCE(AJ.Progress, 0) AS decimal(9,2)) AS varchar(12)) || '%, ETA ' ||
              CASE
                WHEN AJ.ETA_Minutes IS NULL THEN '??'
                WHEN AJ.ETA_Minutes > 3600 THEN CAST(CAST(AJ.ETA_Minutes/1440.0 AS decimal(18,1)) AS varchar(12)) || ' days'
                WHEN AJ.ETA_Minutes > 90 THEN CAST(CAST(AJ.ETA_Minutes/60.0 AS decimal(18,1)) AS varchar(12)) || ' hours'
                ELSE CAST(CAST(AJ.ETA_Minutes AS decimal(18,1)) AS varchar(12)) || ' minutes'
              END
           ELSE ASN.job_state
           END AS state,
       CAST(AJ.processing_time_minutes AS decimal(9, 2)) AS runtime_minutes,
       AJ.owner,
       AJ.comment,
       AJ.special_processing,
       CASE
           WHEN AJ.purged = 0 THEN public.udfCombinePaths(DFP.Dataset_Folder_Path, AJ.results_folder_name)
           ELSE 'Purged: ' || public.udfCombinePaths(DFP.Dataset_Folder_Path, AJ.results_folder_name)
       END AS results_folder_path,
       CASE
           WHEN AJ.myemsl_state > 0 OR COALESCE(DA.myemsl_state, 0) > 1 THEN ''
           ELSE public.udfCombinePaths(DFP.Archive_Folder_Path, AJ.results_folder_name)
       END AS archive_results_folder_path,
       CASE
           WHEN AJ.purged = 0 THEN DFP.Dataset_URL + AJ.results_folder_name || '/'
           ELSE DFP.Dataset_URL
       END AS data_folder_link,
       public.GetJobPSMStats(AJ.job) AS psm_stats,
       COALESCE(MTSPT.PT_DB_Count, 0) AS mts_pt_db_count,
       COALESCE(MTSMT.MT_DB_Count, 0) AS mts_mt_db_count,
       COALESCE(PMTaskCountQ.PMTasks, 0) AS peak_matching_results,
       AJ.created,
       AJ.start AS started,
       AJ.finish AS finished,
       AJ.request_id AS request,
       AJ.priority,
       AJ.assigned_processor_name AS assigned_processor,
       AJ.analysis_manager_error AS am_code,
       public.GetDEMCodeString(AJ.data_extraction_error) AS dem_code,
       CASE AJ.propagation_mode
           WHEN 0 THEN 'Export'
           ELSE 'No Export'
       END AS export_mode,
       public.t_yes_no.Description AS dataset_unreviewed,
       public.t_myemsl_state.myemsl_state_name AS myemsl_state,
      AJPG.Group_Name AS processor_group
FROM S_V_BTO_ID_to_Name AS BTO
     RIGHT OUTER JOIN public.t_analysis_job AS AJ
                      INNER JOIN public.t_dataset AS DS
                        ON AJ.dataset_id = DS.Dataset_ID
                      INNER JOIN public.t_experiments AS E
                        ON DS.Exp_ID = E.Exp_ID
                      INNER JOIN public.t_organisms ExpOrg
                        ON E.organism_id = ExpOrg.Organism_ID
                      LEFT OUTER JOIN V_Dataset_Folder_Paths AS DFP
                        ON DFP.Dataset_ID = DS.Dataset_ID
                      INNER JOIN public.t_storage_path AS SPath
                        ON DS.storage_path_ID = SPath.storage_path_id
                      INNER JOIN public.t_analysis_tool AS AnalysisTool
                        ON AJ.analysis_tool_id = AnalysisTool.analysis_tool_id
                      INNER JOIN public.t_analysis_job_state AS ASN
                        ON AJ.job_state_id = ASN.job_state_id
                      INNER JOIN public.t_instrument_name AS InstName
                        ON DS.instrument_id = InstName.Instrument_ID
                      INNER JOIN public.t_organisms AS JobOrg
                        ON JobOrg.Organism_ID = AJ.organism_id
                      INNER JOIN public.t_yes_no
                        ON AJ.dataset_unreviewed = public.t_yes_no.Flag
                      INNER JOIN public.t_myemsl_state
                        ON AJ.myemsl_state = public.t_myemsl_state.myemsl_state
       ON BTO.Identifier = E.tissue_id
     LEFT OUTER JOIN public.t_analysis_job_processor_group AS AJPG
                     INNER JOIN public.t_analysis_job_processor_group_associations AS AJPJA
                       ON AJPG.ID = AJPJA.Group_ID
       ON AJ.job = AJPJA.job
     LEFT OUTER JOIN ( SELECT Job,
                              COUNT(*) AS MT_DB_Count
                       FROM public.t_mts_mt_db_jobs_cached
                       GROUP BY Job ) AS MTSMT
       ON AJ.job = MTSMT.Job
     LEFT OUTER JOIN ( SELECT Job,
                              COUNT(*) AS PT_DB_Count
                       FROM public.t_mts_pt_db_jobs_cached
                       GROUP BY Job ) AS MTSPT
       ON AJ.job = MTSPT.Job
     LEFT OUTER JOIN ( SELECT DMS_Job,
                              COUNT(*) AS PMTasks
    FROM public.t_mts_peak_matching_tasks_cached AS PM
                       GROUP BY DMS_Job ) AS PMTaskCountQ
       ON PMTaskCountQ.DMS_Job = AJ.job
     LEFT OUTER JOIN public.t_dataset_archive AS DA
       ON DS.Dataset_ID = DA.dataset_id
;
CREATE OR REPLACE VIEW "public"."v_analysis_job_entry"
AS
SELECT CAST(AJ.job AS varchar(32)) AS job,
       AJ.priority,
       AnalysisTool.analysis_tool AS aj_tool_name,
       DS.dataset AS aj_dataset,
       AJ.param_file_name AS aj_parm_file,
       AJ.settings_file_name AS aj_settings_file,
       Org.organism AS aj_organism,
       AJ.organism_db_name AS aj_organism_db,
       AJ.owner,
       AJ.comment,
       AJ.special_processing,
       AJ.batch_id,
       AJ.assigned_processor_name,
       AJ.protein_collection_list AS prot_coll_name_list,
       AJ.protein_options_list AS prot_coll_options_list,
       ASN.job_state AS state_name,
       CASE AJ.propagation_mode
           WHEN 0 THEN 'Export'
           ELSE 'No Export'
       END AS propagation_mode,
       AJPG.Group_Name AS associated_processor_group
    FROM public.t_analysis_job_processor_group AJPG
     INNER JOIN public.t_analysis_job_processor_group_associations AJPGA
       ON AJPG.ID = AJPGA.Group_ID
     RIGHT OUTER JOIN public.t_analysis_job AJ
                      INNER JOIN public.t_dataset DS
                        ON AJ.dataset_id = DS.Dataset_ID
                      INNER JOIN public.t_organisms Org
                        ON AJ.organism_id = Org.Organism_ID
                      INNER JOIN public.t_analysis_tool AnalysisTool
                        ON AJ.analysis_tool_id = AnalysisTool.analysis_tool_id
                      INNER JOIN public.t_analysis_job_state ASN
                        ON AJ.job_state_id = ASN.job_state_id
       ON AJPGA.job = AJ.job
;

SELECT * FROM "public"."v_organism_export";
SELECT * FROM "public"."v_eus_export_dataset_metadata";
SELECT * FROM "public"."v_dataset_disposition";
SELECT * FROM "public"."v_dataset_disposition_lite";
SELECT * FROM "public"."v_dataset_list_report_2";
SELECT * FROM "public"."v_dataset_load";
SELECT * FROM "public"."v_analysis_job";
SELECT * FROM "public"."v_analysis_job_detail_report_2";
SELECT * FROM "public"."v_analysis_job_entry";
