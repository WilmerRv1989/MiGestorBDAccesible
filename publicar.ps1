$ErrorActionPreference = "Stop"

$destino = Join-Path $env:LOCALAPPDATA "Programs\MiGestorBDAccesible"
$proyecto = Join-Path $PSScriptRoot "MiGestorBDAccesible\MiGestorBDAccesible.csproj"

Get-Process MiGestorBDAccesible -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$destino*" } |
    Stop-Process

dotnet publish $proyecto -c Release -o $destino --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "La publicación falló." }

$accesoDirecto = Join-Path ([Environment]::GetFolderPath("Programs")) "Mi Gestor BD Accesible.lnk"
$shell = New-Object -ComObject WScript.Shell
$enlace = $shell.CreateShortcut($accesoDirecto)
$enlace.TargetPath = Join-Path $destino "MiGestorBDAccesible.exe"
$enlace.WorkingDirectory = $destino
$enlace.Description = "Gestor de bases de datos accesible"
$enlace.Save()

Write-Output "Publicado en $destino"
Write-Output "Acceso directo: $accesoDirecto"
