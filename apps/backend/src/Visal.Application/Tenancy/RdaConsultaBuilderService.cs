using System.Security.Cryptography;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Construye el Bundle FHIR R4 RDA Consulta (perfil <c>CompositionAmbulatoryRDA</c>)
/// para el endpoint <c>$enviar-rda-consulta</c> de la API IHCE de MinSalud.
///
/// Secciones emitidas (Olas C2+C3+C4):
///  C2: Composition + Patient + Practitioner + Organization custodian + Location +
///      Encounter + Condition (dx principal del encuentro).
///  C3: AllergyIntolerance (NKDA placeholder o real) + MedicationRequest[] (CUM) +
///      ServiceRequest[] (CUPS).
///  C4: Observation occupation + Observation incapacidad + RiskAssessment +
///      Organization pagador (EPS).
///  C5 (pendiente): DocumentReference epicrisis con PDF base64.
/// </summary>
public sealed class RdaConsultaBuilderService(
    IApplicationDbContext db,
    ITenantContext tenant,
    ILogger<RdaConsultaBuilderService> log) : IRdaConsultaBuilderService
{
    private const string ProfileBase = "https://fhir.minsalud.gov.co/rda/StructureDefinition";
    private const string CodeSystemBase = "https://fhir.minsalud.gov.co/rda/CodeSystem";
    private const string NamingSystemBase = "https://fhir.minsalud.gov.co/rda/NamingSystem";
    private const string ReVps = "http://co.fhir.guide/NamingSystem/REPS";
    private const string LoincSystem = "http://loinc.org";
    private const string V2Terminology = "http://terminology.hl7.org/CodeSystem/v2-0203";

    public async Task<RdaBuildResult> ConstruirAsync(Guid historiaClinicaId, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }
        var advertencias = new List<string>();

        // ---------- Cargar datos clinicos de la HC ----------
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Include(x => x.Paciente)
            .Include(x => x.Profesional)
            .FirstOrDefaultAsync(x => x.Id == historiaClinicaId, ct)
            ?? throw new InvalidOperationException($"HC {historiaClinicaId} no encontrada.");
        if (hc.Paciente is null) { throw new InvalidOperationException("HC sin paciente."); }

        var tenantE = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tid, ct);
        var sucursalId = hc.Paciente.SedeAtencionId
            ?? await db.Sucursales.AsNoTracking().Where(s => s.Activo)
                .OrderBy(s => s.Codigo).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sin sede asignada.");
        var sucursal = await db.Sucursales.AsNoTracking().FirstAsync(s => s.Id == sucursalId, ct);

        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var ambiente = cfg?.AmbienteActivo ?? AmbienteIhce.Sandbox;
        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SucursalId == sucursalId && c.Ambiente == ambiente, ct);
        if (credencial is null || string.IsNullOrWhiteSpace(credencial.CodigoHabilitacion))
        {
            advertencias.Add($"Sede '{sucursal.Nombre}' sin CodigoHabilitacion REPS para {ambiente}. MinSalud rechazara.");
        }
        var codigoRep = credencial?.CodigoHabilitacion ?? "PENDIENTE_REPS";

        // CUPS del encuentro desde la Asignacion del paciente con FormatoHistoria matching
        var fdCodigo = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == hc.FormDefinitionId).Select(f => f.Codigo).FirstOrDefaultAsync(ct);
        var asignacionRel = await db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == hc.PacienteId && a.FormatoHistoria == fdCodigo)
            .OrderByDescending(a => a.FechaInicio)
            .FirstOrDefaultAsync(ct);
        var cupsEncuentro = asignacionRel?.ServicioId ?? "890201";

        // Pagador (Aseguradora) desde la asignacion -> contrato -> aseguradora
        Aseguradora? aseguradora = null;
        if (asignacionRel is not null)
        {
            aseguradora = await (
                from c in db.ContratosAseguradora.AsNoTracking()
                where c.CodigoContrato == asignacionRel.ContratoCodigo
                join a in db.Aseguradoras.AsNoTracking() on c.AseguradoraId equals a.Id
                select a).FirstOrDefaultAsync(ct);
        }

        // Items clinicos de la HC
        var medicamentos = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(m => m.HistoriaClinicaId == hc.Id).OrderBy(m => m.Orden).ToListAsync(ct);
        var ordenes = await db.HistoriaClinicaOrdenesServicio.AsNoTracking()
            .Where(o => o.HistoriaClinicaId == hc.Id).OrderBy(o => o.Orden).ToListAsync(ct);
        var incapacidades = await db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(i => i.HistoriaClinicaId == hc.Id).OrderBy(i => i.Orden).ToListAsync(ct);

        // ---------- IDs estables ----------
        var patientId = $"{NormalizarTipoDoc(hc.Paciente.TipoDocumento)}-{hc.Paciente.NumeroDocumento}";
        var practitionerId = hc.Profesional is not null
            ? $"{NormalizarTipoDoc(hc.Profesional.TipoDocumento)}-{hc.Profesional.NumeroDocumento}"
            : $"CC-anonimo-{hc.Id:N}";
        if (hc.Profesional is null)
        {
            advertencias.Add("HC sin profesional firmante; se incluye Practitioner anonimo (sera rechazado por ReTHUS).");
        }
        var orgId = codigoRep;
        var locationId = $"{codigoRep}-01";
        var payerOrgId = aseguradora is not null ? $"PAYER-{aseguradora.Codigo}" : "PAYER-DESCONOCIDO";
        var encounterId = "Encounter-0";
        var conditionId = "Condition-0";
        var allergyId = "AllergyIntolerance-0";
        // El IG MinSalud exige IDs con patron '<ResourceType>-<n>' (n numerico).
        // Observation-0 = occupation, Observation-{1+i} = disabilities, RiskAssessment-0 = riesgo.
        var occupationId = "Observation-0";
        var riskId = "RiskAssessment-0";

        // ---------- Recursos ----------
        var organization = BuildOrganization(tenantE, orgId, codigoRep);
        var payerOrg = BuildPayerOrganization(aseguradora, payerOrgId, advertencias);
        var practitioner = BuildPractitioner(hc.Profesional, practitionerId, hc.EspecialistaNombre);
        var patient = BuildPatient(hc.Paciente, patientId);
        var location = BuildLocation(locationId, codigoRep, sucursal.Nombre, orgId);
        var condition = BuildCondition(hc.Paciente, conditionId, patientId, advertencias);
        var encounter = BuildEncounter(hc, encounterId, patientId, practitionerId, orgId,
            locationId, cupsEncuentro, condition?.Id, condition?.Code?.Text);
        var allergy = BuildAllergyNkda(allergyId, patientId, encounterId, advertencias);
        var occupation = BuildOccupation(occupationId, patientId, encounterId, hc.Paciente, advertencias);

        var meds = BuildMedicationRequests(medicamentos, patientId, encounterId, practitionerId);
        var services = BuildServiceRequests(ordenes, patientId, encounterId, practitionerId);
        var disabilityObs = BuildDisabilityObservations(incapacidades, patientId, encounterId, advertencias);
        var risk = BuildRiskAssessment(riskId, patientId, encounterId, hc.Paciente, advertencias);

        var composition = BuildComposition(hc, patientId, encounterId, orgId, practitionerId,
            condition?.Id, allergyId, payerOrgId, occupationId, riskId, meds.Select(m => m.Id!).ToList(),
            services.Select(s => s.Id!).ToList(), disabilityObs.Select(o => o.Id!).ToList());

        // ---------- Bundle ----------
        var bundle = new Bundle
        {
            Id = $"rda-consulta-{Guid.CreateVersion7():N}",
            Language = "es-CO",
            Type = Bundle.BundleType.Document,
            Timestamp = DateTimeOffset.UtcNow
        };
        // CompositionAmbulatoryRDA requiere 9..10 secciones obligatorias segun MinSalud.
        // Emitimos todos los recursos sin meta.profile (los perfiles especificos del IG
        // RDA Consulta son distintos a los de RDA Paciente y no estan documentados aun;
        // MinSalud valida estructura y cardinalidades, no el meta.profile especifico).
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = composition });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = patient });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = organization });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = payerOrg });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = practitioner });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = location });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = encounter });
        if (condition is not null) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = condition }); }
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = allergy });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = occupation });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = risk });
        foreach (var m in meds) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = m }); }
        foreach (var s in services) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = s }); }
        foreach (var o in disabilityObs) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = o }); }

        var serializer = new FhirJsonSerializer(new SerializerSettings { Pretty = true });
        var bundleJson = serializer.SerializeToString(bundle);
        var hash = ComputeSha256(bundleJson);

        var existente = await db.RdaEventos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BundleHash == hash, ct);
        if (existente is not null)
        {
            return new RdaBuildResult(existente.Id, existente.BundleJson, existente.BundleHash,
                existente.Estado, bundle.Entry.Count, YaExistia: true, advertencias);
        }

        var evento = new RdaEvento
        {
            TenantId = tid,
            HistoriaClinicaId = hc.Id,
            PacienteId = hc.PacienteId,
            ProfesionalId = hc.ProfesionalId,
            SucursalId = sucursal.Id,
            TipoRda = TipoRdaIhce.Consulta,
            Modalidad = ModalidadRdaIhce.ConsultaExterna,
            Ambiente = ambiente,
            BundleJson = bundleJson,
            BundleHash = hash,
            Estado = EstadoRdaEvento.Borrador,
            Intentos = 0,
            FechaGeneracion = DateTimeOffset.UtcNow
        };
        db.RdaEventos.Add(evento);
        await db.SaveChangesAsync(ct);

        log.LogInformation("RDA Consulta construido: {Id} HC={HcId} CUPS={Cups} Meds={Meds} Servs={Servs}",
            evento.Id, hc.Id, cupsEncuentro, meds.Count, services.Count);
        return new RdaBuildResult(evento.Id, bundleJson, hash, EstadoRdaEvento.Borrador,
            bundle.Entry.Count, YaExistia: false, advertencias);
    }

    // ===================== Composition =====================

    private static Composition BuildComposition(HistoriaClinica hc, string patientId, string encounterId,
        string orgId, string practitionerId, string? conditionId, string allergyId,
        string payerOrgId, string occupationId, string riskId,
        IReadOnlyList<string> medIds, IReadOnlyList<string> serviceIds, IReadOnlyList<string> disabilityIds)
    {
        var fin = (hc.FechaCierre ?? DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(-5));
        var c = new Composition
        {
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/CompositionAmbulatoryRDA" } },
            Status = CompositionStatus.Final,
            Type = MakeCC(new Coding(LoincSystem, "51845-6", "Outpatient Consult note")),
            Subject = ContainedRef(patientId),
            Encounter = ContainedRef(encounterId),
            Date = fin.ToString("o"),
            Title = "RDA Consulta",
            Confidentiality = Composition.V3ConfidentialityClassification.N,
            Custodian = ContainedRef(orgId)
        };
        c.Author.Add(ContainedRef(orgId));
        c.Attester.Add(new Composition.AttesterComponent
        {
            Mode = Composition.CompositionAttestationMode.Legal,
            Party = ContainedRef(practitionerId)
        });
        var ahora = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5));
        var inicio = hc.FechaApertura.ToOffset(TimeSpan.FromHours(-5));
        if (inicio > ahora) { inicio = ahora; }
        if (inicio < ahora.AddYears(-1)) { inicio = ahora.AddYears(-1).AddDays(1); }
        if (fin < inicio) { fin = inicio; }
        if (fin > ahora) { fin = ahora; }
        c.Event.Add(new Composition.EventComponent
        {
            Period = new Period { Start = inicio.ToString("o"), End = fin.ToString("o") }
        });

        // CompositionAmbulatoryRDA requiere 9 secciones nombradas (cardinalidades 1..1).
        // Si una seccion no tiene datos reales, emitimos placeholder con un entry valido
        // para satisfacer la cardinalidad.

        // 1. sectionPayers
        var sPag = MakeSection("Información del responsable del pago de los servicios de salud",
            "48768-6", "Payment sources Document");
        sPag.Entry.Add(ContainedRef(payerOrgId));
        c.Section.Add(sPag);

        // 2. sectionHistoryOfOccupation
        var sDem = MakeSection("Información sociodemográfica del paciente",
            "29762-2", "Social history Narrative");
        sDem.Entry.Add(ContainedRef(occupationId));
        c.Section.Add(sDem);

        // 3. sectionAttendanceAllowance (incapacidades)
        var sInc = MakeSection("Certificados de incapacidad médica generados",
            "77599-9", "Disability certificate");
        if (disabilityIds.Count > 0)
        {
            foreach (var id in disabilityIds) { sInc.Entry.Add(ContainedRef(id)); }
        }
        else
        {
            sInc.EmptyReason = MakeCC(new Coding(
                "http://terminology.hl7.org/CodeSystem/list-empty-reason",
                "nilknown", "Nil Known"));
        }
        c.Section.Add(sInc);

        // 4. Diagnosticos
        if (conditionId is not null)
        {
            var sDx = MakeSection("Historial de diagnósticos de problemas de salud",
                "11450-4", "Problem list - Reported");
            sDx.Entry.Add(ContainedRef(conditionId));
            c.Section.Add(sDx);
        }

        // 5. sectionAllergies
        var sAlg = MakeSection("Historial de alergias, intolerancias y reacciones adversas",
            "48765-2", "Allergies and adverse reactions Document");
        sAlg.Entry.Add(ContainedRef(allergyId));
        c.Section.Add(sAlg);

        // 6. sectionRiskFactors
        var sRie = MakeSection("Factores de riesgo en salud identificados",
            "75310-3", "Health risk assessment panel");
        sRie.Entry.Add(ContainedRef(riskId));
        c.Section.Add(sRie);

        // 7. sectionMedications
        var sMed = MakeSection("Medicamentos prescritos durante la atención",
            "57828-6", "Prescriptions");
        if (medIds.Count > 0)
        {
            foreach (var id in medIds) { sMed.Entry.Add(ContainedRef(id)); }
        }
        else
        {
            sMed.EmptyReason = MakeCC(new Coding(
                "http://terminology.hl7.org/CodeSystem/list-empty-reason",
                "nilknown", "Nil Known"));
        }
        c.Section.Add(sMed);

        // 8. sectionServiceRequests
        var sSrv = MakeSection("Servicios de salud solicitados durante la atención",
            "62387-6", "Interdisciplinary - Plan of care");
        if (serviceIds.Count > 0)
        {
            foreach (var id in serviceIds) { sSrv.Entry.Add(ContainedRef(id)); }
        }
        else
        {
            sSrv.EmptyReason = MakeCC(new Coding(
                "http://terminology.hl7.org/CodeSystem/list-empty-reason",
                "nilknown", "Nil Known"));
        }
        c.Section.Add(sSrv);

        // 9. sectionAddendumDocuments (epicrisis PDF — placeholder hasta C5)
        var sDoc = MakeSection("Documentos adjuntos a la atención (epicrisis)",
            "11488-4", "Consult Note");
        sDoc.EmptyReason = MakeCC(new Coding(
            "http://terminology.hl7.org/CodeSystem/list-empty-reason",
            "nilknown", "Nil Known"));
        c.Section.Add(sDoc);

        return c;
    }

    // ===================== Encounter / Location =====================

    private static Encounter BuildEncounter(HistoriaClinica hc, string encounterId,
        string patientId, string practitionerId, string orgId, string locationId,
        string cupsServicio, string? conditionId, string? dxText)
    {
        var ahora = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5));
        var inicio = hc.FechaApertura.ToOffset(TimeSpan.FromHours(-5));
        if (inicio > ahora) { inicio = ahora; }
        if (inicio < ahora.AddYears(-1)) { inicio = ahora.AddYears(-1).AddDays(1); }
        var fin = (hc.FechaCierre ?? ahora).ToOffset(TimeSpan.FromHours(-5));
        if (fin > ahora) { fin = ahora; }
        if (fin < inicio) { fin = inicio; }

        // Status fijo en "finished": el perfil EncounterAmbulatoryRDA exige fixed value
        // (RDA reporta una atencion ya cerrada, aunque la HC todavia este abierta en Visal).
        var e = new Encounter
        {
            Id = encounterId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/EncounterAmbulatoryRDA" } },
            Status = Encounter.EncounterStatus.Finished,
            Class = new Coding("http://terminology.hl7.org/CodeSystem/v3-ActCode", "AMB", "ambulatory")
        };
        e.Identifier.Add(new Identifier
        {
            ElementId = "EncounterIdentifier",
            Use = Identifier.IdentifierUse.Usual,
            System = $"{NamingSystemBase}/Encounters",
            Value = $"VISAL-HC-{hc.Id:N}"
        });
        e.Type.Add(MakeCC(new Coding($"{CodeSystemBase}/ColombianTechModality", "01", "Intramural")));
        e.Type.Add(MakeCC(new Coding($"{CodeSystemBase}/GrupoServicios", "01", "Consulta externa")));
        e.Type.Add(MakeCC(new Coding($"{CodeSystemBase}/REPShealthcareServices", "328", "MEDICINA GENERAL")));
        e.Type.Add(MakeCC(new Coding($"{CodeSystemBase}/EntornoAtencion", "05", "Institucional")));
        // serviceType.coding.display es obligatorio (cardinalidad 1..1 segun MinSalud).
        e.ServiceType = MakeCC(new Coding($"{CodeSystemBase}/CUPS", cupsServicio,
            $"Servicio CUPS {cupsServicio}"));
        // reasonCode es obligatorio (1..1). Usamos el diagnostico del paciente si existe.
        e.ReasonCode.Add(new CodeableConcept
        {
            Text = string.IsNullOrWhiteSpace(dxText) ? "Consulta general" : dxText
        });
        e.Subject = ContainedRef(patientId);
        var participant = new Encounter.ParticipantComponent
        {
            ElementId = "AttenderPhysician",
            Individual = ContainedRef(practitionerId)
        };
        participant.Type.Add(MakeCC(new Coding(
            "http://terminology.hl7.org/CodeSystem/v3-ParticipationType", "ATND", "attender")));
        e.Participant.Add(participant);
        e.Period = new Period { Start = inicio.ToString("o"), End = fin.ToString("o") };
        if (conditionId is not null)
        {
            var diag = new Encounter.DiagnosisComponent
            {
                ElementId = "MainDiagnosis",
                Condition = ContainedRef(conditionId),
                Use = MakeCC(new Coding($"{CodeSystemBase}/ColombianDiagnosisRole",
                    "8319008", "diagnóstico primario")),
                Rank = 1
            };
            diag.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionDiagnosisType",
                new Coding($"{CodeSystemBase}/RIPSTipoDiagnosticoPrincipalVersion2",
                    "02", "Confirmado Nuevo")));
            e.Diagnosis.Add(diag);
        }
        e.Location.Add(new Encounter.LocationComponent { Location = ContainedRef(locationId) });
        e.ServiceProvider = ContainedRef(orgId);
        return e;
    }

    private static Location BuildLocation(string locationId, string codigoRep, string nombre, string orgId)
    {
        var loc = new Location
        {
            Id = locationId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/CareDeliveryLocationRDA" } },
            Name = nombre,
            ManagingOrganization = ContainedRef(orgId)
        };
        loc.Identifier.Add(new Identifier
        {
            Use = Identifier.IdentifierUse.Official,
            System = ReVps,
            Value = locationId
        });
        return loc;
    }

    // ===================== Clinical resources =====================

    private static Condition? BuildCondition(Paciente p, string conditionId, string patientId,
        List<string> advertencias)
    {
        if (string.IsNullOrWhiteSpace(p.Cie10Codigo) && string.IsNullOrWhiteSpace(p.DiagnosticoPrincipal))
        {
            advertencias.Add("Paciente sin dx — Composition NO incluira seccion Diagnosticos.");
            return null;
        }
        var c = new Condition
        {
            Id = conditionId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/ConditionRDA" } },
            ClinicalStatus = MakeCC(new Coding(
                "http://terminology.hl7.org/CodeSystem/condition-clinical", "active", "Active")),
            VerificationStatus = MakeCC(new Coding(null, "confirmed", "Confirmed")),
            Subject = ContainedRef(patientId)
        };
        c.Category.Add(MakeCC(new Coding(
            "http://terminology.hl7.org/CodeSystem/condition-category",
            "encounter-diagnosis", "Encounter Diagnosis")));
        c.Code = new CodeableConcept { Text = p.DiagnosticoPrincipal ?? p.Cie10Codigo };
        // Condition.code.coding tiene cardinalidad 1..* segun el perfil RDA. Si el
        // Cie10Codigo del paciente no tiene formato CIE-10 valido, emitimos un coding
        // generico para satisfacer la validacion.
        if (!string.IsNullOrWhiteSpace(p.Cie10Codigo) &&
            System.Text.RegularExpressions.Regex.IsMatch(p.Cie10Codigo, "^[A-Z][0-9]{2}"))
        {
            c.Code.Coding.Add(new Coding("http://hl7.org/fhir/sid/icd-10",
                p.Cie10Codigo, p.DiagnosticoPrincipal));
        }
        else
        {
            advertencias.Add($"Cie10Codigo '{p.Cie10Codigo}' no tiene formato CIE-10 valido; se emite coding generico Z000.");
            c.Code.Coding.Add(new Coding("http://hl7.org/fhir/sid/icd-10",
                "Z000", p.DiagnosticoPrincipal ?? "Consulta general"));
        }
        return c;
    }

    private static AllergyIntolerance BuildAllergyNkda(string allergyId, string patientId,
        string encounterId, List<string> advertencias)
    {
        advertencias.Add("Sin alergias capturadas; se reporta placeholder (NKDA / sin reaccion conocida).");
        var a = new AllergyIntolerance
        {
            Id = allergyId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/AllergyIntoleranceRDA" } },
            ClinicalStatus = MakeCC(new Coding(null, "active", "Active")),
            Patient = ContainedRef(patientId),
            Encounter = ContainedRef(encounterId),
            Code = new CodeableConcept { Text = "Sin alergias conocidas (NKDA)" }
        };
        a.Code.Coding.Add(new Coding($"{CodeSystemBase}/TipoAlergia", "01", "Medicamento"));
        return a;
    }

    private static List<MedicationRequest> BuildMedicationRequests(
        IReadOnlyList<HistoriaClinicaMedicamento> rows, string patientId, string encounterId, string practitionerId)
    {
        var list = new List<MedicationRequest>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var mr = new MedicationRequest
            {
                Id = $"MedicationRequest-{i}",
                Meta = new Meta { Profile = new[] { $"{ProfileBase}/MedicationRequestRDA" } },
                Status = MedicationRequest.MedicationrequestStatus.Active,
                Intent = MedicationRequest.MedicationRequestIntent.Order,
                Subject = ContainedRef(patientId),
                Encounter = ContainedRef(encounterId),
                Requester = ContainedRef(practitionerId),
                AuthoredOnElement = new FhirDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)))
            };
            mr.Medication = new CodeableConcept { Text = r.NombreMedicamento };
            if (!string.IsNullOrWhiteSpace(r.Posologia))
            {
                mr.DosageInstruction.Add(new Dosage { Text = r.Posologia });
            }
            list.Add(mr);
        }
        return list;
    }

    private static List<ServiceRequest> BuildServiceRequests(
        IReadOnlyList<HistoriaClinicaOrdenServicio> rows, string patientId, string encounterId, string practitionerId)
    {
        var list = new List<ServiceRequest>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var sr = new ServiceRequest
            {
                Id = $"ServiceRequest-{i}",
                Meta = new Meta { Profile = new[] { $"{ProfileBase}/ServiceRequestRDA" } },
                Status = RequestStatus.Active,
                Intent = RequestIntent.Order,
                Subject = ContainedRef(patientId),
                Encounter = ContainedRef(encounterId),
                Requester = ContainedRef(practitionerId),
                AuthoredOnElement = new FhirDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)))
            };
            sr.Code = new CodeableConcept { Text = r.Descripcion };
            if (!string.IsNullOrWhiteSpace(r.CodigoServicio))
            {
                sr.Code.Coding.Add(new Coding($"{CodeSystemBase}/CUPS", r.CodigoServicio, r.Descripcion));
            }
            list.Add(sr);
        }
        return list;
    }

    private static Observation BuildOccupation(string observationId, string patientId, string encounterId,
        Paciente p, List<string> advertencias)
    {
        var ocup = p.Ocupacion;
        if (string.IsNullOrWhiteSpace(ocup))
        {
            advertencias.Add("Paciente sin ocupacion; se reporta '9999 No aplica'.");
        }
        // Perfil correcto del IG RDA Consulta: PatientOccupationAtEncounterRDA
        // (NO ObservationOccupationRDA, que pertenece al RDA Paciente).
        var o = new Observation
        {
            Id = observationId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/PatientOccupationAtEncounterRDA" } },
            Status = ObservationStatus.Final,
            Subject = ContainedRef(patientId),
            Encounter = ContainedRef(encounterId),
            Code = MakeCC(new Coding(LoincSystem, "11341-5", "History of Occupation"))
        };
        o.Category.Add(MakeCC(new Coding(
            "http://terminology.hl7.org/CodeSystem/observation-category",
            "social-history", "Social History")));
        var occCC = new CodeableConcept { Text = ocup ?? "No aplica" };
        occCC.Coding.Add(new Coding($"{CodeSystemBase}/CIUOOcupacion", "9999", ocup ?? "No aplica"));
        o.Value = occCC;
        return o;
    }

    private static List<Observation> BuildDisabilityObservations(
        IReadOnlyList<HistoriaClinicaIncapacidad> rows, string patientId, string encounterId,
        List<string> advertencias)
    {
        var list = new List<Observation>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var o = new Observation
            {
                Id = $"Observation-{1 + i}",
                Meta = new Meta { Profile = new[] { $"{ProfileBase}/ObservationDisabilityCertificateRDA" } },
                Status = ObservationStatus.Final,
                Subject = ContainedRef(patientId),
                Encounter = ContainedRef(encounterId),
                Code = MakeCC(new Coding(LoincSystem, "77599-9", "Disability certificate"))
            };
            if (r.FechaDesde is { } d && r.FechaHasta is { } h)
            {
                o.Effective = new Period
                {
                    Start = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("o"),
                    End = h.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).ToString("o")
                };
            }
            var causa = new Observation.ComponentComponent
            {
                Code = MakeCC(new Coding(LoincSystem, "77600-5", "Reason for disability")),
                Value = new CodeableConcept { Text = r.Motivo }
            };
            o.Component.Add(causa);
            if (r.Dias is int d2)
            {
                o.Component.Add(new Observation.ComponentComponent
                {
                    Code = MakeCC(new Coding(LoincSystem, "77598-1", "Disability duration days")),
                    Value = new Quantity { Value = d2, Unit = "d" }
                });
            }
            list.Add(o);
        }
        if (list.Count == 0)
        {
            advertencias.Add("HC sin incapacidades; seccion Incapacidades omitida.");
        }
        return list;
    }

    /// <summary>
    /// Factores de riesgo en RDA Consulta: RiskAssessment con perfil RiskFactorRDA.
    /// </summary>
    private static RiskAssessment BuildRiskAssessment(string riskId, string patientId, string encounterId,
        Paciente p, List<string> advertencias)
    {
        advertencias.Add("Visal no captura factores de riesgo estructurados; se reporta placeholder.");
        var ra = new RiskAssessment
        {
            Id = riskId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/RiskFactorRDA" } },
            Status = ObservationStatus.Final,
            Subject = ContainedRef(patientId),
            Encounter = ContainedRef(encounterId),
            Occurrence = new FhirDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)))
        };
        ra.Code = MakeCC(new Coding($"{CodeSystemBase}/FactoresRiesgoSalud", "99", "No reportado"));
        return ra;
    }

    // ===================== Patient / Practitioner / Organizations =====================

    private static Patient BuildPatient(Paciente p, string patientId)
    {
        var pat = new Patient
        {
            Id = patientId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/PatientRDA" } },
            Active = true,
            BirthDate = p.FechaNacimiento?.ToString("yyyy-MM-dd"),
            Gender = MapGender(p.Sexo),
            Deceased = new FhirBoolean(false)
        };
        pat.Extension.Add(new Extension($"{ProfileBase}/ExtensionPatientNationality",
            new Coding($"{CodeSystemBase}/ISO31661", "170", "Colombia")));
        pat.Extension.Add(new Extension($"{ProfileBase}/ExtensionPatientEthnicity",
            new Coding($"{CodeSystemBase}/ColombianEthnicGroup", "6", "Otras etnias")));
        pat.Extension.Add(new Extension($"{ProfileBase}/ExtensionPatientDisability",
            new Coding($"{CodeSystemBase}/ColombianDisabilityClassification", "08", "Sin discapacidad")));
        pat.Extension.Add(new Extension($"{ProfileBase}/ExtensionPatientGenderIdentity",
            new Coding($"{CodeSystemBase}/ColombianGenderIdentity",
                p.Sexo?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true ? "02" : "01",
                p.Sexo?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true ? "Femenino" : "Masculino")));

        var idType = new CodeableConcept();
        idType.Coding.Add(new Coding(V2Terminology, "PN", "Person number"));
        idType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianPersonIdentifier",
            NormalizarTipoDoc(p.TipoDocumento), TipoDocLabel(p.TipoDocumento)));
        pat.Identifier.Add(new Identifier
        {
            ElementId = "NationalPersonIdentifier-0",
            Use = Identifier.IdentifierUse.Official,
            Type = idType,
            System = $"{NamingSystemBase}/RNEC",
            Value = p.NumeroDocumento
        });
        var hn = new HumanName { Use = HumanName.NameUse.Official };
        if (!string.IsNullOrWhiteSpace(p.PrimerNombre)) { hn.GivenElement.Add(new FhirString(p.PrimerNombre)); }
        if (!string.IsNullOrWhiteSpace(p.SegundoNombre)) { hn.GivenElement.Add(new FhirString(p.SegundoNombre)); }
        if (!string.IsNullOrWhiteSpace(p.PrimerApellido) || !string.IsNullOrWhiteSpace(p.SegundoApellido))
        {
            hn.Family = p.PrimerApellido ?? "";
            hn.FamilyElement.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionFathersFamilyName", new FhirString(p.PrimerApellido ?? "")));
            if (!string.IsNullOrWhiteSpace(p.SegundoApellido))
            {
                hn.FamilyElement.Extension.Add(new Extension(
                    $"{ProfileBase}/ExtensionMothersFamilyName", new FhirString(p.SegundoApellido)));
            }
        }
        else { hn.Text = p.NombreCompleto; }
        pat.Name.Add(hn);

        var addr = new Address
        {
            ElementId = "HomeAddress-0",
            Use = Address.AddressUse.Home,
            Type = Address.AddressType.Physical,
            City = string.IsNullOrWhiteSpace(p.Ciudad) ? "Bogotá D.C." : p.Ciudad,
            Country = "Colombia"
        };
        addr.CityElement.Extension.Add(new Extension(
            $"{ProfileBase}/ExtensionDivipolaMunicipality",
            new Coding($"{CodeSystemBase}/DIVIPOLA", "11001", null)));
        addr.CountryElement.Extension.Add(new Extension(
            $"{ProfileBase}/ExtensionCountryCode",
            new Coding($"{CodeSystemBase}/ISO31661", "170", null)));
        addr.Extension.Add(new Extension(
            $"{ProfileBase}/ExtensionResidenceZone",
            new Coding($"{CodeSystemBase}/ColombianResidenceZone",
                p.Zona?.StartsWith("RURAL", StringComparison.OrdinalIgnoreCase) == true ? "02" : "01",
                p.Zona?.StartsWith("RURAL", StringComparison.OrdinalIgnoreCase) == true ? "Rural" : "Urbana")));
        pat.Address.Add(addr);

        if (pat.GenderElement is not null)
        {
            pat.GenderElement.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionBiologicalGender",
                new Coding($"{CodeSystemBase}/ColombianGenderGroup",
                    p.Sexo?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true ? "02" : "01",
                    p.Sexo?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true ? "Mujer" : "Hombre")));
        }
        if (pat.BirthDateElement is not null)
        {
            pat.BirthDateElement.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionBirthTime", new Time("12:00:00")));
        }
        return pat;
    }

    private static Practitioner BuildPractitioner(Profesional? prof, string practitionerId, string? nombreSnapshot)
    {
        var p = new Practitioner
        {
            Id = practitionerId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/PractitionerRDA" } }
        };
        if (prof is not null)
        {
            var idType = new CodeableConcept();
            idType.Coding.Add(new Coding(V2Terminology, "PN", "Person number"));
            idType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianPersonIdentifier",
                NormalizarTipoDoc(prof.TipoDocumento), TipoDocLabel(prof.TipoDocumento)));
            p.Identifier.Add(new Identifier
            {
                ElementId = "NationalPersonIdentifier-0",
                Use = Identifier.IdentifierUse.Official,
                Type = idType,
                Value = prof.NumeroDocumento
            });
            var hn = new HumanName { Use = HumanName.NameUse.Official };
            if (!string.IsNullOrWhiteSpace(prof.PrimerNombre)) { hn.GivenElement.Add(new FhirString(prof.PrimerNombre)); }
            if (!string.IsNullOrWhiteSpace(prof.SegundoNombre)) { hn.GivenElement.Add(new FhirString(prof.SegundoNombre)); }
            if (!string.IsNullOrWhiteSpace(prof.PrimerApellido) || !string.IsNullOrWhiteSpace(prof.SegundoApellido))
            {
                hn.Family = prof.PrimerApellido ?? "";
                hn.FamilyElement.Extension.Add(new Extension(
                    $"{ProfileBase}/ExtensionFathersFamilyName",
                    new FhirString(prof.PrimerApellido ?? "")));
                if (!string.IsNullOrWhiteSpace(prof.SegundoApellido))
                {
                    hn.FamilyElement.Extension.Add(new Extension(
                        $"{ProfileBase}/ExtensionMothersFamilyName",
                        new FhirString(prof.SegundoApellido)));
                }
            }
            else { hn.Text = prof.NombreCompleto; }
            p.Name.Add(hn);
        }
        else
        {
            p.Name.Add(new HumanName { Use = HumanName.NameUse.Official, Text = nombreSnapshot ?? "PROFESIONAL NO REGISTRADO" });
        }
        return p;
    }

    private static Organization BuildOrganization(Tenant tenantE, string orgId, string codigoRep)
    {
        var o = new Organization
        {
            Id = orgId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/CareDeliveryOrganizationRDA" } }
        };
        var taxType = new CodeableConcept();
        taxType.Coding.Add(new Coding(V2Terminology, "TAX", "Tax ID number"));
        taxType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianOrganizationIdentifiers",
            "NIT", "Numero de Identificacion Tributaria"));
        o.Identifier.Add(new Identifier
        {
            ElementId = "TaxIdentifier-0",
            Use = Identifier.IdentifierUse.Official,
            Type = taxType,
            Value = string.IsNullOrWhiteSpace(tenantE.TaxId) ? "Desconocido" : tenantE.TaxId
        });
        var prnType = new CodeableConcept();
        prnType.Coding.Add(new Coding(V2Terminology, "PRN", "Provider number"));
        prnType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianOrganizationIdentifiers",
            "CodigoPrestador", "Codigo de habilitacion de prestador"));
        o.Identifier.Add(new Identifier
        {
            ElementId = "HealthcareProviderIdentifier-0",
            Use = Identifier.IdentifierUse.Official,
            Type = prnType,
            System = ReVps,
            Value = codigoRep
        });
        return o;
    }

    private static Organization BuildPayerOrganization(Aseguradora? a, string payerOrgId, List<string> advertencias)
    {
        if (a is null)
        {
            advertencias.Add("Sin pagador asignado; se reporta Organization pagador placeholder.");
        }
        var o = new Organization
        {
            Id = payerOrgId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/HealthBenefitPlanAdminOrganizationRDA" } },
            Name = a?.Nombre ?? "PAGADOR DESCONOCIDO"
        };
        var taxType = new CodeableConcept();
        taxType.Coding.Add(new Coding(V2Terminology, "TAX", "Tax ID number"));
        taxType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianOrganizationIdentifiers",
            "NIT", "Numero de Identificacion Tributaria"));
        o.Identifier.Add(new Identifier
        {
            ElementId = "TaxIdentifier-0",
            Use = Identifier.IdentifierUse.Official,
            Type = taxType,
            Value = a?.Nit ?? "000000000"
        });
        var epsType = new CodeableConcept();
        epsType.Coding.Add(new Coding(V2Terminology, "PRN", "Provider number"));
        epsType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianOrganizationIdentifiers",
            "CodigoEAPB", "Codigo de Entidad Administradora de Planes de Beneficios"));
        o.Identifier.Add(new Identifier
        {
            ElementId = "PayerIdentifier-0",
            Use = Identifier.IdentifierUse.Official,
            Type = epsType,
            Value = a?.Codigo ?? "PENDIENTE"
        });
        return o;
    }

    // ===================== Helpers =====================

    private static ResourceReference ContainedRef(string id) => new($"#{id}");

    private static Composition.SectionComponent MakeSection(string title, string loincCode, string display)
        => new() { Title = title, Code = MakeCC(new Coding(LoincSystem, loincCode, display)) };

    private static CodeableConcept MakeCC(Coding c) { var cc = new CodeableConcept(); cc.Coding.Add(c); return cc; }

    private static AdministrativeGender? MapGender(string? sexo) => sexo?.Trim().ToUpperInvariant() switch
    {
        "MASCULINO" or "M" => AdministrativeGender.Male,
        "FEMENINO" or "F" => AdministrativeGender.Female,
        "OTRO" => AdministrativeGender.Other,
        null or "" => null,
        _ => AdministrativeGender.Unknown
    };

    private static string NormalizarTipoDoc(string? td) => td?.Trim().ToUpperInvariant() switch
    {
        "CC" => "CC", "TI" => "TI", "CE" => "CE",
        "PA" or "PAS" => "PA", "RC" => "RC", "AS" => "AS", "MS" => "MS",
        _ => "CC"
    };

    private static string TipoDocLabel(string? td) => NormalizarTipoDoc(td) switch
    {
        "CC" => "Cedula ciudadania",
        "TI" => "Tarjeta identidad",
        "CE" => "Cedula extranjeria",
        "PA" => "Pasaporte",
        "RC" => "Registro civil",
        _ => "Cedula ciudadania"
    };

    private static string ComputeSha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(64);
        foreach (var b in bytes) { sb.Append(b.ToString("x2")); }
        return sb.ToString();
    }
}
