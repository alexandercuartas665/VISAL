# Audit-Diagnosticos-HC.ps1
# Read-only. Recorre cada HC objetivo y localiza en el schema los CAMPOS
# de diagnostico (por name/label que contenga CIE, DIAGNOSTICO, ORIGEN,
# TIPO, RELACION). Reporta path y posicion.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

$codigos = @("HC-FO-13","HC-FO-14","HC-FO-15","HC-FO-16","HC-FO-18",
             "HC-FO-19","HC-FO-20","HC-FO-21","HC-FO-22","HC-FO-25")

foreach ($cod in $codigos) {
    Write-Host ""
    Write-Host ("========== {0} ==========" -f $cod) -ForegroundColor Cyan
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $raw | ConvertFrom-Json -AsHashtable
    Write-Host "  Top-level:"
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        $sec = $schema.children[$i]
        $hijos = if ($sec.children) { $sec.children.Count } else { 0 }
        Write-Host ("    [{0}] {1} (hijos={2})" -f $i, $sec.label, $hijos)
    }
    # Buscar dentro de cada seccion los campos con names/labels sospechosos de diagnostico
    Write-Host "  Campos de diagnostico encontrados:"
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        $sec = $schema.children[$i]
        if (-not $sec.children) { continue }
        for ($j=0; $j -lt $sec.children.Count; $j++) {
            $c = $sec.children[$j]
            $lbl = ([string]$c.label).ToUpper()
            $nm = ([string]$c.name).ToLower()
            if ($lbl -match "^(CIE|DIAGN|ORIGEN|TIPO|RELAC)" -or $nm -match "^(cie|diagn|origen|tipo|relac|d_x)" ) {
                Write-Host ("    [{0}].[{1}] type={2} ft={3} label='{4}' name={5}" -f $i, $j, $c.type, $c.fieldType, $c.label, $c.name) -ForegroundColor Yellow
            }
            # tambien detectar si ya hay una tabla llamada diagnosticos
            if ($c.type -eq "field" -and $c.fieldType -eq "table" -and $nm -match "diagn") {
                Write-Host ("    [{0}].[{1}] YA ES TABLA type=field/table name={2}" -f $i, $j, $c.name) -ForegroundColor Green
            }
        }
    }
}
