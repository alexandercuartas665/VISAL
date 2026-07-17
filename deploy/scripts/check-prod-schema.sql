-- =====================================================================
-- CHECK DE ESQUEMA — Visal PROD vs commit fae7943 (2026-07-14)
-- Pegar este bloque completo en la Consola SQL de prod (/consola-sql).
-- Es 100% SELECT: no modifica nada. Devuelve 4 resultados en un array
-- unificado para poder verlos de un vistazo.
-- =====================================================================

WITH
esperadas_mig AS (
  SELECT unnest(ARRAY[
    '20260520022015_InitialCreate','20260520023630_AddPlatformUserPassword','20260520035244_AddTenantEvolutionConfig',
    '20260520035923_AddWhatsAppLine','20260520040424_AddPipelineAndLeads','20260520040832_AddFollowUpTask',
    '20260520041523_AddChat','20260520130831_AddWompiMasterConfig','20260520144345_AddTenantLogo',
    '20260520203216_AddWompiIntegritySecret','20260520205315_AddWompiWebhookEvent','20260520210217_AddDataProtectionKeys',
    '20260521020912_AddSubscriptionAutoRenew','20260521030920_AddPipelineFields','20260521122908_AddAdvisorInvites',
    '20260521175850_AddEvolutionMasterConfig','20260521191609_EvolutionServerChoice','20260521222235_AddMessageTemplatesAndMedia',
    '20260521225655_AddMessageSender','20260522002733_AddWebhookConfig','20260522024946_AddAiAgents',
    '20260522025600_AddAutomationRules','20260522030711_AddAutomationRuleFields','20260522095500_AddLeadArchive',
    '20260522105004_AddLeadNotes','20260522110636_AddLeadFiles','20260522122456_AddAiProviderConfigs',
    '20260522132505_AddAiUsageLogs','20260522191606_AddAiAgentPrompts','20260523041320_AddPlatformBranding',
    '20260523093717_AddEmailConfigAndPasswordReset','20260523120230_AddGoogleAuthConfig','20260524002107_AddPipelineFieldExtras',
    '20260524012716_AddTenantApiConfig','20260525121227_AddPipelineFieldMultiWithDetail','20260525122734_AddPipelineFieldTotalSourceKeys',
    '20260525194406_AddQuoteTemplates','20260525201535_AddTemplateAssets','20260527102516_AddQuoteTemplateSendAsImage',
    '20260527212708_AddFormDefinitions','20260528013707_AddAseguradoras','20260528020147_AddProfesionales',
    '20260528021538_AddRolesUsuariosSucursalesPacientes','20260528093125_AddTenantUserSucursales','20260528095043_AddPlatformUserDocumento',
    '20260528100351_ExpandPacienteVisalFields','20260528105035_AddCatalogoPaciente','20260528105933_AddGeografiaColombia',
    '20260528152216_MedContratadoToFK','20260528155548_AddCie11Config','20260528163034_AddAsignacionLotes',
    '20260529230940_ExpandUsuarioYPermisosCoordinacion','20260530022956_AddAsignacionTurno','20260530025238_LinkTenantUserToProfesional',
    '20260530025951_AddAsignacionTurnoSesion','20260530104259_AddPrefillRoutesToFormDefinition','20260530150342_AddHistoriaClinica',
    '20260531004356_AddCodigoSecundarioToFormDefinition','20260531191900_AddMedicamentos','20260601205310_MedicamentosColumnsToText',
    '20260601233829_AddHistoriaClinicaMedicamento','20260601235644_AddNotaMedica','20260602013938_HistoriaClinicaOrdenesServicio',
    '20260602015459_HistoriaClinicaIncapacidadesCertificaciones','20260602170609_AddCups','20260602193857_HistoriaClinicaRemisiones',
    '20260603014656_AutomationRuleAiAgent','20260603094429_AutomationRuleAutoReviewFlags','20260603103450_NotaMedicaFirmaPaciente',
    '20260603111708_AsistenteChatMensaje','20260603172009_RelacionFormulario','20260603192540_HistoriaClinicaEscala',
    '20260603211411_RelacionFormularioTipo','20260603223753_HistoriaClinicaDocumento','20260604141551_AsignacionTurnoTarifa',
    '20260605091005_TenantSlogan','20260605113133_FirmaPacienteRequest','20260605120552_NotaMedicaDocumentoPacienteId',
    '20260605132151_TipologiaArchivo','20260612221334_PacienteCodigoPaisYContactosV2','20260618205447_InteroperabilidadConfigYCredencialesSede',
    '20260618214948_RdaEvento','20260619000320_InteroperabilidadPathsIhce','20260619015409_InteroperabilidadPathConsultarProfesional',
    '20260619104129_RdaConsultaTipoRdaYPathEnvio','20260623020759_AddHcInsumos','20260623104337_AddHcMedicamentoCodigo',
    '20260623133035_AddSqlConsoleLog','20260626134310_AddFormDefinitionSnapshots','20260630214923_FirmaPacRequestNotaNullable',
    '20260630223236_PacienteAmpliarVarcharFields','20260701093836_CatalogoServicioReferencia','20260701102233_HistoriaClinicaOrdenExterna',
    '20260701220218_InsumoMipresUrl','20260702133407_RemisionCantidadYDropCups','20260702142442_PacienteContactoFirmaUrl',
    '20260702162704_AddPaquetes','20260702173221_ContratoRequierePdf','20260702174341_AddCuotaCopagoYPagoAsignacion',
    '20260702200441_AddRipsHcYCatalogos','20260703124128_AddContactoEmergenciaIdAFirmaRequest','20260703142627_AllowNullNotaMedicaIdInDocumento',
    '20260703160830_ProfesionalFirmaUrlAsText','20260703192953_GupshupProvider','20260704124438_TenantWhatsAppTemplateBindingV1',
    '20260706105113_ProfesionalRolPredeterminado','20260706155321_AddDiagnosticos','20260706234424_AddPacienteEstadoAdmision',
    '20260707100222_AddMipresObligatorioAndMedicamentoMipresUrl','20260710020238_AddDocumentoHistoriaClinicaId','20260710021207_AddHcMenuConfig',
    '20260710110747_SeedEscalaRelacionesFromExistingHc','20260710143016_AddCatalogoTiposServicio','20260710144949_AddHcPestanaAlias',
    '20260710155257_AddHcPestanaOrden','20260710163220_AddAtencionColumnaConfig','20260714231918_AddTurnosProgramaciones'
  ]) AS migration_id
),
esperadas_tabla AS (
  SELECT unnest(ARRAY[
    'atencion_columna_configs','catalogos_tipo_servicio','cuotas_copagos','diagnosticos',
    'hc_menu_configs','hc_pestana_aliases','historia_clinica_insumos','historia_clinica_ordenes_externas',
    'interoperabilidad_configs','interoperabilidad_credenciales_sede','nota_medica_documentos','paciente_contactos_emergencia',
    'paquetes','rda_eventos','relaciones_formulario','sql_console_logs','tenant_gupshup_configs',
    'tenant_user_tipos_coordinados','tenant_whats_app_template_bindings','tipologia_archivos','tipos_turno',
    'turno_programaciones','form_definition_snapshots','catalogos_servicio_referencia'
  ]) AS table_name
),
esperadas_col AS (
  SELECT * FROM (VALUES
    ('sucursales','mipres_obligatorio'),
    ('pacientes','estado_admision'),
    ('pacientes','fecha_cierre_admision'),
    ('historias_clinicas','fecha_cierre'),
    ('historia_clinica_medicamentos','mipres_url'),
    ('historia_clinica_medicamentos','codigo_medicamento'),
    ('historia_clinica_insumos','mipres_url'),
    ('historia_clinica_documentos','historia_clinica_id'),
    ('form_definitions','codigo_secundario'),
    ('form_definitions','prefill_routes_json'),
    ('contratos_aseguradora','requiere_pdf_autorizacion'),
    ('profesionales','rol_predeterminado_id'),
    ('interoperabilidad_configs','path_envio_rda'),
    ('interoperabilidad_configs','path_envio_rda_consulta'),
    ('rda_eventos','tipo_rda'),
    ('whats_app_lines','provider'),
    ('whats_app_lines','inbound_token'),
    ('tenants','slogan')
  ) AS t(table_name, column_name)
)

