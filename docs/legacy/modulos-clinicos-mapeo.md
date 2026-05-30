# Mapeo de modulos clinicos Visal (legacy -> Visal .NET 9)

Fuente: `C:\Desarrollo\core\Bootstrap\Formularios\Modulos\Visal\controles\*.ascx`.

Este documento mapea los modulos relacionados con la operacion clinica del profesional para guiar la migracion. Layout + campos primero, logica funcional despues.

---

## 1. Profesionales (`ctrlProfesionales.ascx`)

**Estado:** YA implementado en `/cfg-profesionales` (CRUD completo) + nuevo flujo "Crear usuario" que vincula al Profesional con un TenantUser.

Reusable como esta. Las relaciones con Historias y Notas se cierran via `TenantUser.ProfesionalId`.

---

## 2. Historias Clinicas (`ctrlHistoriasClinicas.ascx`)

Ruta destino: `/historias`

### Layout: split 3/9

#### Sidebar izquierdo (col-md-3)

- **Card "Filtros":**
  - Fecha inicial (date)
  - Fecha final (date)
  - Filtro historia (dropdown, autopostback)
  - Boton "Mostrar historias"

- **Lista "Historias generadas al paciente":**
  - Card por item con:
    - Especialista (PROFESIONA_NOMBRE)
    - Fecha inicial (FECHA_REG dd/MM/yyyy)
    - Menu hamburguesa con acciones:
      - Ver esta historia
      - Impresion
      - Procesar Interoperabilidad
      - Abrir Documento

#### Panel derecho (col-md-9)

- **Barra de acciones (botones):**
  - Iniciar historia medica (primary)
  - Descartar / inactivar (danger)
  - Cerrar (outline-secondary)
  - Crear Copia (oculto por logica de servidor)

- **Motivo inactivacion** (textarea oculto hasta accion)

- **Barra de busqueda:** "BUSCAR DATOS DE HISTORIA:" + input con autocompletar + boton filtro

- **Tabs:**
  1. Historial (default, contiene `crtCargaEncuestaII` - encuesta dinamica)
  2. Ordenes medicamento (`ctrlOrdenMedicamentos`)
  3. Ordenes a Servicios (`ctrlOrdenServicios`)
  4. Remisiones (`ctrlOrdenRemisiones`)
  5. Ordenes incapacidad (`ctrlOrdenIncapacidades`)
  6. Ordenes de certificaciones (`ctrlOrdenCertificaciones`)

### Datos de cada item de la lista
- REFERENCIAII (id de la historia)
- REG (token)
- FORMATO (codigo de formato de historia)
- ESTADO (estado actual)
- PROFESIONA_NOMBRE (especialista que la creo)
- FECHA_REG (fecha)

---

## 3. Notas Medicas (`ctrlNotasMedicas.ascx`)

Ruta destino: `/notas`

### Header
- Titulo: "Notas Medicas"
- Subtitulo: "Modulo Profesional de Terapias"
- Boton derecho: "Guardado Definitivo" (primary)

### Tabs (6)

1. Notas Completadas
2. **Diligenciamiento** (default)
3. Firma
4. Seguimiento
5. Documentos Externos
6. Ver Historias

### Tab Diligenciamiento (formulario principal)

| Fila | Campos |
|---|---|
| 1 | Seleccionar Fecha Nota (date) \| Nombre Paciente (RO) \| Documento Paciente (RO) |
| 2 | Codigo Unico Nota (RO) \| Hora Nota (dropdown) \| Fecha Agregada Nota (RO) \| Sesion No (text) |
| 3 | Contenido de la Nota (textarea 12 filas) |
| 4 | [Guardado Parcial] [Guardado Definitivo] [Cancelar] |

### Tab Notas Completadas

- Filtros: Paciente (dropdown) + Ver Todos; Desde / Hasta (date); Descargar Notas (Generar Reporte)
- Acciones: Ver Nota, Generar Informes Fin, Crear Cuenta de Cobro
- Grid `grdNotasComp` con columnas:
  - checkbox seleccion
  - CODINT (Codigo Interno)
  - CODASIGN (Asignacion)
  - ORDEN
  - TIPOTERAPIA
  - SESSIONNO
  - COMPLETADO
  - NODOCPACIENTE (Paciente ID)
  - NOMPACIENTE (Paciente Nombre)
  - IDPROFECIONAL (Profesional)
  - FECHA (dd/MM/yyyy)
  - NOTA (Contenido)
  - MESASIGNADO
  - DETALLESERVICIO
  - CODSERVICIO
  - CONTRATO

### Tab Firma

- "Ingrese Firma Paciente"
- Boton "Limpiar"
- Canvas 450x175 px para dibujar firma (mouse + touch)
- Botones: Guardar, Cancelar

### Tab Documentos Externos

- Fila 1: Paciente (dropdown)
- Fila 2: Nombre del Archivo (dropdown: Lista de firmas, Escala, Formato)
- Fila 3: Tipo de Terapia (dropdown: FISIOTERAPEUTA, FONOAUDIOLOGIA, TERAPIAOCUPACIONAL, TERAPIARESPIRATORIA, NEBULIZACION, TRAQUEOSTOMIA) + Mes del Documento (dropdown meses)
- Panel izquierdo: ANOTACIONES DE LA IMAGEN (textarea) + uploader de archivo
- Panel centro: CARPETAS (arbol)
- Panel derecho: Visor de documentos

### Tab Ver Historias

- Pacientes (dropdown) + Ver Todos + Ver Historia
- Area de historias (panel con cards)

---

## 4. Otros modulos referenciados (deferred)

Subcontroles del tab "Historial" / "Ordenes" no migrados aun:

- `crtCargaEncuestaII.ascx` - encuesta dinamica que se llena segun el formato de historia.
- `ctrlOrdenMedicamentos.ascx` - prescripcion de medicamentos
- `ctrlOrdenServicios.ascx` - solicitud de servicios adicionales
- `ctrlOrdenRemisiones.ascx` - remisiones a otros especialistas
- `ctrlOrdenIncapacidades.ascx` - emision de incapacidades
- `ctrlOrdenCertificaciones.ascx` - emision de certificaciones medicas

Se dejan como tabs con placeholder por ahora. Se migran cuando se cierre el flujo principal de Historias y Notas.

---

## Notas de implementacion

- Toda relacion con un paciente/profesional viaja por `PacienteId` y `ProfesionalId` (FK).
- El boton "Notas" del modulo `/atencion` abrira el editor de nota del tab Diligenciamiento de `/notas` con la sesion preseleccionada.
- "Iniciar historia medica" de `/historias` debe verificar la vigencia configurable en `Configuracion de Empresa` (clave `clinica.meses_validez_historia`).
- El motor de formularios existente (`/formularios`) probablemente alimenta `crtCargaEncuestaII` con la plantilla dinamica.
