# Interoperabilidad — RDA / IHCE (Res. 1888 de 2025)

Documentacion tecnica y artefactos para la implementacion del **Resumen Digital de Atencion en Salud (RDA)** bajo la Resolucion 1888 de 2025 del Ministerio de Salud y Proteccion Social de Colombia, en el marco de la **Interoperabilidad de la Historia Clinica Electronica (IHCE)**.

## Marco normativo

- **Ley 2015 de 2020** — crea la Historia Clinica Electronica Interoperable.
- **Resolucion 866 de 2021** — conjuntos minimos de datos clinicos.
- **Resolucion 1888 de 2025** — adopta el RDA (vigencia: 15-abr-2026, ya vencida a 18-jun-2026).
- **Guia de implementacion FHIR (Vulcano)** — `https://vulcano.ihcecol.gov.co/` v1.0.0.

## Que es el RDA

Documento clinico estandarizado que contiene la informacion **minima esencial de una atencion en salud**, intercambiado entre prestadores via HL7 FHIR R4 sobre HTTPS con TLS 1.3 + AES-256 a la plataforma nacional de MinSalud.

## Modalidades reconocidas (Res. 1888, primera fase)

| Modalidad | Codigo | Aplica a Visal RT |
|---|---|---|
| RDA Paciente | `Paciente` | Resumen consolidado del paciente |
| RDA Hospitalizacion | `Hospitalizacion` | No (somos domiciliario) |
| RDA Consulta Externa | `ConsultaExterna` | **SI — modalidad principal** para cierre de cada HC |
| RDA Urgencias | `Urgencias` | No |

La modalidad domiciliaria no esta nombrada explicitamente; se usa `ConsultaExterna` hasta que MinSalud aclare.

## Trigger en Visal

**Por cada HC cerrada** se emite un Bundle FHIR RDA. Por ahora **manual** desde el modulo `/interoperabilidad/rda` (sin trigger automatico hasta que el flujo este estable).

## Estructura del Bundle

```
Bundle (type=document)
└── entry[0] Composition (raiz, LOINC 34133-9)
    ├── section: Motivo de consulta            (LOINC 10154-3)
    ├── section: Historia enfermedad actual    (LOINC 10164-2)
    ├── section: Antecedentes patologicos      (LOINC 11348-0)
    ├── section: Antecedentes farmacologicos   (LOINC 10160-0) → MedicationStatement
    ├── section: Alergias                      (LOINC 48765-2) → AllergyIntolerance
    ├── section: Examen fisico                 (LOINC 29545-1)
    ├── section: Diagnosticos                  (LOINC 11450-4) → Condition (CIE-10)
    └── section: Plan de tratamiento           (LOINC 18776-5) → Procedure (CUPS)
├── Patient (PatientRDA)
├── Encounter (EncounterRDA)
├── Practitioner (PractitionerRDA)
├── Organization (OrganizationRDA — IPS prestadora)
├── Condition (ConditionRDA — dx principal CIE-10)
├── MedicationStatement (MedicationStatementRDA — CUM)
├── Procedure (ProcedureRDA — CUPS)
└── AllergyIntolerance (AllergyIntoleranceRDA)
```

## Mapeo Visal → FHIR

| Recurso FHIR | Origen en Visal |
|---|---|
| `Bundle` | Nuevo entidad `RdaEvento` (1 por HC cerrada) |
| `Composition` | `HistoriaClinica` + extraccion de secciones desde `ValoresJson` |
| `Patient` | `Paciente` (todos los campos demograficos + DIVIPOLA + extension Regimen) |
| `Encounter` | `HistoriaClinica` (id, fecha apertura/cierre, modalidad) |
| `Practitioner` | `Profesional` (id, nombre, registro medico, tipo) |
| `Organization` | `Tenant` + `Sucursal` (NIT, razon social, codigo habilitacion REPS) |
| `Condition` | `Paciente.Cie10Codigo` + diagnosticos extraidos de `HistoriaClinica.ValoresJson` |
| `MedicationStatement` | `HistoriaClinicaMedicamento` + `Medicamento` (CUM) |
| `Procedure` | `HistoriaClinicaRemision` + `Cup` (CUPS) |
| `AllergyIntolerance` | Extraccion de `ValoresJson.alergias` (o NKDA por defecto) |

