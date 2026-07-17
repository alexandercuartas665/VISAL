<#
.SYNOPSIS
    Wrapper que dispara los dos flujos de respaldo diario:
      1. Bases de datos de contenedores Docker LOCALES  -> D:\Backups\Docker
      2. Base de datos de PRODUCCION Visal (via SSH)    -> D:\Backups\Produccion\Visal

.DESCRIPTION
    Los dos scripts corren en secuencia. Si el primero falla, igual se intenta
    el segundo (los backups son independientes). El codigo de salida es 0 solo
    si ambos terminaron OK.

    Este es el script que llama la tarea programada de Windows
    "Visal - Backup Docker Databases".
#>

[CmdletBinding()]
param()

$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$results = @()
$exitCode = 0

function Run-Step {
    param([string]$Nombre, [string]$Script)
    Write-Host ""
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host "==> $Nombre" -ForegroundColor Cyan
    Write-Host "======================================================================" -ForegroundColor Cyan
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script
        if ($LASTEXITCODE -eq 0) {
            $script:results += [pscustomobject]@{ Paso=$Nombre; Estado='OK'; Codigo=0 }
        } else {
            $script:results += [pscustomobject]@{ Paso=$Nombre; Estado='ERROR'; Codigo=$LASTEXITCODE }
            $script:exitCode = 1
        }
    } catch {
        $script:results += [pscustomobject]@{ Paso=$Nombre; Estado='EXCEPCION'; Codigo=$_.Exception.Message }
        $script:exitCode = 1
    }
}

Run-Step "Backup Docker locales"      (Join-Path $here "Backup-DockerDatabases.ps1")
Run-Step "Backup Produccion Visal"    (Join-Path $here "Backup-VisalProdDatabase.ps1")

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "==> RESUMEN" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host

exit $exitCode
