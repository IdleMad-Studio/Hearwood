# Builds the Phonon runtime library (xn-ptts) that runs Pocket TTS. Needs Rust and the MSVC build tools. Run once per architecture, before dotnet publish:
#   pwsh tools/build-phonon.ps1 -Arch x64
# The DLL lands in src/Hearwood/native/<arch> (not committed), and every build copies it beside the app.
# Taken from another project by the same author.
param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch
)

$ErrorActionPreference = 'Stop'

# Pinned: the engine is written against this commit's ptts.h, which changes between releases.
# main after v0.6.0 (2026-11-04): adds ptts_set_seed/ptts_set_temperature, which no release has yet.
$commit = 'ee395caac058950cbcabfa3785c63630942f31ed'
$rust = if ($Arch -eq 'arm64') { 'aarch64-pc-windows-msvc' } else { 'x86_64-pc-windows-msvc' }
# The repo's own default builds for the build machine's CPU, which can crash on other PCs. These are its release baselines.
$cpu = if ($Arch -eq 'arm64') { 'generic' } else { 'x86-64-v3' }

$target = Join-Path $PSScriptRoot "..\src\Hearwood\native\$Arch"
$work = Join-Path ([IO.Path]::GetTempPath()) "hearwood-phonon-$([Guid]::NewGuid().ToString('N'))"

try {
    git init --quiet $work
    git -C $work fetch --quiet --depth 1 https://github.com/gradium-ai/xn-ptts.git $commit
    if ($LASTEXITCODE -ne 0) { throw 'Could not download the Phonon source.' }
    git -C $work checkout --quiet FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out the Phonon source.' }

    # Pocket's tail: pocket-tts generates its frames-after-EOS guess + 2 frames from the EOS frame on,
    # ptts one fewer, which cuts off the end of the last word. One more frame after EOS matches pocket-tts.
    # The library serves Pocket only, so every utterance gets it. Read and written as UTF-8 without a BOM.
    $plan = Join-Path $work 'ptts\src\plan.rs'
    $source = [IO.File]::ReadAllText($plan)
    $line = 'Self { frames_after_eos, countdown: None }'
    if (([regex]::Matches($source, [regex]::Escape($line))).Count -ne 1) { throw "plan.rs no longer has '$line'; check the Pocket tail change against this commit." }
    [IO.File]::WriteAllText($plan, $source.Replace($line, 'Self { frames_after_eos: frames_after_eos + 1, countdown: None }'), [Text.UTF8Encoding]::new($false))

    rustup target add $rust
    if ($LASTEXITCODE -ne 0) { throw "Could not add the Rust target $rust." }

    $env:RUSTFLAGS = "-C target-cpu=$cpu"
    Push-Location $work
    try {
        cargo build --release -p ptts-ffi --target $rust
        if ($LASTEXITCODE -ne 0) { throw 'The Phonon build failed.' }
    }
    finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $work "target\$rust\release\ptts_ffi.dll") $target -Force
    Copy-Item (Join-Path $work 'LICENSE-MIT') (Join-Path $target 'ptts_ffi-LICENSE.txt') -Force
    Write-Host "Phonon library copied to $target"
}
finally {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}
