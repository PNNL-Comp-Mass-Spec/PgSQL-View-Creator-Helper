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

CREATE OR REPLACE VIEW "public"."v_dataset_disposition"
AS
SELECT DS.dataset_id AS id,
       '' AS sel,
       DS.dataset,
       SPath.url_https + COALESCE(DS.folder_name,DS.dataset)||'/QC/'||DS.dataset||'_BPI_MS.png' AS qc_link,
       'http://prismsupport.pnl.gov/smaqc/index.php/smaqc/instrument/'||InstName.instrument AS smaqc,
       LCC.cart_name AS lc_cart,
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
                        ON DSN.ds_state_id = DS.ds_state_id
                      INNER JOIN public.t_instrument_name AS InstName
                        ON DS.instrument_id = InstName.instrument_id
                      INNER JOIN public.t_dataset_rating_name AS DRN
                        ON DS.dataset_rating_id = DRN.dataset_rating_id
       ON RRH.dataset_id = DS.dataset_id
     INNER JOIN public.t_storage_path AS SPath
       ON SPath.storage_path_id = DS.storage_path_id
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
SELECT DS.dataset_id AS id,
       DS.dataset,
       E.experiment,
       C.campaign,
       DSN.dataset_state AS state,
       DSInst.instrument,
       DS.created,
       DS.comment,
       DSRating.dataset_rating AS rating,
       DTN.dataset_type,
       DS.operator_prn AS operator,
       DL.dataset_folder_path,
       DL.archive_folder_path,
       DL.qc_link,
       COALESCE(DS.acq_time_start, RR.request_run_start) AS acq_start,
       COALESCE(DS.acq_time_end, RR.request_run_finish) AS acq_end,
       DS.acq_length_minutes AS acq_length,
       DS.scan_count,
       Cast(DS.file_size_bytes / 1024.0 / 1024 AS decimal(9,2)) AS file_size_mb,
       CartConfig.cart_config_name AS cart_config,
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
       ON DSN.ds_state_id = DS.ds_state_id
     INNER JOIN public.t_dataset_type_name DTN
       ON DS.dataset_type_id = DTN.dataset_type_id
     LEFT OUTER JOIN public.t_cached_dataset_instruments DSInst
       ON DS.dataset_id = DSInst.dataset_id
     INNER JOIN public.t_dataset_rating_name DSRating
       ON DS.dataset_rating_id = DSRating.dataset_rating_id
     INNER JOIN public.t_experiments E
       ON DS.exp_id = E.exp_id
     INNER JOIN public.t_campaign C
       ON E.campaign_id = C.campaign_id
     LEFT OUTER JOIN public.t_cached_dataset_links AS DL
       ON DS.dataset_id = DL.dataset_id
     INNER JOIN public.t_lc_column LC
       ON DS.lc_column_id = LC.ID
     INNER JOIN public.t_organisms Org
       ON Org.organism_id = E.organism_id
     LEFT OUTER JOIN public.t_lc_cart_configuration CartConfig
       ON DS.cart_config_id = CartConfig.cart_config_id
     LEFT OUTER JOIN public.t_requested_run RR
       ON DS.dataset_id = RR.dataset_id
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
   public.t_dataset.ds_state_id = public.t_dataset_state_name.ds_state_id INNER
    JOIN
   public.t_instrument_name ON
   public.t_dataset.instrument_id = public.t_instrument_name.instrument_id
    INNER JOIN
   public.t_dataset_type_name ON
   public.t_dataset.dataset_type_id = public.t_dataset_type_name.dataset_type_id INNER
    JOIN
   public.t_experiments ON
   public.t_dataset.exp_id = public.t_experiments.exp_id INNER JOIN
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
       DS.ds_comp_state AS comp_state,
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
       ON AJ.dataset_id = DS.dataset_id
     INNER JOIN public.t_organisms O
       ON AJ.organism_id = O.organism_id
     INNER JOIN public.t_analysis_tool AnTool
       ON AJ.analysis_tool_id = AnTool.analysis_tool_id
     INNER JOIN public.t_instrument_name InstName
       ON DS.instrument_id = InstName.instrument_id
     INNER JOIN V_Dataset_Folder_Paths DFP
       ON DS.dataset_id = DFP.dataset_id
     INNER JOIN public.t_experiments E
       ON DS.exp_id = E.exp_id
     INNER JOIN public.t_campaign C
       ON E.campaign_id = C.campaign_id
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
              CAST(CAST(COALESCE(AJ.progress, 0) AS decimal(9,2)) AS varchar(12)) || '%, ETA ' ||
              CASE
                WHEN AJ.eta_minutes IS NULL THEN '??'
                WHEN AJ.eta_minutes > 3600 THEN CAST(CAST(AJ.eta_minutes/1440.0 AS decimal(18,1)) AS varchar(12)) || ' days'
                WHEN AJ.eta_minutes > 90 THEN CAST(CAST(AJ.eta_minutes/60.0 AS decimal(18,1)) AS varchar(12)) || ' hours'
                ELSE CAST(CAST(AJ.eta_minutes AS decimal(18,1)) AS varchar(12)) || ' minutes'
              END
           ELSE ASN.job_state
           END AS state,
       CAST(AJ.processing_time_minutes AS decimal(9, 2)) AS runtime_minutes,
       AJ.owner,
       AJ.comment,
       AJ.special_processing,
       CASE
           WHEN AJ.purged = 0 THEN public.udfCombinePaths(DFP.Dataset_Folder_Path, AJ.results_folder_name)
           ELSE 'purged: ' || public.udfCombinePaths(DFP.Dataset_Folder_Path, AJ.results_folder_name)
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
       public.t_yes_no.description AS dataset_unreviewed,
       public.t_myemsl_state.myemsl_state_name AS myemsl_state,
      AJPG.group_name AS processor_group
