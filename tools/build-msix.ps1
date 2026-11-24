# Builds Hearwood's Microsoft Store package: one MSIX per architecture with the model inside, bundled into
# dist/msix/Hearwood_<version>.msixbundle for upload to Partner Center. Needs the Windows SDK (makeappx, makepri),
# ptts_ffi.dll for each architecture (tools/build-phonon.ps1), and the converted model, which Hearwood writes to
# %APPDATA%\Hearwood the first time it runs.
#   pwsh tools/build-msix.ps1 -Version 1.0.0.0
# The identity defaults are Hearwood's, from Partner Center's Product identity page; the Store rejects any other.
# -CertificateThumbprint signs the packages, so a test build can be installed; the Store signs uploads itself.
param(
    [string]$Version = '1.0.0.0',
    [string]$IdentityName = 'IdleMadStudio.Hearwood',
    [string]$Publisher = 'CN=94F6BC86-F44E-4204-8EFB-CE9EC19AC167',
    [string]$PublisherDisplayName = 'IdleMad Studio',
    [ValidateSet('x64', 'arm64')][string[]]$Arch = @('x64', 'arm64'),
    # A code-signing certificate in Cert:\CurrentUser\My whose subject is $Publisher, for installing a test build.
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'dist\msix'
$work = Join-Path $root 'obj\msix'

# The Store requires the last part to be 0; it is reserved for its own use.
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must look like 1.2.3.0 (the last part must be 0), not '$Version'." }

# The newest SDK that has the tools.
$sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64' } |
    Where-Object { (Test-Path (Join-Path $_ 'makeappx.exe')) -and (Test-Path (Join-Path $_ 'makepri.exe')) } |
    Select-Object -First 1
if (-not $sdkBin) { throw 'The Windows SDK (makeappx.exe, makepri.exe) is not installed.' }
$makeappx = Join-Path $sdkBin 'makeappx.exe'
$makepri = Join-Path $sdkBin 'makepri.exe'
$signtool = Join-Path $sdkBin 'signtool.exe'

# The converted model, pinned so a package can only ever carry the files Hearwood was tested with. The tokenizer's
# hash is the download's (PocketSetup.Tokenizer); the others are what PocketSetup and PocketCheckpoint write from it.
$checkpoint = 'kyutai-english_2026-10'
$model = Join-Path $env:APPDATA "Hearwood\pocket\$checkpoint"
$pinned = [ordered]@{
    'config.json'          = 'c4d097a086b035578c02227c190fe7d63e76fb26816ee818e7a1f82c76fa9291'
    'tokenizer.json'       = 'f498428e1eafee50492f7be13dc9bfafcfc12e508cd0eb1b01c92ecd5d8c6687'
    'model.safetensors'    = 'b1d64c2c9eaf450d16534324be510991c9fb120208e35db96bb4ee01c2abc7ab'
    'bos_before_voice.npy' = '543dec1a62dc0d1dbebe6549af984304f6da8264c94c8b1748f4923e644730d0'
}
foreach ($file in $pinned.Keys) {
    $path = Join-Path $model $file
    if (-not (Test-Path $path)) { throw "The model file '$path' is missing. Run Hearwood once so it downloads the model, then try again." }
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $pinned[$file]) { throw "'$path' is not the pinned file. Delete '$model', run Hearwood once, and try again." }
}

# Runs a tool quietly; its output is shown only when it fails.
function Invoke-Tool([string]$exe, [string[]]$arguments) {
    $output = & $exe @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Out-Host
        throw "$(Split-Path $exe -Leaf) failed (exit code $LASTEXITCODE)."
    }
}

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
$packages = Join-Path $work 'packages'
New-Item -ItemType Directory -Force $packages | Out-Null
$manifest = Get-Content (Join-Path $root 'packaging\AppxManifest.xml') -Raw

foreach ($a in $Arch) {
    if (-not (Test-Path (Join-Path $root "src\Hearwood\native\$a\ptts_ffi.dll"))) { throw "ptts_ffi.dll for $a is missing. Run: pwsh tools/build-phonon.ps1 -Arch $a" }

    Write-Host "Publishing $a..."
    $layout = Join-Path $work "layout-$a"
    Invoke-Tool 'dotnet' @('publish', (Join-Path $root 'src\Hearwood'), '-c', 'Release', '-r', "win-$a", '--self-contained', 'true', '-o', $layout, '-nologo', '-v', 'q')
    Get-ChildItem $layout -Filter *.pdb | Remove-Item

    # The model, ready to use and read-only (PocketService.BundledCheckpointDir), with the voices where ptts reads them.
    $bundled = Join-Path $layout "pocket\$checkpoint"
    New-Item -ItemType Directory -Force (Join-Path $bundled 'voices') | Out-Null
    foreach ($file in $pinned.Keys) { Copy-Item (Join-Path $model $file) $bundled }
    Copy-Item (Join-Path $root 'src\Hearwood\assets\voices\*.safetensors') (Join-Path $bundled 'voices')

    # Into the app's own assets folder (the voices): Windows paths ignore case, so 'Assets' is the same folder.
    Copy-Item (Join-Path $root 'packaging\Assets\*.png') (Join-Path $layout 'assets')
    $filled = $manifest.Replace('$IdentityName$', $IdentityName).Replace('$Publisher$', $Publisher)
    $filled = $filled.Replace('$PublisherDisplayName$', $PublisherDisplayName).Replace('$Version$', $Version).Replace('$Arch$', $a)
    Set-Content (Join-Path $layout 'AppxManifest.xml') $filled -Encoding utf8NoBOM -NoNewline

    # resources.pri maps Assets\Square44x44Logo.png and friends onto their scale and target-size variants.
    $resFiles = Join-Path $work "logos-$a.resfiles"
    Get-ChildItem (Join-Path $root 'packaging\Assets\*.png') | ForEach-Object { "Assets\$($_.Name)" } | Set-Content $resFiles -Encoding utf8NoBOM
    $priConfig = Join-Path $work "priconfig-$a.xml"
    (Get-Content (Join-Path $root 'packaging\priconfig.xml') -Raw).Replace('$ResFiles$', $resFiles) | Set-Content $priConfig -Encoding utf8NoBOM -NoNewline
    Invoke-Tool $makepri @('new', '/pr', $layout, '/cf', $priConfig, '/mn', (Join-Path $layout 'AppxManifest.xml'), '/of', (Join-Path $layout 'resources.pri'), '/o')

    $msix = Join-Path $packages "Hearwood_${Version}_$a.msix"
    Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', $msix, '/o')
    if ($CertificateThumbprint) { Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint, $msix) }
}

$bundle = Join-Path $out "Hearwood_$Version.msixbundle"
Invoke-Tool $makeappx @('bundle', '/d', $packages, '/p', $bundle, '/bv', $Version, '/o')
if ($CertificateThumbprint) { Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint, $bundle) }
Write-Host "Built $bundle"
