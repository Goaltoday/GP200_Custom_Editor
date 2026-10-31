param([string]$Configuration='Release',[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=Join-Path $root 'artifacts'
$app=Join-Path $out 'app'
New-Item -ItemType Directory -Path $out -Force | Out-Null
if(Test-Path -LiteralPath $app){Remove-Item -LiteralPath $app -Recurse -Force}
New-Item -ItemType Directory -Path $app -Force | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $out 'cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$project=Join-Path $root 'Editor/Gp200.Replica.csproj'
$packages=Join-Path $out 'nuget'
$runtime='win-x64'
& $Dotnet restore $project --configfile (Join-Path $root 'NuGet.Config') --source 'https://api.nuget.org/v3/index.json' --packages $packages --runtime $runtime -p:NuGetAudit=false -v quiet
if($LASTEXITCODE -ne 0){throw 'Editor restore failed'}
& $Dotnet publish $project -c $Configuration --no-restore --runtime $runtime --self-contained true -o $app "-p:PathMap=$root=/src/gp200-editor" -p:DebugSymbols=false -p:DebugType=None -p:SatelliteResourceLanguages=es -v quiet
if($LASTEXITCODE -ne 0){throw 'Editor publish failed'}
# These are optional diagnostics/locales, not editor dependencies.
$optionalFiles=@('GP200 Replica.pdb','createdump.exe')
foreach($name in $optionalFiles){$path=Join-Path $app $name;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
$unusedCultures=@('cs','de','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant')
foreach($culture in $unusedCultures){$path=Join-Path $app $culture;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Recurse -Force}}
$exe=Join-Path $app 'GP200 Replica.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Published executable was not produced'}
Write-Output "PASS: self-contained Windows x64 application folder: $app"