FROM S_V_BTO_ID_to_Name AS BTO
     RIGHT OUTER JOIN public.t_analysis_job AS AJ
                      INNER JOIN public.t_dataset AS DS
                        ON AJ.dataset_id = DS.dataset_id
                      INNER JOIN public.t_experiments AS E
                        ON DS.exp_id = E.exp_id
                      INNER JOIN public.t_organisms ExpOrg
                        ON E.organism_id = ExpOrg.organism_id
                      LEFT OUTER JOIN V_Dataset_Folder_Paths AS DFP
                        ON DFP.dataset_id = DS.dataset_id
                      INNER JOIN public.t_storage_path AS SPath
                        ON DS.storage_path_id = SPath.storage_path_id
                      INNER JOIN public.t_analysis_tool AS AnalysisTool
                        ON AJ.analysis_tool_id = AnalysisTool.analysis_tool_id
                      INNER JOIN public.t_analysis_job_state AS ASN
                        ON AJ.job_state_id = ASN.job_state_id
                      INNER JOIN public.t_instrument_name AS InstName
                        ON DS.instrument_id = InstName.instrument_id
                      INNER JOIN public.t_organisms AS JobOrg
                        ON JobOrg.organism_id = AJ.organism_id
                      INNER JOIN public.t_yes_no
                        ON AJ.dataset_unreviewed = public.t_yes_no.flag
                      INNER JOIN public.t_myemsl_state
                        ON AJ.myemsl_state = public.t_myemsl_state.myemsl_state
       ON BTO.Identifier = E.tissue_id
     LEFT OUTER JOIN public.t_analysis_job_processor_group AS AJPG
                     INNER JOIN public.t_analysis_job_processor_group_associations AS AJPJA
                       ON AJPG.ID = AJPJA.group_id
       ON AJ.job = AJPJA.job
     LEFT OUTER JOIN ( SELECT job,
                              COUNT(*) AS MT_DB_Count
                       FROM public.t_mts_mt_db_jobs_cached
                       GROUP BY job ) AS MTSMT
       ON AJ.job = MTSMT.job
     LEFT OUTER JOIN ( SELECT job,
                              COUNT(*) AS PT_DB_Count
                       FROM public.t_mts_pt_db_jobs_cached
                       GROUP BY job ) AS MTSPT
       ON AJ.job = MTSPT.job
     LEFT OUTER JOIN ( SELECT dms_job,
                              COUNT(*) AS PMTasks
    FROM public.t_mts_peak_matching_tasks_cached AS PM
                       GROUP BY dms_job ) AS PMTaskCountQ
       ON PMTaskCountQ.dms_job = AJ.job
     LEFT OUTER JOIN public.t_dataset_archive AS DA
       ON DS.dataset_id = DA.dataset_id
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
       AJPG.group_name AS associated_processor_group
    FROM public.t_analysis_job_processor_group AJPG
     INNER JOIN public.t_analysis_job_processor_group_associations AJPGA
       ON AJPG.ID = AJPGA.group_id
     RIGHT OUTER JOIN public.t_analysis_job AJ
                      INNER JOIN public.t_dataset DS
                        ON AJ.dataset_id = DS.dataset_id
                      INNER JOIN public.t_organisms Org
                        ON AJ.organism_id = Org.organism_id
                      INNER JOIN public.t_analysis_tool AnalysisTool
                        ON AJ.analysis_tool_id = AnalysisTool.analysis_tool_id
                      INNER JOIN public.t_analysis_job_state ASN
                        ON AJ.job_state_id = ASN.job_state_id
       ON AJPGA.job = AJ.job
;

SELECT * FROM "public"."v_dataset_disposition";
SELECT * FROM "public"."v_dataset_disposition_lite";
SELECT * FROM "public"."v_dataset_list_report_2";
SELECT * FROM "public"."v_dataset_load";
SELECT * FROM "public"."v_analysis_job";
SELECT * FROM "public"."v_analysis_job_detail_report_2";
SELECT * FROM "public"."v_analysis_job_entry";
