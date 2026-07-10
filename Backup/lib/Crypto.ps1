#requires -Version 5.1
# =============================================================================
#  Crypto.ps1  -  Helper de cifrado AES-256-CBC + PBKDF2 para proteger el .env
#                 dentro del ZIP de backup.
#
#  Uso:
#     . "$PSScriptRoot\lib\Crypto.ps1"
#     Protect-FileWithAes -InputPath ".env" -OutputPath ".env.aes" -Password $sec
#     Unprotect-FileWithAes -InputPath ".env.aes" -OutputPath ".env" -Password $sec
#
#  Formato del archivo cifrado (binario, single file):
#     [16 bytes salt] [16 bytes IV] [ciphertext...]
#  KDF: PBKDF2-HMAC-SHA256, 200_000 iteraciones. Key 32 bytes (AES-256).
# =============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-SecureStringToBytes {
    param([Parameter(Mandatory)][securestring]$Password)
    # Extrae los bytes UTF-8 de la SecureString sin dejar el plaintext en un
    # string manejado por el GC. El BSTR se libera en el finally.
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        return [System.Text.Encoding]::UTF8.GetBytes($plain)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    }
}

function Protect-FileWithAes {
    param(
        [Parameter(Mandatory)][string]$InputPath,
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][securestring]$Password
    )
    if (-not (Test-Path $InputPath)) { throw "No existe el archivo a cifrar: $InputPath" }

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $salt = New-Object byte[] 16
    $iv   = New-Object byte[] 16
    $rng.GetBytes($salt)
    $rng.GetBytes($iv)

    $pwdBytes = $null
    $key = $null
    try {
        $pwdBytes = Convert-SecureStringToBytes -Password $Password
        $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes -ArgumentList $pwdBytes, $salt, 200000, ([System.Security.Cryptography.HashAlgorithmName]::SHA256)
        $key = $kdf.GetBytes(32)

        $aes = [System.Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256
        $aes.BlockSize = 128
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $key
        $aes.IV = $iv

        $plaintext = [System.IO.File]::ReadAllBytes($InputPath)
        $encryptor = $aes.CreateEncryptor()
        try {
            $cipher = $encryptor.TransformFinalBlock($plaintext, 0, $plaintext.Length)
        }
        finally { $encryptor.Dispose() }

        $out = New-Object System.IO.FileStream($OutputPath, [System.IO.FileMode]::Create)
        try {
            $out.Write($salt, 0, $salt.Length)
            $out.Write($iv,   0, $iv.Length)
            $out.Write($cipher, 0, $cipher.Length)
        }
        finally { $out.Dispose() }

        $aes.Dispose()
    }
    finally {
        if ($null -ne $pwdBytes) { [Array]::Clear($pwdBytes, 0, $pwdBytes.Length) }
        if ($null -ne $key)      { [Array]::Clear($key, 0, $key.Length) }
    }
}

function Unprotect-FileWithAes {
    param(
        [Parameter(Mandatory)][string]$InputPath,
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][securestring]$Password
    )
    if (-not (Test-Path $InputPath)) { throw "No existe el archivo a descifrar: $InputPath" }

    $blob = [System.IO.File]::ReadAllBytes($InputPath)
    if ($blob.Length -lt 32) { throw "Archivo cifrado corrupto o vacio: $InputPath" }

    $salt = New-Object byte[] 16
    $iv   = New-Object byte[] 16
    $cipher = New-Object byte[] ($blob.Length - 32)
    [Array]::Copy($blob, 0,  $salt,   0, 16)
    [Array]::Copy($blob, 16, $iv,     0, 16)
    [Array]::Copy($blob, 32, $cipher, 0, $cipher.Length)

    $pwdBytes = $null
    $key = $null
    try {
        $pwdBytes = Convert-SecureStringToBytes -Password $Password
        $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes -ArgumentList $pwdBytes, $salt, 200000, ([System.Security.Cryptography.HashAlgorithmName]::SHA256)
        $key = $kdf.GetBytes(32)

        $aes = [System.Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256
        $aes.BlockSize = 128
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $key
        $aes.IV = $iv

        $decryptor = $aes.CreateDecryptor()
        try {
            $plain = $decryptor.TransformFinalBlock($cipher, 0, $cipher.Length)
        }
        finally { $decryptor.Dispose() }

        [System.IO.File]::WriteAllBytes($OutputPath, $plain)
        $aes.Dispose()
    }
    finally {
        if ($null -ne $pwdBytes) { [Array]::Clear($pwdBytes, 0, $pwdBytes.Length) }
        if ($null -ne $key)      { [Array]::Clear($key, 0, $key.Length) }
    }
}
