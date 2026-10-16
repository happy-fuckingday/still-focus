param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet run --project tests/StillFocus.Tests -c Release
    if ($LASTEXITCODE) { throw 'Core tests failed.' }
    dotnet publish src/StillFocus.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/publish --nologo
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    $package = Join-Path $repoRoot "artifacts/StillFocus-$Version-win-x64"
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item -LiteralPath 'artifacts/publish/StillFocus.exe','README.md','LICENSE','CHANGELOG.md' -Destination $package
    Copy-Item -LiteralPath 'docs' -Destination $package -Recurse -Force
    # Include the license notices from the exact runtime packs used by this build.
    $runtimeConfig = Get-Content -LiteralPath 'src/StillFocus.App/bin/Release/net10.0-windows/win-x64/StillFocus.runtimeconfig.json' -Raw | ConvertFrom-Json
    $assets = Get-Content -LiteralPath 'src/StillFocus.App/obj/project.assets.json' -Raw | ConvertFrom-Json
    $licenses = New-Item -ItemType Directory -Path (Join-Path $package 'licenses') -Force
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $packName = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
        $packPath = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "$packName/$($framework.version)" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$packPath) { throw "Runtime license pack missing: $packName" }
        foreach ($notice in Get-ChildItem -LiteralPath $packPath -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' }) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses.FullName "$($framework.name)-$($notice.Name)")
        }
    }
    $zip = "$package.zip"
    Compress-Archive -Path "$package/*" -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $zip)" | Set-Content -LiteralPath "$zip.sha256" -Encoding utf8NoBOM
    Write-Output "Created $zip"
}
finally { Pop-Location }