## Gaps conocidos (a llenar antes de envio productivo)

- [ ] **CodigoHabilitacion REPS** del prestador (12 digitos) — agregar a `Tenant` o `Sucursal`.
- [ ] **DIVIPOLA** del paciente — `Municipio.Codigo` debe almacenar el codigo DIVIPOLA oficial (5 digitos: 2 depto + 3 mpio), no solo el ExternalId de api-colombia.
- [ ] **Diagnosticos estructurados** — hoy `HistoriaClinica.ValoresJson` guarda dx como texto libre; el RDA necesita codigo CIE-10 + texto. Refinar el motor de formularios para que el field "diagnostico_principal" lleve codigo + display.
- [ ] **Alergias estructuradas** — hoy no se capturan en HC. Agregar campo en formulario o seccion clinica.
- [ ] **CUM real de medicamentos** — la tabla `medicamentos` tiene `ExpedienteCum` + `ConsecutivoCum` pero el formato del RDA es probablemente `<expediente>-<consecutivo>`. Confirmar contra el CodeSystem oficial.
- [ ] **Endpoint y credenciales IHCE** — solicitar a Mesa de Ayuda IHCE (`https://www.minsalud.gov.co/ihce/Paginas/Mesa-de-Ayuda.aspx`).

## Validacion local

Antes de cualquier envio, el Bundle debe validar contra el paquete oficial `minsalud.fhir.co.rda` v1.0.0 usando uno de:

- **Vulcano** (online): subir el JSON en `https://vulcano.ihcecol.gov.co/`.
- **Firely .NET Validator** (local): consumir el paquete NPM `minsalud.fhir.co.rda` y validar con `Hl7.Fhir.Validation` en el backend.
- **HL7 FHIR Validator CLI**: `java -jar validator_cli.jar -ig minsalud.fhir.co.rda#1.0.0 <bundle.json>`.

## Ejemplos

| Archivo | Descripcion |
|---|---|
| [`ejemplos/rda-juan-carlos-001.json`](./ejemplos/rda-juan-carlos-001.json) | Bundle FHIR R4 sintetico para JUAN CARLOS PEREZ MOLINA (CC 1010101010) con HC de fisioterapia, dx M54.5 lumbago, ibuprofeno 400mg, 10 sesiones de terapia fisica. **Construido a mano**, no por el backend. |

## Roadmap implementacion

| Ola | Estado | Descripcion |
|---|---|---|
| Ola 1 | pending | Firely .NET SDK + entidad `RdaEvento` + migracion |
| Ola 2 | pending | `RdaBuilderService` minimo (Composition + Patient + Encounter + Practitioner + Organization) |
| Ola 3 | pending | Secciones clinicas (Condition + MedicationStatement + Procedure + AllergyIntolerance) |
| Ola 4 | pending | Modulo `/interoperabilidad/rda` UI (lista, generar manual, validador, descarga) |
| Ola 5 | pending | Cliente HTTP TLS 1.3 a plataforma IHCE (endpoint pendiente) |

## Referencias

- Guia de implementacion oficial: <https://vulcano.ihcecol.gov.co/>
- PDF Res. 1888/2025: <https://www.minsalud.gov.co/Normatividad_Nuevo/Resolucion%20No%201888%20de%202025.pdf>
- Mesa de Ayuda IHCE: <https://www.minsalud.gov.co/ihce/Paginas/Mesa-de-Ayuda.aspx>
- Firely .NET SDK: <https://github.com/FirelyTeam/firely-net-sdk>
