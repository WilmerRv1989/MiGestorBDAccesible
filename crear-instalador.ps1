$ErrorActionPreference = "Stop"

$salida = Join-Path $PSScriptRoot "salida"
$publicado = Join-Path $salida "publicado"
$proyecto = Join-Path $PSScriptRoot "MiGestorBDAccesible\MiGestorBDAccesible.csproj"
$herramientas = Join-Path $PSScriptRoot "MiGestorBDAccesible.Herramientas\MiGestorBDAccesible.Herramientas.csproj"
$script = Join-Path $PSScriptRoot "instalador\MiGestorBDAccesible.nsi"
$makensis = Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"

if (-not (Test-Path $makensis)) { throw "No se encontró NSIS. Instálalo con: winget install NSIS.NSIS" }

[xml]$propiedades = Get-Content (Join-Path $PSScriptRoot "Directory.Build.props")
$version = $propiedades.Project.PropertyGroup.Version
$partes = $version.Split(".")
$versionCorta = if ($partes[2] -eq "0") { "$($partes[0]).$($partes[1])" } else { $version }
$instalador = Join-Path $salida "MiGestorBDAccesible-$versionCorta-instalador.exe"

if (Test-Path $publicado) { Remove-Item $publicado -Recurse -Force }

dotnet publish $proyecto -c Release -r win-x64 --self-contained true -o $publicado --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "La publicación falló." }

dotnet run --project $herramientas -c Release -- exportar-manual (Join-Path $publicado "Manual.html")
if ($LASTEXITCODE -ne 0) { throw "No se pudo exportar el manual." }

& $makensis /V2 "/DVERSION=$version" "/DVERSION_CORTA=$versionCorta" "/DFUENTE=$publicado" "/DSALIDA=$instalador" $script
if ($LASTEXITCODE -ne 0) { throw "NSIS no pudo crear el instalador." }

$manual = Join-Path $salida "MiGestorBDAccesible-$versionCorta-manual.html"
Copy-Item (Join-Path $publicado "Manual.html") $manual -Force

$tamano = [math]::Round((Get-Item $instalador).Length / 1MB, 1)
Write-Output "Instalador creado: $instalador ($tamano MB)"
Write-Output "Manual para compartir: $manual"
