param(
    [Parameter(Mandatory = $true)]
    [string] $GameRoot
)

$ErrorActionPreference = "Stop"

function Fail([string] $Message) {
    throw "TajsBootstrap disable refused: $Message"
}

try {
    $root = [IO.Path]::GetFullPath($GameRoot.Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar)
}
catch {
    Fail "game root is invalid: $($_.Exception.Message)"
}

if (-not [IO.Directory]::Exists($root)) {
    Fail "game root does not exist: $root"
}

$exe = Join-Path $root "Captain of Industry.exe"
$managed = Join-Path $root "Captain of Industry_Data\Managed"
if (-not [IO.File]::Exists($exe) -and -not [IO.Directory]::Exists($managed)) {
    Fail "path is not a Captain of Industry root: $root"
}

$running = Get-CimInstance Win32_Process -Filter "Name='Captain of Industry.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and [string]::Equals(
        [IO.Path]::GetFullPath($_.ExecutablePath).TrimEnd([IO.Path]::DirectorySeparatorChar),
        $exe,
        [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Fail "Captain of Industry is running from this root; close it before changing the bootstrap manifest"
}

$manifestPath = Join-Path $root "TajsCOI\TajsBootstrap.install.json"
if (-not [IO.File]::Exists($manifestPath)) {
    Write-Output "No TajsBootstrap install manifest exists; nothing was changed."
    exit 0
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    Fail "install manifest is unreadable: $($_.Exception.Message)"
}

if ($null -eq $manifest -or $manifest.Schema -ne 1 -or [string]::IsNullOrWhiteSpace([string] $manifest.GameRoot)) {
    Fail "install manifest is incomplete or uses an unsupported schema"
}

try {
    $manifestRoot = [IO.Path]::GetFullPath(([string] $manifest.GameRoot).Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar)
}
catch {
    Fail "install manifest game root is invalid: $($_.Exception.Message)"
}
if (-not [string]::Equals($manifestRoot, $root, [StringComparison]::OrdinalIgnoreCase)) {
    Fail "install manifest belongs to another game root"
}

$expectedFiles = @(
    "TajsCOI\Bootstrap\TajsBootstrap.dll",
    "TajsCOI\Bootstrap\0Harmony.dll",
    "winhttp.dll",
    "doorstop_config.ini"
)
$legacyExpectedFiles = @(
    "TajsCOI\Bootstrap\TajsBootstrap.dll",
    "TajsCOI\Bootstrap\0Harmony.dll"
)
$records = @($manifest.Files)
$recordNames = @($records | ForEach-Object { ([string] $_.RelativePath).Replace('/', '\') } | Sort-Object)
$acceptedFileSets = @(
    ,(@($expectedFiles | Sort-Object))
    ,(@($legacyExpectedFiles | Sort-Object))
)
$matchesExpectedSet = $false
foreach ($acceptedFileSet in $acceptedFileSets) {
    if (($recordNames -join '|') -ceq ($acceptedFileSet -join '|')) {
        $matchesExpectedSet = $true
        break
    }
}
if (-not $matchesExpectedSet) {
    Fail "install manifest does not describe the Tajs-owned bootstrap payload"
}
foreach ($record in $records) {
    $relative = ([string] $record.RelativePath).Replace('/', '\')
    if (($expectedFiles -notcontains $relative -and $legacyExpectedFiles -notcontains $relative) -or
        [string]::IsNullOrWhiteSpace([string] $record.Sha256)) {
        Fail "install manifest contains an unknown or incomplete payload record"
    }
}

if (-not [bool] $manifest.Enabled) {
    Write-Output "TajsBootstrap is already disabled; no files were changed."
    exit 0
}

$manifest.Enabled = $false
$directory = [IO.Path]::GetDirectoryName($manifestPath)
$temporary = Join-Path $directory ([IO.Path]::GetFileName($manifestPath) + ".tmp-" + [Guid]::NewGuid().ToString("N"))
$backup = $manifestPath + ".bak-" + [Guid]::NewGuid().ToString("N")
try {
    $json = $manifest | ConvertTo-Json -Depth 8
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($temporary, $json, $utf8)
    try {
        [IO.File]::Replace($temporary, $manifestPath, $backup, $true)
    }
    catch [PlatformNotSupportedException] {
        [IO.File]::Delete($manifestPath)
        [IO.File]::Move($temporary, $manifestPath)
    }
    Write-Output "TajsBootstrap disabled in $manifestPath. Owned payload and Doorstop files remain in place; version.dll was not touched."
}
catch {
    Fail "manifest could not be updated: $($_.Exception.Message)"
}
finally {
    if ([IO.File]::Exists($temporary)) {
        [IO.File]::Delete($temporary)
    }
    if ([IO.File]::Exists($backup)) {
        [IO.File]::Delete($backup)
    }
}
