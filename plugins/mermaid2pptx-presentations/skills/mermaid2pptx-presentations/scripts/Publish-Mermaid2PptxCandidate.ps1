[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$Destination,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$candidatePath = [IO.Path]::GetFullPath($Candidate)
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
    throw "Publication candidate does not exist: $candidatePath"
}

$candidateDirectory = Split-Path -Path $candidatePath -Parent
$destinationDirectory = Split-Path -Path $destinationPath -Parent
if (-not $candidateDirectory.Equals(
    $destinationDirectory,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "Atomic publication requires the candidate beside the destination."
}

function Invoke-AtomicReplacement {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Target
    )

    $backupName = ".{0}.{1}.rollback" -f @(
        [IO.Path]::GetFileName($Target),
        [guid]::NewGuid().ToString("N")
    )
    $backupPath = Join-Path (
        Split-Path -Path $Target -Parent
    ) $backupName

    try {
        [IO.File]::Replace($Source, $Target, $backupPath)
    }
    catch {
        if (-not (Test-Path -LiteralPath $Target -PathType Leaf) -and
            (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            [IO.File]::Move($backupPath, $Target, $false)
        }
        throw [InvalidOperationException]::new(
            "Failed to atomically replace existing output '$Target'.",
            $_.Exception
        )
    }

    if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
        try {
            Remove-Item -LiteralPath $backupPath -Force
        }
        catch {
            Write-Warning "Published output, but could not remove rollback backup '$backupPath'."
        }
    }
}

if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
    if (-not $Force) {
        throw "Output appeared before publication and cannot be replaced without -Force: $destinationPath"
    }
    Invoke-AtomicReplacement `
        -Source $candidatePath `
        -Target $destinationPath
}
else {
    try {
        [IO.File]::Move($candidatePath, $destinationPath, $false)
    }
    catch [IO.IOException] {
        if ($Force -and
            (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
            Invoke-AtomicReplacement `
                -Source $candidatePath `
                -Target $destinationPath
        }
        else {
            throw [InvalidOperationException]::new(
                "Failed to publish candidate without overwriting '$destinationPath'.",
                $_.Exception
            )
        }
    }
}

$destinationPath
