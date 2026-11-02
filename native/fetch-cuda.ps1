<#
.SYNOPSIS
    Fetches NVIDIA's CUDA 12.8.1 runtime DLLs into native\cuda12, verified against NVIDIA's
    published SHA-256 hashes.

.DESCRIPTION
    The CUDA edition of Murmur redistributes three files from NVIDIA's official redistributable
    archives (developer.download.nvidia.com/compute/cuda/redist). They are not committed: the
    archives are ~540 MB and the DLLs ~750 MB. This script is how they get onto a build
    machine, and the pinned hashes are why the result can be trusted.

    Redistribution is permitted by the CUDA Toolkit EULA, Attachment A ("CUDA Runtime" and
    "CUDA BLAS Library"). NVIDIA's license text is extracted alongside as NVIDIA-LICENSE.txt
    and ships inside the CUDA edition.

.PARAMETER ArchiveDirectory
    Where the downloaded archives are kept. Archives already present with the right hash are
    reused rather than downloaded again.
#>
param(
    [string]$ArchiveDirectory = (Join-Path $env:TEMP 'murmur-cuda-redist')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$base = 'https://developer.download.nvidia.com/compute/cuda/redist'

# From redistrib_12.8.1.json. Bumping CUDA means updating these from the matching manifest.
$archives = @(
    @{
        Name   = 'cuda_cudart-windows-x86_64-12.8.90-archive.zip'
        Url    = "$base/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.8.90-archive.zip"
        Sha256 = '4a39058fd8519444a81cfc7ae055d136f48d1a31ffa41ae255b35b2edd61e13b'
        Files  = @{ 'bin/cudart64_12.dll' = 'cudart64_12.dll'; 'LICENSE' = 'NVIDIA-LICENSE.txt' }
    },
    @{
        Name   = 'libcublas-windows-x86_64-12.8.4.1-archive.zip'
        Url    = "$base/libcublas/windows-x86_64/libcublas-windows-x86_64-12.8.4.1-archive.zip"
        Sha256 = '57a470112cec7e112c95253dde8b3c7184d795dbd92b0bde77a4cb7f8c94c8aa'
        Files  = @{ 'bin/cublas64_12.dll' = 'cublas64_12.dll'; 'bin/cublasLt64_12.dll' = 'cublasLt64_12.dll' }
    }
)

$target = Join-Path $PSScriptRoot 'cuda12'
New-Item -ItemType Directory -Force $ArchiveDirectory, $target | Out-Null

foreach ($archive in $archives) {
    $path = Join-Path $ArchiveDirectory $archive.Name

    $cached = (Test-Path $path) -and ((Get-FileHash $path -Algorithm SHA256).Hash -eq $archive.Sha256)
    if (-not $cached) {
        Write-Host "Downloading $($archive.Name)..."
        Invoke-WebRequest -Uri $archive.Url -OutFile $path -UseBasicParsing
    }

    $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $archive.Sha256) {
        Remove-Item $path -Force
        throw "$($archive.Name) failed verification: sha256 $actual, expected $($archive.Sha256). Deleted."
    }
    Write-Host "Verified $($archive.Name)"

    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        foreach ($entryPath in $archive.Files.Keys) {
            # Entries sit under a top-level folder named after the archive.
            $entry = $zip.Entries | Where-Object { $_.FullName -like "*/$entryPath" } | Select-Object -First 1
            if (-not $entry) { throw "$($archive.Name) has no $entryPath" }

            $destination = Join-Path $target $archive.Files[$entryPath]
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
            Write-Host "  -> native\cuda12\$($archive.Files[$entryPath])"
        }
    }
    finally {
        $zip.Dispose()
    }
}

Write-Host 'Done. native\cuda12 now holds NVIDIA''s verified CUDA 12.8.1 runtime.'
