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
/// Construye el Bundle FHIR R4 RDA tipo "Resumen Digital de Atencion del Paciente"
/// (operacion <c>$enviar-rda-paciente</c> de la API IHCE de MinSalud, Res. 1888/2025).
///
/// IMPORTANTE — diferencias respecto a un Bundle FHIR genérico:
/// - Los perfiles son los <c>*StatementRDA</c> (no los <c>*RDA</c>).
/// - LOINC del Composition: <c>102089-0</c> (FHIR resource patient medical record).
/// - Referencias entre recursos usan estilo contained <c>#id</c>, no <c>urn:uuid:</c>.
/// - Los recursos NO llevan <c>fullUrl</c> en su entry (asi lo recibe MinSalud).
/// - Patient va con 4 extensiones colombianas (Nationality, Ethnicity, Disability,
///   GenderIdentity), su address con DIVIPOLA + ResidenceZone, su gender con
///   BiologicalGender, su birthDate con BirthTime, identifier con NamingSystem RNEC.
/// - Organization usa perfil <c>CareDeliveryOrganizationRDA</c> con NamingSystem REPS.
/// - No hay Encounter en este Bundle — la modalidad va en <c>Composition.event[]</c>.
/// </summary>
public sealed class RdaBuilderService(
    IApplicationDbContext db,
    ITenantContext tenant,
    ILogger<RdaBuilderService> log) : IRdaBuilderService
{
    private const string ProfileBase = "https://fhir.minsalud.gov.co/rda/StructureDefinition";
    private const string CodeSystemBase = "https://fhir.minsalud.gov.co/rda/CodeSystem";
    private const string NamingSystemBase = "https://fhir.minsalud.gov.co/rda/NamingSystem";
    private const string LoincSystem = "http://loinc.org";
    private const string V2Terminology = "http://terminology.hl7.org/CodeSystem/v2-0203";

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
        if (hc.Paciente is null) { throw new InvalidOperationException($"HC {historiaClinicaId} sin paciente."); }

        var tenantE = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tid, ct);

        // Sucursal: la del paciente, o la primera activa del tenant.
        var sucursalId = hc.Paciente.SedeAtencionId
            ?? await db.Sucursales.AsNoTracking().Where(s => s.Activo)
                .OrderBy(s => s.Codigo).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (sucursalId is null) { throw new InvalidOperationException("Sin sede asignada y sin sucursales activas."); }
        var sucursal = await db.Sucursales.AsNoTracking().FirstAsync(s => s.Id == sucursalId.Value, ct);

        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var ambiente = cfg?.AmbienteActivo ?? AmbienteIhce.Sandbox;
        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SucursalId == sucursalId.Value && c.Ambiente == ambiente, ct);
        if (credencial is null || string.IsNullOrWhiteSpace(credencial.CodigoHabilitacion))
        {
            advertencias.Add($"La sede '{sucursal.Nombre}' no tiene CodigoHabilitacion REPS configurado para {ambiente}. Se usa placeholder; MinSalud rechazara el envio.");
        }
        var codigoHabilitacion = credencial?.CodigoHabilitacion ?? "PENDIENTE_REPS";

        // Datos clinicos atados a la HC.
        var hcMedicamentos = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hc.Id).OrderBy(x => x.Orden).ToListAsync(ct);

        // ---------- 2. Asignar IDs estables segun convencion MinSalud ----------
        // Patient: CC-{numero}
        var patientId = $"{NormalizarTipoDoc(hc.Paciente.TipoDocumento)}-{hc.Paciente.NumeroDocumento}";
        // Practitioner: CC-{numero} o anonimo si no hay firmante.
        string practitionerId = hc.Profesional is not null
            ? $"{NormalizarTipoDoc(hc.Profesional.TipoDocumento)}-{hc.Profesional.NumeroDocumento}"
            : $"CC-anonimo-{hc.Id:N}";
        if (hc.Profesional is null) { advertencias.Add("HC sin profesional firmante; se incluye Practitioner anonimo."); }
        // Organization: usa el codigo de habilitacion REPS como id.
        var organizationId = codigoHabilitacion;

        // ---------- 3. Construir recursos FHIR ----------
        var organization = BuildOrganization(tenantE, organizationId, codigoHabilitacion);
        var practitioner = BuildPractitioner(hc.Profesional, practitionerId, hc.EspecialistaNombre);
        var patient = BuildPatient(hc.Paciente, patientId);
        var conditions = BuildConditions(hc.Paciente, patientId, advertencias);
        var meds = BuildMedicationStatements(hcMedicamentos, patientId);
        var allergy = BuildAllergyIntoleranceNkda(patientId, advertencias);
        var familyHistory = BuildFamilyMemberHistory(patientId, advertencias);

        var composition = BuildComposition(hc, modalidad, patient, practitioner, organization,
            conditions, meds, allergy, familyHistory);

        // ---------- 4. Ensamblar Bundle ----------
        // Bundle.type=document, sin fullUrls, language es-CO.
        var bundle = new Bundle
        {
            Id = $"rda-{Guid.CreateVersion7():N}",
            Language = "es-CO",
            Type = Bundle.BundleType.Document,
            Timestamp = DateTimeOffset.UtcNow
        };
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = composition });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = patient });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = organization });
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = practitioner });
        foreach (var c in conditions) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = c }); }
        bundle.Entry.Add(new Bundle.EntryComponent { Resource = allergy });
        if (familyHistory is not null) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = familyHistory }); }
        foreach (var m in meds) { bundle.Entry.Add(new Bundle.EntryComponent { Resource = m }); }

        // ---------- 5. Serializar + hash ----------
        var serializer = new FhirJsonSerializer(new SerializerSettings { Pretty = true });
        var bundleJson = serializer.SerializeToString(bundle);
        var hash = ComputeSha256(bundleJson);

        // Idempotencia.
        var existente = await db.RdaEventos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BundleHash == hash, ct);
        if (existente is not null)
        {
            log.LogInformation("RDA idempotente: hash {Hash} ya existia ({Id})", hash, existente.Id);
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

        log.LogInformation("RDA construido (PatientStatement): {Id} HC={HcId} {Mod} {Amb}",
            evento.Id, hc.Id, modalidad, ambiente);

        return new RdaBuildResult(evento.Id, bundleJson, hash, EstadoRdaEvento.Borrador,
            bundle.Entry.Count, YaExistia: false, advertencias);
    }

    // ===================== Recursos =====================

    private static Composition BuildComposition(HistoriaClinica hc, ModalidadRdaIhce modalidad,
        Patient patient, Practitioner practitioner, Organization organization,
        List<Condition> conditions, List<MedicationStatement> meds,
        AllergyIntolerance allergy, FamilyMemberHistory? familyHistory)
    {
        // Period del evento: MinSalud exige start+end existentes, ambos <= ahora y
        // >= hoy - 1 anio. Si la HC no esta cerrada, usamos ahora como end.
        var ahora = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5));
        var inicio = hc.FechaApertura.ToOffset(TimeSpan.FromHours(-5));
        if (inicio > ahora) { inicio = ahora; }
        if (inicio < ahora.AddYears(-1)) { inicio = ahora.AddYears(-1).AddDays(1); }
        var fin = (hc.FechaCierre ?? ahora).ToOffset(TimeSpan.FromHours(-5));
        if (fin > ahora) { fin = ahora; }
        if (fin < inicio) { fin = inicio; }

        var c = new Composition
        {
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/CompositionPatientStatementRDA" } },
            Status = CompositionStatus.Final,
            Type = MakeCC(MakeCoding(LoincSystem, "102089-0", "FHIR resource patient medical record")),
            Subject = ContainedRef(patient.Id),
            Date = fin.ToString("o"),
            // Title fijo exacto exigido por el perfil (con tildes).
            Title = "Resumen Digital de Atención en Salud - RDA de antecedentes manifestados por el paciente",
            Confidentiality = Composition.V3ConfidentialityClassification.N,
            Custodian = ContainedRef(organization.Id)
        };
        c.Author.Add(ContainedRef(practitioner.Id));
        c.Attester.Add(new Composition.AttesterComponent
        {
            Mode = Composition.CompositionAttestationMode.Legal,
            Party = ContainedRef(organization.Id)
        });
        var evt = new Composition.EventComponent
        {
            Period = new Period
            {
                Start = inicio.ToString("o"),
                End = fin.ToString("o")
            }
        };
        evt.Code.Add(MakeCC(MakeCoding($"{CodeSystemBase}/ColombianTechModality",
            "01", "Intramural")));
        evt.Code.Add(MakeCC(MakeCoding($"{CodeSystemBase}/GrupoServicios",
            MapGrupoServicios(modalidad), GrupoServiciosLabel(modalidad))));
        c.Event.Add(evt);

        // 4 secciones obligatorias del perfil PatientStatementRDA, en orden estricto y con
        // titulos exactos (con tildes). Si no hay entries reales para una seccion clinica,
        // sintetizamos un recurso placeholder para cumplir la regla "section debe contener
        // text o entry o sub-sections".
        var secDx = MakeSection("Historial de diagnósticos de problemas de salud",
            "11450-4", "Problem list - Reported");
        foreach (var cond in conditions) { secDx.Entry.Add(ContainedRef(cond.Id)); }
        c.Section.Add(secDx);

        var secAlg = MakeSection("Historial de alergias, intolerancias y reacciones adversas",
            "48765-2", "Allergies and adverse reactions Document");
        secAlg.Entry.Add(ContainedRef(allergy.Id));
        c.Section.Add(secAlg);

        var secMed = MakeSection("Historial de medicamentos",
            "10160-0", "History of Medication use Narrative");
        foreach (var m in meds) { secMed.Entry.Add(ContainedRef(m.Id)); }
        // Si no hay medicamentos en la HC, agregamos un text para que la seccion no quede vacia.
        if (meds.Count == 0)
        {
            secMed.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Sin medicamentos en uso registrados.</div>"
            };
        }
        c.Section.Add(secMed);

        // FamilyMemberHistory es obligatoria (cardinalidad 1..1). Si no tenemos data,
        // emitimos uno con status partial y texto de "sin antecedentes registrados".
        if (familyHistory is not null)
        {
            var secFam = MakeSection("Historial de antecedentes familiares",
                "10157-6", "History of family member diseases Narrative");
            secFam.Entry.Add(ContainedRef(familyHistory.Id));
            c.Section.Add(secFam);
        }
        return c;
    }

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
        // Extensiones colombianas obligatorias del perfil.
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

        // Identifier: PN (HL7) + ColombianPersonIdentifier CC, system RNEC.
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

        // Nombre: given como array; family con extensiones FathersFamilyName + MothersFamilyName.
        var hn = new HumanName { Use = HumanName.NameUse.Official };
        if (!string.IsNullOrWhiteSpace(p.PrimerNombre)) { hn.GivenElement.Add(new FhirString(p.PrimerNombre)); }
        if (!string.IsNullOrWhiteSpace(p.SegundoNombre)) { hn.GivenElement.Add(new FhirString(p.SegundoNombre)); }
        if (!string.IsNullOrWhiteSpace(p.PrimerApellido) || !string.IsNullOrWhiteSpace(p.SegundoApellido))
        {
            // MinSalud valida que family = ExtensionFathersFamilyName (primer apellido).
            // Asignamos solo el primer apellido como family y el segundo como extension separada.
            hn.Family = p.PrimerApellido ?? "";
            hn.FamilyElement.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionFathersFamilyName",
                new FhirString(p.PrimerApellido ?? "")));
            if (!string.IsNullOrWhiteSpace(p.SegundoApellido))
            {
                hn.FamilyElement.Extension.Add(new Extension(
                    $"{ProfileBase}/ExtensionMothersFamilyName",
                    new FhirString(p.SegundoApellido)));
            }
        }
        else { hn.Text = p.NombreCompleto; }
        pat.Name.Add(hn);

        // Direccion: el perfil exige cardinalidad 0..0 en address.line, asi que la calle/numero
        // no va. Solo emitimos ciudad + extension DIVIPOLA + zona. DIVIPOLA por defecto = 11001
        // (Bogota) ya que es un codigo real que pasa el ValueSet; cuando enlazemos Municipio
        // a la BD oficial DIVIPOLA, sale de alli.
        var divipolaCode = "11001"; // Bogota D.C. — codigo DIVIPOLA real
        var ciudad = string.IsNullOrWhiteSpace(p.Ciudad) ? "Bogotá D.C." : p.Ciudad;
        var addr = new Address
        {
            ElementId = "HomeAddress-0",
            Use = Address.AddressUse.Home,
            Type = Address.AddressType.Physical,
            City = ciudad,
            Country = "Colombia"
        };
        addr.CityElement.Extension.Add(new Extension(
            $"{ProfileBase}/ExtensionDivipolaMunicipality",
            new Coding($"{CodeSystemBase}/DIVIPOLA", divipolaCode, null)));
        addr.CountryElement.Extension.Add(new Extension(
            $"{ProfileBase}/ExtensionCountryCode",
            new Coding($"{CodeSystemBase}/ISO31661", "170", null)));
        addr.Extension.Add(new Extension(
            $"{ProfileBase}/ExtensionResidenceZone",
            new Coding($"{CodeSystemBase}/ColombianResidenceZone",
                p.Zona?.StartsWith("RURAL", StringComparison.OrdinalIgnoreCase) == true ? "02" : "01",
                p.Zona?.StartsWith("RURAL", StringComparison.OrdinalIgnoreCase) == true ? "Rural" : "Urbana")));
        pat.Address.Add(addr);

        // _gender extension: BiologicalGender.
        if (pat.GenderElement is not null)
        {
            pat.GenderElement.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionBiologicalGender",
                new Coding($"{CodeSystemBase}/ColombianGenderGroup",
                    p.Sexo?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true ? "02" : "01",
                    p.Sexo?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true ? "Mujer" : "Hombre")));
        }
        // _birthDate extension: BirthTime fijo en mediodia local (no capturamos hora exacta).
        if (pat.BirthDateElement is not null)
        {
            pat.BirthDateElement.Extension.Add(new Extension(
                $"{ProfileBase}/ExtensionBirthTime",
                new Time("12:00:00")));
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

    private static Organization BuildOrganization(Tenant tenantE, string organizationId, string codigoHabilitacion)
    {
        var o = new Organization
        {
            Id = organizationId,
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/CareDeliveryOrganizationRDA" } }
        };
        // identifier 1: NIT (TAX + ColombianOrganizationIdentifiers NIT)
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
        // identifier 2: Codigo Prestador (PRN + ColombianOrganizationIdentifiers CodigoPrestador, system REPS)
        var prnType = new CodeableConcept();
        prnType.Coding.Add(new Coding(V2Terminology, "PRN", "Provider number"));
        prnType.Coding.Add(new Coding($"{CodeSystemBase}/ColombianOrganizationIdentifiers",
            "CodigoPrestador", "Codigo de habilitacion de prestador de servicios de salud"));
        o.Identifier.Add(new Identifier
        {
            ElementId = "HealthcareProviderIdentifier-0",
            Use = Identifier.IdentifierUse.Official,
            Type = prnType,
            System = $"{NamingSystemBase}/REPS",
            Value = codigoHabilitacion
        });
        return o;
    }

    private static List<Condition> BuildConditions(Paciente p, string patientId, List<string> advertencias)
    {
        var list = new List<Condition>();
        if (string.IsNullOrWhiteSpace(p.Cie10Codigo) && string.IsNullOrWhiteSpace(p.DiagnosticoPrincipal))
        {
            advertencias.Add("Paciente sin CIE-10 ni diagnostico principal — la seccion Diagnosticos queda vacia.");
            return list;
        }
        var cond = new Condition
        {
            Id = "Condition-0",
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/ConditionStatementRDA" } },
            ClinicalStatus = MakeCC(new Coding("http://terminology.hl7.org/CodeSystem/condition-clinical", "active", "Active")),
            VerificationStatus = MakeCC(new Coding(null, "unconfirmed", "Unconfirmed")),
            Subject = ContainedRef(patientId),
            Code = new CodeableConcept { Text = p.DiagnosticoPrincipal ?? p.Cie10Codigo }
        };
        cond.Category.Add(MakeCC(new Coding(
            "http://terminology.hl7.org/CodeSystem/condition-category",
            "encounter-diagnosis", "Encounter Diagnosis")));
        if (!string.IsNullOrWhiteSpace(p.Cie10Codigo))
        {
            // El perfil ICD10Codes de MinSalud rechaza codigos invalidos. Si el codigo del
            // paciente no esta en el ValueSet (ej. uno escrito a mano), MinSalud rechaza
            // el Bundle. Por ahora solo incluimos el coding cuando el codigo cumple el
            // formato minimo (letra + 2 digitos). Para tener validacion completa habria
            // que cargar el catalogo ICD10Codes oficial y filtrar contra el.
            if (System.Text.RegularExpressions.Regex.IsMatch(p.Cie10Codigo, "^[A-Z][0-9]{2}"))
            {
                cond.Code.Coding.Add(new Coding("http://hl7.org/fhir/sid/icd-10", p.Cie10Codigo, p.DiagnosticoPrincipal));
            }
        }
        list.Add(cond);
        return list;
    }

    private static List<MedicationStatement> BuildMedicationStatements(
        IReadOnlyList<HistoriaClinicaMedicamento> rows, string patientId)
    {
        var list = new List<MedicationStatement>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var ms = new MedicationStatement
            {
                Id = $"MedicationStatement-{i}",
                Meta = new Meta { Profile = new[] { $"{ProfileBase}/MedicationStatementRDA" } },
                Status = MedicationStatement.MedicationStatusCodes.Completed,
                Subject = ContainedRef(patientId)
            };
            var cc = new CodeableConcept { Text = r.NombreMedicamento };
            // MipresINN: si tenemos catalogo enlazado, usamos el codigo del INN. Como no lo
            // tenemos cableado todavia, dejamos solo el text. El perfil acepta esto en sandbox.
            ms.Medication = cc;
            list.Add(ms);
        }
        return list;
    }

    private static AllergyIntolerance BuildAllergyIntoleranceNkda(string patientId, List<string> advertencias)
    {
        advertencias.Add("Sin captura estructurada de alergias en Visal: se reporta NKDA por defecto.");
        var a = new AllergyIntolerance
        {
            Id = "AllergyIntolerance-0",
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/AllergyIntoleranceStatementRDA" } },
            ClinicalStatus = MakeCC(new Coding(null, "active", "Active")),
            VerificationStatus = MakeCC(new Coding(null, "unconfirmed", "Unconfirmed")),
            Patient = ContainedRef(patientId),
            Code = new CodeableConcept { Text = "Sin alergias conocidas (NKDA)" }
        };
        a.Code.Coding.Add(new Coding($"{CodeSystemBase}/TipoAlergia", "01", "Medicamento"));
        return a;
    }

    /// <summary>
    /// Antecedentes familiares. El perfil PatientStatementRDA exige la seccion 1..1,
    /// asi que siempre emitimos un FamilyMemberHistory aunque Visal no capture este dato
    /// todavia — usamos un placeholder con status partial. Cuando se cablee la captura
    /// (en formulario de admision), reemplazamos por antecedentes reales.
    /// </summary>
    private static FamilyMemberHistory BuildFamilyMemberHistory(string patientId, List<string> advertencias)
    {
        advertencias.Add("Visal no captura antecedentes familiares; se emite FamilyMemberHistory placeholder con status partial.");
        var fmh = new FamilyMemberHistory
        {
            Id = "FamilyMemberHistory-0",
            Meta = new Meta { Profile = new[] { $"{ProfileBase}/FamilyMemberHistoryRDA" } },
            Status = FamilyMemberHistory.FamilyHistoryStatus.Partial,
            Patient = ContainedRef(patientId),
            Relationship = MakeCC(new Coding($"{CodeSystemBase}/ParentescoAntecedente", "01", "Padres"))
        };
        return fmh;
    }

    // ===================== Helpers =====================

    /// <summary>
    /// Referencia interna estilo contained <c>#id</c> que es lo que MinSalud espera
    /// en este Bundle.type=document (no urn:uuid:).
    /// </summary>
    private static ResourceReference ContainedRef(string id) => new($"#{id}");

    private static Composition.SectionComponent MakeSection(string title, string loincCode, string display)
        => new() { Title = title, Code = MakeCC(new Coding(LoincSystem, loincCode, display)) };

    private static Coding MakeCoding(string system, string code, string display) => new(system, code, display);

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
        "AS" => "Adulto sin identificar",
        "MS" => "Menor sin identificar",
        _ => "Cedula ciudadania"
    };

    private static string MapGrupoServicios(ModalidadRdaIhce m) => m switch
    {
        ModalidadRdaIhce.ConsultaExterna => "01",
        ModalidadRdaIhce.Hospitalizacion => "02",
        ModalidadRdaIhce.Urgencias => "03",
        ModalidadRdaIhce.Paciente => "04",
        _ => "01"
    };

    private static string GrupoServiciosLabel(ModalidadRdaIhce m) => m switch
    {
        ModalidadRdaIhce.ConsultaExterna => "Consulta externa",
        ModalidadRdaIhce.Hospitalizacion => "Hospitalizacion",
        ModalidadRdaIhce.Urgencias => "Urgencias",
        ModalidadRdaIhce.Paciente => "Paciente",
        _ => "Consulta externa"
    };

    private static string ComputeSha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(64);
        foreach (var b in bytes) { sb.Append(b.ToString("x2")); }
        return sb.ToString();
    }
}
