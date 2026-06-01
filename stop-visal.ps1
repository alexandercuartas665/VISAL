# =========================================================================
#  stop-visal.ps1 - detiene la instancia de Visal en background.
# =========================================================================

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$pidFile = Join-Path $repo ".visal.pid"

function Info($m) { Write-Host "    $m" -ForegroundColor Green }
function Warn($m) { Write-Host "    $m" -ForegroundColor Yellow }

if (Test-Path $pidFile) {
    $pid = Get-Content $pidFile | Select-Object -First 1
    $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
    if ($proc) {
        $proc | Stop-Process -Force
        Info "Visal detenido (PID $pid)."
    } else {
        Warn "PID $pid registrado pero el proceso ya no existe."
    }
    Remove-Item $pidFile -Force
} else {
    Warn "No hay .visal.pid - buscando procesos huerfanos..."
}

# Matar cualquier Visal.SuperAdmin huerfano
Get-Process -Name "Visal.SuperAdmin" -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force; Info "Matado huerfano PID $($_.Id)." }

Start-Sleep -Milliseconds 500
Info "Listo."
