# Convert-DocxToPdfWord.ps1
# Convierte cada .docx en el directorio CONSENTIMIENTOS a PDF usando
# Word COM. Ubica el PDF junto al docx (misma carpeta). Skip si el PDF
# ya existe.

[CmdletBinding()]
param(
    [string]$DirDocx = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\05. Formatos\CONSENTIMIESNTOS"
)
$ErrorActionPreference = "Stop"

$docxs = Get-ChildItem -Path $DirDocx -Filter *.docx -File
Write-Host ("Encontrados {0} docx" -f $docxs.Count) -ForegroundColor Cyan

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0  # wdAlertsNone

$wdFormatPDF = 17

try {
    foreach ($doc in $docxs) {
        $pdfPath = [System.IO.Path]::ChangeExtension($doc.FullName, ".pdf")
        if (Test-Path $pdfPath) {
            Write-Host ("  {0}: PDF ya existe, skip" -f $doc.Name) -ForegroundColor DarkGray
            continue
        }
        Write-Host ("  {0}: convirtiendo..." -f $doc.Name) -ForegroundColor Yellow
        $wDoc = $word.Documents.Open($doc.FullName, [ref]$false, [ref]$true)
        try {
            $wDoc.SaveAs2($pdfPath, [ref]$wdFormatPDF)
        } finally {
            $wDoc.Close([ref]$false)
        }
        Write-Host ("  {0}: PDF OK" -f $doc.Name) -ForegroundColor Green
    }
} finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}
Write-Host "Word cerrado." -ForegroundColor Cyan
