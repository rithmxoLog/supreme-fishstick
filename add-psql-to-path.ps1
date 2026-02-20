# add-psql-to-path.ps1
# Adds PostgreSQL to the system PATH and sets PostgreSQL environment variables.
# Must be run as Administrator.

$pgBin      = "C:\Program Files\PostgreSQL\18\bin"
$pgData     = "C:\Program Files\PostgreSQL\18\data"
$pgUser     = "postgres"
$pgDatabase = "postgres"

# ── Admin check ───────────────────────────────────────────────────────────────
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Please run this script as Administrator (right-click PowerShell > Run as administrator)."
    exit 1
}

# ── Verify install path ───────────────────────────────────────────────────────
if (-not (Test-Path "$pgBin\psql.exe")) {
    Write-Error "psql.exe not found at: $pgBin\psql.exe"
    Write-Error "Edit the `$pgBin variable at the top of this script to match your install path."
    exit 1
}

# ── 1. Add bin dir to system PATH ─────────────────────────────────────────────
$currentPath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
$pathEntries = $currentPath -split ";"

if ($pathEntries | Where-Object { $_.TrimEnd("\") -eq $pgBin.TrimEnd("\") }) {
    Write-Host "[PATH] PostgreSQL bin is already in the system PATH." -ForegroundColor Yellow
} else {
    $newPath = $currentPath.TrimEnd(";") + ";" + $pgBin
    [System.Environment]::SetEnvironmentVariable("Path", $newPath, "Machine")
    Write-Host "[PATH] Added '$pgBin' to system PATH." -ForegroundColor Green
}

# ── 2. Set PostgreSQL environment variables (system-wide) ─────────────────────
$envVars = @{
    "PGDATA"     = $pgData      # Default data directory
    "PGUSER"     = $pgUser      # Default login user (avoids needing -U postgres)
    "PGDATABASE" = $pgDatabase  # Default database
}

foreach ($kv in $envVars.GetEnumerator()) {
    $existing = [System.Environment]::GetEnvironmentVariable($kv.Key, "Machine")
    if ($existing -eq $kv.Value) {
        Write-Host "[ENV]  $($kv.Key) already set to '$($kv.Value)'." -ForegroundColor Yellow
    } else {
        [System.Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, "Machine")
        Write-Host "[ENV]  Set $($kv.Key) = '$($kv.Value)'." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Done! Open a new terminal and run:" -ForegroundColor Cyan
Write-Host "  psql --version" -ForegroundColor White
Write-Host "  psql -d gitxo     (connects as postgres user to gitxo db)" -ForegroundColor White