-- ===== 1. Migraciones aplicadas / faltantes =====
SELECT
  '1_MIGRACIONES' AS check_type,
  (SELECT COUNT(*) FROM "__EFMigrationsHistory")::text AS aplicadas,
  (SELECT COUNT(*) FROM esperadas_mig)::text AS esperadas,
  (SELECT string_agg(migration_id, E'\n' ORDER BY migration_id)
   FROM esperadas_mig
   WHERE migration_id NOT IN (SELECT migration_id FROM "__EFMigrationsHistory")) AS faltantes

UNION ALL

-- ===== 2. Tablas criticas ausentes =====
SELECT
  '2_TABLAS_CRITICAS' AS check_type,
  (SELECT COUNT(*) FROM information_schema.tables
   WHERE table_schema='public' AND table_type='BASE TABLE')::text AS aplicadas,
  (SELECT COUNT(*) FROM esperadas_tabla)::text AS esperadas,
  (SELECT string_agg(table_name, E'\n' ORDER BY table_name)
   FROM esperadas_tabla
   WHERE table_name NOT IN (
     SELECT table_name FROM information_schema.tables
     WHERE table_schema='public' AND table_type='BASE TABLE'
   )) AS faltantes

UNION ALL

-- ===== 3. Columnas criticas ausentes =====
SELECT
  '3_COLUMNAS_CRITICAS' AS check_type,
  '' AS aplicadas,
  (SELECT COUNT(*) FROM esperadas_col)::text AS esperadas,
  (SELECT string_agg(table_name || '.' || column_name, E'\n' ORDER BY table_name)
   FROM esperadas_col e
   WHERE NOT EXISTS (
     SELECT 1 FROM information_schema.columns c
     WHERE c.table_schema='public'
       AND c.table_name = e.table_name
       AND c.column_name = e.column_name
   )) AS faltantes

UNION ALL

-- ===== 4. Ultima migracion aplicada =====
SELECT
  '4_ULTIMA_APLICADA' AS check_type,
  (SELECT MAX(migration_id) FROM "__EFMigrationsHistory") AS aplicadas,
  '20260714231918_AddTurnosProgramaciones' AS esperadas,
  CASE WHEN (SELECT MAX(migration_id) FROM "__EFMigrationsHistory")
            = '20260714231918_AddTurnosProgramaciones'
       THEN 'OK: al dia con el commit fae7943'
       ELSE 'ATRASADA: faltan migraciones (ver check 1)'
  END AS faltantes;
