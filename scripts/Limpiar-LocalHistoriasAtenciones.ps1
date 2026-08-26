<#
.SYNOPSIS
    Limpia (borra) TODAS las historias clinicas, coordinaciones y atenciones de
    la base LOCAL de desarrollo (visal_dev), para dejarla lista para pruebas
    manuales. NO toca pacientes, formularios, usuarios ni catalogos.

.DESCRIPTION
    *** SOLO LOCAL ***. Ejecuta un TRUNCATE ... CASCADE sobre el contenedor
    Docker local 'visal-postgres' / base 'visal_dev'. Por seguridad NO acepta
    host remoto y se niega si el contenedor no es exactamente el local
    (evita borrar prod 10.0.0.4 o pruebas 10.0.0.3 por error). Tampoco toca la
    base 'visal_prod_copia'.

    Tablas que se vacian (via CASCADE desde asignacion_lotes + historias_clinicas):
      asignacion_lotes, asignaciones, asignacion_turnos, asignacion_turno_sesiones,
      asignacion_turno_sesion_hcs, historias_clinicas y todos sus hijos:
      historia_clinica_certificaciones / _documentos / _escalas / _incapacidades /
      _insumos / _medicamentos / _ordenes_servicio / _remisiones /
      _suministro_medicamentos, notas_medicas (+ nota_medica_documentos),
      ordenes_medicamentos_publicas, rda_eventos, revisiones_clinica
      (+ revision_clinica_eventos).

    Se CONSERVAN: pacientes, form_definitions, tenants, usuarios, catalogos, etc.

.PARAMETER Force
    Omite la confirmacion. Sin este flag, pide teclear SI para continuar.

.EXAMPLE
    .\Limpiar-LocalHistoriasAtenciones.ps1
.EXAMPLE
    .\Limpiar-LocalHistoriasAtenciones.ps1 -Force
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'

# --- Objetivo FIJO: solo local. No hay parametros de host remoto a proposito. ---
$Container = 'visal-postgres'   # dev local. NUNCA 'visal-postgres-prod'.
$Db        = 'visal_dev'
$User      = 'visal'

# Guarda 1: el contenedor debe existir y llamarse EXACTAMENTE 'visal-postgres'.
$found = docker ps --filter "name=^/$Container`$" --format '{{.Names}}'
if ($found -ne $Container) {
    Write-Host "ABORTADO: no encuentro el contenedor local '$Container' arriba." -ForegroundColor Red
    Write-Host "Este script es SOLO para local (visal_dev). No opera sobre prod ni pruebas." -ForegroundColor Red
    exit 1
}

function Get-Count([string]$sql) {
    return (docker exec $Container psql -U $User -d $Db -tA -c $sql).Trim()
}

Write-Host "== Antes (base $Db) ==" -ForegroundColor Cyan
Write-Host ("  historias_clinicas : {0}" -f (Get-Count "SELECT count(1) FROM historias_clinicas;"))
Write-Host ("  asignaciones       : {0}" -f (Get-Count "SELECT count(1) FROM asignaciones;"))
Write-Host ("  asignacion_turnos  : {0}" -f (Get-Count "SELECT count(1) FROM asignacion_turnos;"))
Write-Host ("  sesiones atendidas : {0}" -f (Get-Count "SELECT count(1) FROM asignacion_turno_sesiones;"))

if (-not $Force) {
    $r = Read-Host "Esto BORRA todas las historias/coordinaciones/atenciones de '$Db' (LOCAL). Escribe SI para continuar"
    if ($r -ne 'SI') { Write-Host "Cancelado." -ForegroundColor Yellow; exit 0 }
}

$sql = "TRUNCATE asignacion_lotes, historias_clinicas RESTART IDENTITY CASCADE;"
docker exec $Container psql -U $User -d $Db -v ON_ERROR_STOP=1 -c $sql

Write-Host "== Despues ==" -ForegroundColor Green
Write-Host ("  historias_clinicas : {0}" -f (Get-Count "SELECT count(1) FROM historias_clinicas;"))
Write-Host ("  asignaciones       : {0}" -f (Get-Count "SELECT count(1) FROM asignaciones;"))
Write-Host ("  asignacion_turnos  : {0}" -f (Get-Count "SELECT count(1) FROM asignacion_turnos;"))
Write-Host ("  sesiones atendidas : {0}" -f (Get-Count "SELECT count(1) FROM asignacion_turno_sesiones;"))
Write-Host "Listo. Base local limpia para pruebas (pacientes y formularios intactos)." -ForegroundColor Green
