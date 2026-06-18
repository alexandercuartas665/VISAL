using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using TaskFhir = Hl7.Fhir.Model.Task;

namespace Visal.Application.Tenancy;

/// <summary>
/// Construye el Bundle FHIR R4 RDA desde una HistoriaClinica usando el SDK oficial
/// Firely .NET (Hl7.Fhir.R4). Esta Ola arma los recursos demograficos / de contexto:
///
/// - Bundle (type=document, identifier oid+uuid)
/// - Composition (LOINC 34133-9, secciones vacias con titulo + LOINC)
/// - Patient (demograficos completos, telecom, address)
/// - Encounter (la HC como encuentro, status y periodo)
/// - Practitioner (el profesional firmante)
/// - Organization (el tenant + sucursal con CodigoHabilitacion REPS)
///
/// El JSON se persiste tal cual lo serializa Firely (canonico FHIR). El SHA-256 sobre
/// ese mismo JSON es el bundle_hash que garantiza idempotencia.
/// </summary>
public sealed class RdaBuilderService(
    IApplicationDbContext db,
    ITenantContext tenant,
    ILogger<RdaBuilderService> log) : IRdaBuilderService
{
    private const string RdaProfileBase = "https://fhir.minsalud.gov.co/rda/StructureDefinition";
    private const string RdaCodeSystemBase = "https://fhir.minsalud.gov.co/rda/CodeSystem";
    private const string RdaSidBase = "https://fhir.minsalud.gov.co/rda/sid";
    private const string LoincSystem = "http://loinc.org";

    public async Task<RdaBuildResult> ConstruirAsync(Guid historiaClinicaId, ModalidadRdaIhce modalidad, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("No hay tenant activo para construir el RDA.");
        }
        var advertencias = new List<string>();

        // ---------- 1. Cargar fuente de datos ----------
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Include(x => x.Paciente)
            .Include(x => x.Profesional)
            .FirstOrDefaultAsync(x => x.Id == historiaClinicaId, ct)
            ?? throw new InvalidOperationException($"Historia clinica {historiaClinicaId} no encontrada.");

        if (hc.Paciente is null)
        {
            throw new InvalidOperationException($"HC {historiaClinicaId} sin paciente.");
        }

        var tenantE = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tid, ct);

        // Sede del paciente con fallback a la primera sucursal del tenant.
        var sucursalId = hc.Paciente.SedeAtencionId
            ?? await db.Sucursales.AsNoTracking().Where(s => s.Activo)
                .OrderBy(s => s.Codigo).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (sucursalId is null)
        {
            throw new InvalidOperationException("El paciente no tiene sede asignada y no hay sucursales activas para asignar por defecto.");
        }
        var sucursal = await db.Sucursales.AsNoTracking().FirstAsync(s => s.Id == sucursalId.Value, ct);

        // Resolver ambiente activo + credencial de esa sede (para sacar CodigoHabilitacion).
        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var ambiente = cfg?.AmbienteActivo ?? AmbienteIhce.Sandbox;
        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SucursalId == sucursalId.Value && c.Ambiente == ambiente, ct);
        if (credencial is null || string.IsNullOrWhiteSpace(credencial.CodigoHabilitacion))
        {
            advertencias.Add($"La sede '{sucursal.Nombre}' no tiene CodigoHabilitacion REPS configurado para el ambiente {ambiente}. El Bundle se construye con un placeholder y NO sera aceptado por MinSalud.");
        }

        // Cargar contenido clinico atado a la HC (Ola 3).
        var hcMedicamentos = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hc.Id).OrderBy(x => x.Orden).ToListAsync(ct);
        var hcRemisiones = await db.HistoriaClinicaRemisiones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hc.Id).OrderBy(x => x.Orden).ToListAsync(ct);

        // ---------- 2. Construir recursos FHIR ----------
        var nowOffset = hc.FechaCierre ?? hc.FechaApertura;
        var nowFhir = new FhirDateTime(nowOffset.ToOffset(TimeSpan.FromHours(-5)));

        var organization = BuildOrganization(tenantE, sucursal, credencial?.CodigoHabilitacion);
        var practitioner = BuildPractitioner(hc.Profesional, hc.EspecialistaNombre, advertencias);
        var patient = BuildPatient(hc.Paciente);
        var encounter = BuildEncounter(hc, modalidad, patient, practitioner, organization);

        // Recursos clinicos (Ola 3). Listas vacias si la HC no tiene datos.
        var conditions = BuildConditions(hc.Paciente, patient, encounter, practitioner, advertencias);
        var medicationStatements = BuildMedicationStatements(hcMedicamentos, patient, encounter, practitioner);
        var procedures = BuildProcedures(hcRemisiones, patient, encounter, practitioner, organization, conditions);
        var allergyIntolerance = BuildAllergyIntoleranceNkda(hc.Paciente, patient, practitioner, advertencias);

        var composition = BuildComposition(hc, modalidad, patient, encounter, practitioner, organization, nowFhir,
            conditions, medicationStatements, procedures, allergyIntolerance);

        // ---------- 3. Ensamblar Bundle ----------
        var bundle = new Bundle
        {
            Id = $"rda-{Guid.CreateVersion7():N}",
            Meta = new Meta
            {
                Profile = new[] { $"{RdaProfileBase}/BundleRDA" },
                LastUpdated = DateTimeOffset.UtcNow
            },
            Identifier = new Identifier("urn:oid:2.16.170.1.100.10.1", $"VISAL-RDA-{Guid.CreateVersion7():N}"),
            Type = Bundle.BundleType.Document,
            Timestamp = DateTimeOffset.UtcNow
        };
        // Orden Composition → Patient → Encounter → Practitioner → Organization → clinicos.
        bundle.Entry.Add(MakeEntry(composition));
        bundle.Entry.Add(MakeEntry(patient));
        bundle.Entry.Add(MakeEntry(encounter));
        bundle.Entry.Add(MakeEntry(practitioner));
        bundle.Entry.Add(MakeEntry(organization));
        foreach (var c in conditions) { bundle.Entry.Add(MakeEntry(c)); }
        foreach (var m in medicationStatements) { bundle.Entry.Add(MakeEntry(m)); }
        foreach (var p in procedures) { bundle.Entry.Add(MakeEntry(p)); }
        bundle.Entry.Add(MakeEntry(allergyIntolerance));

        // ---------- 4. Serializar canonico FHIR + hash ----------
        var serializer = new FhirJsonSerializer(new SerializerSettings { Pretty = true });
        var bundleJson = serializer.SerializeToString(bundle);
        var hash = ComputeSha256(bundleJson);

        // ---------- 5. Idempotencia: si ya existe el mismo hash, devolver el existente ----------
        var existente = await db.RdaEventos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BundleHash == hash, ct);
        if (existente is not null)
        {
            log.LogInformation("RDA idempotente: bundle hash {Hash} ya existia como evento {Id}", hash, existente.Id);
            return new RdaBuildResult(existente.Id, existente.BundleJson, existente.BundleHash,
                existente.Estado, bundle.Entry.Count, YaExistia: true, advertencias);
        }

        // ---------- 6. Persistir RdaEvento ----------
        var evento = new RdaEvento
        {
            TenantId = tid,
            HistoriaClinicaId = hc.Id,
            PacienteId = hc.PacienteId,
            ProfesionalId = hc.ProfesionalId,
            SucursalId = sucursal.Id,
            Modalidad = modalidad,
            Ambiente = ambiente,
            BundleJson = bundleJson,
            BundleHash = hash,
            Estado = EstadoRdaEvento.Borrador,
            Intentos = 0,
            FechaGeneracion = DateTimeOffset.UtcNow
        };
        db.RdaEventos.Add(evento);
        await db.SaveChangesAsync(ct);

        log.LogInformation("RDA construido: evento {Id} para HC {HcId} modalidad {Mod} ambiente {Amb}",
            evento.Id, hc.Id, modalidad, ambiente);

        return new RdaBuildResult(evento.Id, bundleJson, hash, EstadoRdaEvento.Borrador,
            bundle.Entry.Count, YaExistia: false, advertencias);
    }

    // ===================== Construccion de recursos =====================

    private static Organization BuildOrganization(Tenant tenant, Sucursal sucursal, string? codigoHabilitacion)
    {
        var nit = string.IsNullOrWhiteSpace(tenant.TaxId) ? "PENDIENTE_NIT" : tenant.TaxId;
        var habilitacion = string.IsNullOrWhiteSpace(codigoHabilitacion) ? "PENDIENTE_REPS" : codigoHabilitacion;
        var org = new Organization
        {
            Id = $"org-{tenant.Id:N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/OrganizationRDA" } },
            Active = true,
            Name = string.IsNullOrWhiteSpace(tenant.LegalName) ? tenant.Name : tenant.LegalName
        };
        org.Identifier.Add(new Identifier($"{RdaSidBase}/nit", nit)
        {
            Use = Identifier.IdentifierUse.Official,
            Type = MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSTipoDocumentoIdentidad", "NI", "NIT"))
        });
        org.Identifier.Add(new Identifier($"{RdaSidBase}/codigo-habilitacion", habilitacion)
        {
            Use = Identifier.IdentifierUse.Secondary,
            Type = new CodeableConcept { Text = "Codigo Habilitacion REPS" }
        });
        org.Type.Add(MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSTipoPrestador", "IPS",
            "Institucion Prestadora de Servicios de Salud")));
        if (!string.IsNullOrWhiteSpace(sucursal.Ciudad))
        {
            org.Address.Add(new Address
            {
                Use = Address.AddressUse.Work,
                Type = Address.AddressType.Physical,
                City = sucursal.Ciudad,
                Country = "CO"
            });
        }
        return org;
    }

    private static Practitioner BuildPractitioner(Profesional? prof, string? especialistaNombreSnapshot, List<string> advertencias)
    {
        if (prof is null)
        {
            advertencias.Add("La HC no tiene profesional firmante; se incluye un Practitioner con nombre snapshot pero sin documento ni registro medico.");
            var anon = new Practitioner
            {
                Id = $"prc-anonimo-{Guid.CreateVersion7():N}",
                Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/PractitionerRDA" } },
                Active = false
            };
            anon.Name.Add(new HumanName
            {
                Use = HumanName.NameUse.Official,
                Text = especialistaNombreSnapshot ?? "PROFESIONAL NO REGISTRADO"
            });
            return anon;
        }
        var p = new Practitioner
        {
            Id = $"prc-{prof.Id:N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/PractitionerRDA" } },
            Active = true
        };
        p.Identifier.Add(new Identifier($"{RdaSidBase}/cedula-ciudadania", prof.NumeroDocumento)
        {
            Use = Identifier.IdentifierUse.Official,
            Type = MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSTipoDocumentoIdentidad",
                NormalizarTipoDoc(prof.TipoDocumento), TipoDocLabel(prof.TipoDocumento)))
        });
        var name = new HumanName { Use = HumanName.NameUse.Official };
        if (!string.IsNullOrWhiteSpace(prof.PrimerApellido) || !string.IsNullOrWhiteSpace(prof.SegundoApellido))
        {
            name.Family = $"{prof.PrimerApellido} {prof.SegundoApellido}".Trim();
        }
        if (!string.IsNullOrWhiteSpace(prof.PrimerNombre)) { name.GivenElement.Add(new FhirString(prof.PrimerNombre)); }
        if (!string.IsNullOrWhiteSpace(prof.SegundoNombre)) { name.GivenElement.Add(new FhirString(prof.SegundoNombre)); }
        if (string.IsNullOrWhiteSpace(name.Family) && name.GivenElement.Count == 0)
        {
            name.Text = prof.NombreCompleto;
        }
        p.Name.Add(name);
        if (!string.IsNullOrWhiteSpace(prof.RegistroMedico))
        {
            p.Identifier.Add(new Identifier($"{RdaSidBase}/registro-medico", prof.RegistroMedico)
            {
                Use = Identifier.IdentifierUse.Secondary,
                Type = new CodeableConcept { Text = "Registro Medico" }
            });
        }
        return p;
    }

    private static Patient BuildPatient(Paciente p)
    {
        var pat = new Patient
        {
            Id = $"pat-{p.Id:N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/PatientRDA" } },
            Active = true,
            BirthDate = p.FechaNacimiento?.ToString("yyyy-MM-dd"),
            Gender = MapGender(p.Sexo)
        };
        // Identificador principal
        pat.Identifier.Add(new Identifier($"{RdaSidBase}/cedula-ciudadania", p.NumeroDocumento)
        {
            Use = Identifier.IdentifierUse.Official,
            Type = MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSTipoDocumentoIdentidad",
                NormalizarTipoDoc(p.TipoDocumento), TipoDocLabel(p.TipoDocumento)))
        });
        // Nombre
        var name = new HumanName { Use = HumanName.NameUse.Official };
        if (!string.IsNullOrWhiteSpace(p.PrimerApellido) || !string.IsNullOrWhiteSpace(p.SegundoApellido))
        {
            name.Family = $"{p.PrimerApellido} {p.SegundoApellido}".Trim();
        }
        if (!string.IsNullOrWhiteSpace(p.PrimerNombre)) { name.GivenElement.Add(new FhirString(p.PrimerNombre)); }
        if (!string.IsNullOrWhiteSpace(p.SegundoNombre)) { name.GivenElement.Add(new FhirString(p.SegundoNombre)); }
        if (string.IsNullOrWhiteSpace(name.Family) && name.GivenElement.Count == 0)
        {
            name.Text = p.NombreCompleto;
        }
        pat.Name.Add(name);
        // Telecom
        if (!string.IsNullOrWhiteSpace(p.Telefono))
        {
            var telValor = string.IsNullOrWhiteSpace(p.CodigoPaisTelefono)
                ? p.Telefono
                : $"{p.CodigoPaisTelefono.TrimStart('+')}{p.Telefono}".Trim();
            pat.Telecom.Add(new ContactPoint(ContactPoint.ContactPointSystem.Phone,
                ContactPoint.ContactPointUse.Mobile, telValor));
        }
        if (!string.IsNullOrWhiteSpace(p.Email))
        {
            pat.Telecom.Add(new ContactPoint(ContactPoint.ContactPointSystem.Email,
                ContactPoint.ContactPointUse.Home, p.Email));
        }
        // Direccion
        if (!string.IsNullOrWhiteSpace(p.Direccion) || !string.IsNullOrWhiteSpace(p.Ciudad))
        {
            var addr = new Address
            {
                Use = Address.AddressUse.Home,
                Type = Address.AddressType.Physical,
                City = p.Ciudad,
                Country = "CO"
            };
            if (!string.IsNullOrWhiteSpace(p.Direccion)) { addr.LineElement.Add(new FhirString(p.Direccion)); }
            if (!string.IsNullOrWhiteSpace(p.Zona))
            {
                addr.Extension.Add(new Extension($"{RdaProfileBase}/ExtZonaTerritorial",
                    MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSZonaTerritorial",
                        p.Zona.StartsWith("URBAN", StringComparison.OrdinalIgnoreCase) ? "01" : "02",
                        p.Zona))));
            }
            pat.Address.Add(addr);
        }
        // Estado civil
        if (!string.IsNullOrWhiteSpace(p.EstadoCivil))
        {
            pat.MaritalStatus = new CodeableConcept { Text = p.EstadoCivil };
        }
        // Contacto de emergencia legacy (los nuevos contactos van como sub-recursos en Ola siguiente)
        if (!string.IsNullOrWhiteSpace(p.ContactoEmergencia))
        {
            var c = new Patient.ContactComponent
            {
                Name = new HumanName { Use = HumanName.NameUse.Official, Text = p.ContactoEmergencia }
            };
            c.Relationship.Add(new CodeableConcept { Text = p.Parentesco ?? "Contacto de emergencia" });
            if (!string.IsNullOrWhiteSpace(p.TelefonoEmergencia))
            {
                c.Telecom.Add(new ContactPoint(ContactPoint.ContactPointSystem.Phone,
                    ContactPoint.ContactPointUse.Mobile, p.TelefonoEmergencia));
            }
            pat.Contact.Add(c);
        }
        // Extensiones del perfil colombiano
        if (!string.IsNullOrWhiteSpace(p.Regimen))
        {
            pat.Extension.Add(new Extension($"{RdaProfileBase}/ExtRegimenAfiliacion",
                MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSRegimenAfiliacion",
                    p.Regimen.StartsWith("CONTRIB", StringComparison.OrdinalIgnoreCase) ? "01" : "02",
                    p.Regimen))));
        }
        if (!string.IsNullOrWhiteSpace(p.Ocupacion))
        {
            pat.Extension.Add(new Extension($"{RdaProfileBase}/ExtOcupacion", new FhirString(p.Ocupacion)));
        }
        return pat;
    }

    private static Encounter BuildEncounter(HistoriaClinica hc, ModalidadRdaIhce modalidad,
        Patient patient, Practitioner practitioner, Organization organization)
    {
        var e = new Encounter
        {
            Id = $"enc-{hc.Id:N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/EncounterRDA" } },
            Status = hc.Estado == HistoriaClinicaEstado.Cerrada
                ? Encounter.EncounterStatus.Finished
                : Encounter.EncounterStatus.InProgress,
            Class = new Coding("http://terminology.hl7.org/CodeSystem/v3-ActCode", "AMB", "ambulatory")
        };
        e.Identifier.Add(new Identifier("urn:oid:2.16.170.1.100.10.1.2", $"VISAL-HC-{hc.Id:N}"));
        e.Type.Add(MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSModalidadAtencionRDA",
            modalidad.ToString(), modalidad.ToString())));
        e.Subject = ReferenceTo(patient);
        e.ServiceProvider = ReferenceTo(organization);
        var part = new Encounter.ParticipantComponent
        {
            Individual = ReferenceTo(practitioner)
        };
        part.Type.Add(MakeCC(MakeCoding("http://terminology.hl7.org/CodeSystem/v3-ParticipationType", "ATND", "attender")));
        e.Participant.Add(part);
        e.Period = new Period
        {
            Start = hc.FechaApertura.ToOffset(TimeSpan.FromHours(-5)).ToString("o", CultureInfo.InvariantCulture),
            End = hc.FechaCierre?.ToOffset(TimeSpan.FromHours(-5)).ToString("o", CultureInfo.InvariantCulture)
        };
        return e;
    }

    private static Composition BuildComposition(HistoriaClinica hc, ModalidadRdaIhce modalidad,
        Patient patient, Encounter encounter, Practitioner practitioner, Organization organization,
        FhirDateTime now,
        List<Condition> conditions, List<MedicationStatement> meds, List<Procedure> procedures,
        AllergyIntolerance allergy)
    {
        var c = new Composition
        {
            Id = $"comp-{hc.Id:N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/CompositionRDA" } },
            Language = "es-CO",
            Identifier = new Identifier("urn:oid:2.16.170.1.100.10.1.1", $"VISAL-COMP-{hc.Id:N}"),
            Status = hc.Estado == HistoriaClinicaEstado.Cerrada
                ? CompositionStatus.Final
                : CompositionStatus.Preliminary,
            Type = MakeCC(MakeCoding(LoincSystem, "34133-9", "Summarization of episode note"),
                texto: "Resumen Digital de Atencion en Salud"),
            Subject = ReferenceTo(patient),
            Encounter = ReferenceTo(encounter),
            Date = now.ToString(),
            Title = $"Resumen Digital de Atencion - {ModalidadLabel(modalidad)} - {patient.Name.First().Text ?? patient.Name.First().Family}",
            Custodian = ReferenceTo(organization)
        };
        c.Category.Add(MakeCC(MakeCoding($"{RdaCodeSystemBase}/CSModalidadAtencionRDA",
            modalidad.ToString(), ModalidadLabel(modalidad))));
        c.Author.Add(ReferenceTo(practitioner));

        // 8 secciones obligatorias del perfil. Ola 3 enlaza los recursos clinicos via Entry.
        c.Section.Add(MakeSection("Motivo de consulta", "10154-3", "Chief complaint Narrative - Reported"));
        c.Section.Add(MakeSection("Historia de la enfermedad actual", "10164-2", "History of Present illness Narrative"));
        c.Section.Add(MakeSection("Antecedentes patologicos", "11348-0", "History of Past illness Narrative"));
        c.Section.Add(MakeSection("Antecedentes farmacologicos", "10160-0", "History of Medication use Narrative",
            meds.Select(ReferenceTo)));
        c.Section.Add(MakeSection("Alergias e intolerancias", "48765-2", "Allergies and adverse reactions Document",
            new[] { ReferenceTo(allergy) }));
        c.Section.Add(MakeSection("Examen fisico", "29545-1", "Physical findings Narrative"));
        c.Section.Add(MakeSection("Diagnosticos", "11450-4", "Problem list - Reported",
            conditions.Select(ReferenceTo)));
        c.Section.Add(MakeSection("Plan de tratamiento", "18776-5", "Plan of care note",
            procedures.Select(ReferenceTo)));
        return c;
    }

    // ===================== Recursos clinicos (Ola 3) =====================

    /// <summary>
    /// Diagnosticos del paciente. Visal hoy guarda dx en <c>Paciente.Cie10Codigo</c>
    /// + <c>DiagnosticoPrincipal</c>. El motor de formularios captura mas dx dentro de
    /// <c>HistoriaClinica.ValoresJson</c>, pero ese parsing semantico se deja para
    /// una iteracion posterior — por ahora exportamos el dx principal del Paciente.
    /// </summary>
    private static List<Condition> BuildConditions(Paciente p, Patient patient, Encounter encounter,
        Practitioner practitioner, List<string> advertencias)
    {
        var list = new List<Condition>();
        if (string.IsNullOrWhiteSpace(p.Cie10Codigo) && string.IsNullOrWhiteSpace(p.DiagnosticoPrincipal))
        {
            advertencias.Add("El paciente no tiene CIE-10 ni diagnostico principal registrado. La seccion Diagnosticos del Bundle queda vacia.");
            return list;
        }
        var dx = new Condition
        {
            Id = $"cond-{Guid.CreateVersion7():N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/ConditionRDA" } },
            ClinicalStatus = MakeCC(MakeCoding("http://terminology.hl7.org/CodeSystem/condition-clinical", "active", "Active")),
            VerificationStatus = MakeCC(MakeCoding("http://terminology.hl7.org/CodeSystem/condition-ver-status", "confirmed", "Confirmed")),
            Subject = ReferenceTo(patient),
            Encounter = ReferenceTo(encounter),
            Recorder = ReferenceTo(practitioner),
            RecordedDateElement = new FhirDateTime(DateTimeOffset.UtcNow)
        };
        dx.Category.Add(MakeCC(MakeCoding("http://terminology.hl7.org/CodeSystem/condition-category",
            "encounter-diagnosis", "Encounter Diagnosis")));
        dx.Code = new CodeableConcept
        {
            Text = p.DiagnosticoPrincipal ?? p.Cie10Codigo
        };
        if (!string.IsNullOrWhiteSpace(p.Cie10Codigo))
        {
            dx.Code.Coding.Add(new Coding("http://hl7.org/fhir/sid/icd-10", p.Cie10Codigo, p.DiagnosticoPrincipal));
        }
        list.Add(dx);
        return list;
    }

    /// <summary>
    /// Medicamentos prescritos en la HC. Mapea <c>HistoriaClinicaMedicamento</c> +
    /// catalogo <c>Medicamento</c> a MedicationStatement. El codigo CUM se arma como
    /// "{ExpedienteCum}-{ConsecutivoCum}" cuando ambos estan presentes.
    /// </summary>
    private static List<MedicationStatement> BuildMedicationStatements(
        IReadOnlyList<HistoriaClinicaMedicamento> rows, Patient patient, Encounter encounter, Practitioner practitioner)
    {
        var list = new List<MedicationStatement>();
        foreach (var r in rows)
        {
            var ms = new MedicationStatement
            {
                Id = $"med-{r.Id:N}",
                Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/MedicationStatementRDA" } },
                Status = MedicationStatement.MedicationStatusCodes.Active,
                Subject = ReferenceTo(patient),
                Context = ReferenceTo(encounter),
                InformationSource = ReferenceTo(practitioner),
                DateAssertedElement = new FhirDateTime(DateTimeOffset.UtcNow)
            };
            var cc = new CodeableConcept { Text = r.NombreMedicamento };
            // CUM si tenemos catalogo enlazado.
            if (r.Medicamento is not null)
            {
                var exp = r.Medicamento.ExpedienteCum?.Trim();
                var cons = r.Medicamento.ConsecutivoCum?.Trim();
                if (!string.IsNullOrWhiteSpace(exp) && !string.IsNullOrWhiteSpace(cons))
                {
                    cc.Coding.Add(new Coding($"{RdaCodeSystemBase}/CSMedicamentoCUM",
                        $"{exp}-{cons}", r.NombreMedicamento));
                }
                if (!string.IsNullOrWhiteSpace(r.Medicamento.Atc))
                {
                    cc.Coding.Add(new Coding("http://www.whocc.no/atc", r.Medicamento.Atc,
                        r.Medicamento.DescripcionAtc));
                }
            }
            ms.Medication = cc;
            // Dosificacion: texto libre de Posologia + frecuencia + dias.
            var dosageText = string.Join(" ", new[] { r.Cantidad, r.Frecuencia, r.Dias, r.Posologia }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(dosageText))
            {
                ms.Dosage.Add(new Dosage { Text = dosageText.Trim() });
            }
            list.Add(ms);
        }
        return list;
    }

    /// <summary>
    /// Procedimientos / remisiones de la HC, mapeados a Procedure con codigo CUPS.
    /// </summary>
    private static List<Procedure> BuildProcedures(IReadOnlyList<HistoriaClinicaRemision> rows,
        Patient patient, Encounter encounter, Practitioner practitioner, Organization organization,
        List<Condition> conditions)
    {
        var list = new List<Procedure>();
        foreach (var r in rows)
        {
            var proc = new Procedure
            {
                Id = $"proc-{r.Id:N}",
                Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/ProcedureRDA" } },
                Status = EventStatus.InProgress,
                Subject = ReferenceTo(patient),
                Encounter = ReferenceTo(encounter)
            };
            proc.Code = new CodeableConcept { Text = r.EspecialidadNombre };
            if (!string.IsNullOrWhiteSpace(r.EspecialidadCodigo))
            {
                proc.Code.Coding.Add(new Coding($"{RdaCodeSystemBase}/CSCUPS",
                    r.EspecialidadCodigo, r.EspecialidadNombre));
            }
            proc.Category = new CodeableConcept { Text = r.Capitulo };
            var performer = new Procedure.PerformerComponent
            {
                Actor = ReferenceTo(practitioner),
                OnBehalfOf = ReferenceTo(organization)
            };
            proc.Performer.Add(performer);
            // Si hay condiciones, vinculamos como razon.
            foreach (var cond in conditions)
            {
                proc.ReasonReference.Add(ReferenceTo(cond));
            }
            if (!string.IsNullOrWhiteSpace(r.Motivo))
            {
                proc.Note.Add(new Annotation { Text = new Markdown(r.Motivo) });
            }
            list.Add(proc);
        }
        return list;
    }

    /// <summary>
    /// Visal no tiene captura estructurada de alergias hoy, asi que reportamos NKDA
    /// (No Known Drug Allergies — SNOMED 716186003) por defecto. El profesional puede
    /// dejar nota en HC.ValoresJson; eso se mapeara en una iteracion futura.
    /// </summary>
    private static AllergyIntolerance BuildAllergyIntoleranceNkda(Paciente p, Patient patient,
        Practitioner practitioner, List<string> advertencias)
    {
        advertencias.Add("Visal no captura alergias estructuradas; el Bundle reporta NKDA (sin alergias conocidas) por defecto.");
        var a = new AllergyIntolerance
        {
            Id = $"alg-{Guid.CreateVersion7():N}",
            Meta = new Meta { Profile = new[] { $"{RdaProfileBase}/AllergyIntoleranceRDA" } },
            ClinicalStatus = MakeCC(MakeCoding("http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical",
                "active", "Active")),
            VerificationStatus = MakeCC(MakeCoding("http://terminology.hl7.org/CodeSystem/allergyintolerance-verification",
                "confirmed", "Confirmed")),
            Patient = ReferenceTo(patient),
            Recorder = ReferenceTo(practitioner),
            RecordedDateElement = new FhirDateTime(DateTimeOffset.UtcNow),
            Code = new CodeableConcept
            {
                Text = "Sin alergias conocidas (NKDA)"
            }
        };
        a.Code.Coding.Add(new Coding("http://snomed.info/sct", "716186003", "No known allergy"));
        return a;
    }

    // ===================== Helpers =====================

    private static Bundle.EntryComponent MakeEntry(Resource r)
        => new() { FullUrl = $"urn:uuid:{Guid.CreateVersion7()}", Resource = r };

    private static Composition.SectionComponent MakeSection(string title, string loincCode, string display,
        IEnumerable<ResourceReference>? entries = null)
    {
        var s = new Composition.SectionComponent
        {
            Title = title,
            Code = MakeCC(MakeCoding(LoincSystem, loincCode, display))
        };
        if (entries is not null)
        {
            foreach (var r in entries) { s.Entry.Add(r); }
        }
        return s;
    }

    private static Coding MakeCoding(string system, string code, string display)
        => new(system, code, display);

    private static CodeableConcept MakeCC(Coding coding, string? texto = null)
    {
        var cc = new CodeableConcept();
        cc.Coding.Add(coding);
        if (!string.IsNullOrWhiteSpace(texto)) { cc.Text = texto; }
        return cc;
    }

    private static ResourceReference ReferenceTo(Resource r) => new($"urn:uuid:{r.Id}");

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
        "CC" => "CC",
        "TI" => "TI",
        "CE" => "CE",
        "PA" or "PAS" => "PA",
        "RC" => "RC",
        "AS" => "AS",
        "MS" => "MS",
        _ => "CC"
    };

    private static string TipoDocLabel(string? td) => NormalizarTipoDoc(td) switch
    {
        "CC" => "Cedula de Ciudadania",
        "TI" => "Tarjeta de Identidad",
        "CE" => "Cedula de Extranjeria",
        "PA" => "Pasaporte",
        "RC" => "Registro Civil",
        "AS" => "Adulto sin identificar",
        "MS" => "Menor sin identificar",
        _ => "Cedula de Ciudadania"
    };

    private static string ModalidadLabel(ModalidadRdaIhce m) => m switch
    {
        ModalidadRdaIhce.Paciente => "RDA Paciente",
        ModalidadRdaIhce.Hospitalizacion => "Hospitalizacion",
        ModalidadRdaIhce.ConsultaExterna => "Consulta Externa",
        ModalidadRdaIhce.Urgencias => "Urgencias",
        _ => m.ToString()
    };

    private static string ComputeSha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(64);
        foreach (var b in bytes) { sb.Append(b.ToString("x2")); }
        return sb.ToString();
    }
}
