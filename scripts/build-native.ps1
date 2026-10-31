param([string]$Generator,[switch]$Install)
$ErrorActionPreference='Stop';$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$out=Join-Path $root 'artifacts/native'
$arguments=@('-S',(Join-Path $root 'Native'),'-B',$out,'-A','x64')
if($Generator){$arguments+=@('-G',$Generator)}
& cmake @arguments
if($LASTEXITCODE -ne 0){throw 'Native configure failed. Install CMake and MSVC C++/Windows SDK.'}
& cmake --build $out --config Release --parallel 2
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
if($Install){
 Copy-Item -LiteralPath (Join-Path $out 'Release/GP200ConversionWorker.exe') -Destination (Join-Path $root 'Editor/Converter/GP200ConversionWorker.exe') -Force
 Copy-Item -LiteralPath (Join-Path $out 'Release/r8brain-gp200.dll') -Destination (Join-Path $root 'Editor/Data/r8brain-gp200.dll') -Force
 Write-Output 'Native binaries replaced in Editor. Rebuild the editor before distributing.'
}else{Write-Output 'Native build completed; included runtime binaries were not replaced.'}
