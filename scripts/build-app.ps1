param([string]$Configuration='Release',[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=Join-Path $root 'artifacts'
$package=Join-Path $out 'app'
$app=Join-Path $package 'GP200_Custom_Editor'
New-Item -ItemType Directory -Path $out -Force | Out-Null
if(Test-Path -LiteralPath $package){Remove-Item -LiteralPath $package -Recurse -Force}
New-Item -ItemType Directory -Path $app -Force | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $out 'cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$project=Join-Path $root 'Editor/Gp200.Replica.csproj'
$packages=Join-Path $out 'nuget'
$runtime='win-x64'
$tfm=(Select-String -Path $project -Pattern '<TargetFramework>net(\d+)\.0-windows</TargetFramework>').Matches.Groups[1].Value
if(!$tfm){throw 'Could not determine the target .NET version from the project'}
& $Dotnet restore $project --configfile (Join-Path $root 'NuGet.Config') --source 'https://api.nuget.org/v3/index.json' --packages $packages --runtime $runtime -p:NuGetAudit=false -v quiet
if($LASTEXITCODE -ne 0){throw 'Editor restore failed'}
& $Dotnet publish $project -c $Configuration --no-restore --runtime $runtime --self-contained false -o $app "-p:PathMap=$root=/src/gp200-editor" -p:PublishSingleFile=true -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=runtime -p:DebugSymbols=false -p:DebugType=None -p:SatelliteResourceLanguages=es -v quiet
if($LASTEXITCODE -ne 0){throw 'Editor publish failed'}

# Keep the private .NET installation beside the app, but out of the app root.
# The published apphost searches this relative runtime/ directory.
$dotnetCommand=Get-Command $Dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
$dotnetRoot=Split-Path -Parent $dotnetCommand.Source
$runtimeRoot=Join-Path $app 'runtime'
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -Destination $runtimeRoot
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination $runtimeRoot
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination $runtimeRoot

function Copy-LatestRuntimeVersion([string]$source, [string]$destination, [int]$major) {
    if(!(Test-Path -LiteralPath $source)){throw "Required .NET runtime folder is missing: $source"}
    $versionDir=Get-ChildItem -LiteralPath $source -Directory |
        Where-Object { $_.Name -match "^$major\.\d+\.\d+$" } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    if(!$versionDir){throw "No .NET $major runtime found in $source"}
    $target=Join-Path $destination $versionDir.Name
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $versionDir.FullName '*') -Destination $target -Recurse -Force
}

$hostFxrSource=Join-Path $dotnetRoot 'host/fxr'
Copy-LatestRuntimeVersion $hostFxrSource (Join-Path $runtimeRoot 'host/fxr') ([int]$tfm)
Copy-LatestRuntimeVersion (Join-Path $dotnetRoot 'shared/Microsoft.NETCore.App') (Join-Path $runtimeRoot 'shared/Microsoft.NETCore.App') ([int]$tfm)
Copy-LatestRuntimeVersion (Join-Path $dotnetRoot 'shared/Microsoft.WindowsDesktop.App') (Join-Path $runtimeRoot 'shared/Microsoft.WindowsDesktop.App') ([int]$tfm)
# These are optional diagnostics/locales, not editor dependencies.
$optionalFiles=@('GP200 Replica.pdb','createdump.exe')
foreach($name in $optionalFiles){$path=Join-Path $app $name;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
$unusedCultures=@('cs','de','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant')
foreach($culture in $unusedCultures){$path=Join-Path $app $culture;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Recurse -Force}}
$instructions="Open GP200_Custom_Editor and run GP200 Replica.exe. Keep the complete folder together. The private .NET runtime is inside runtime/."
[IO.File]::WriteAllText((Join-Path $package 'README.txt'),$instructions+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
$exe=Join-Path $app 'GP200 Replica.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Published executable was not produced'}
$rootDlls=@(Get-ChildItem -LiteralPath $app -File -Filter '*.dll')
if($rootDlls.Count -gt 0){throw "DLLs must be stored in subfolders, not beside the executable: $($rootDlls.Name -join ', ')"}
foreach($required in @('runtime/host/fxr','runtime/shared/Microsoft.NETCore.App','runtime/shared/Microsoft.WindowsDesktop.App','Data','Converter')){
    if(!(Test-Path -LiteralPath (Join-Path $app $required))){throw "Published application is missing required folder: $required"}
}
Write-Output "PASS: Windows x64 application folder with private runtime: $app"
