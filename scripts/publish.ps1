param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot))
$outputRoot = Join-Path $root 'artifacts'
$runRoot = Join-Path $outputRoot ('build-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$app = Join-Path $runRoot 'CodexUsageMonitor-win-x64'
New-Item -ItemType Directory -Path $app -Force | Out-Null
function Run-Dotnet([string[]]$Arguments) {
  & $Dotnet @Arguments
  if ($LASTEXITCODE -ne 0) { throw 'Build or test failed.' }
}
Run-Dotnet @('build', (Join-Path $root 'CodexUsageMonitor.sln'), '-c','Release','--nologo','-m:1','-nr:false','-p:UseSharedCompilation=false','-v','minimal')
foreach ($project in @('TaskbarWidget','CodexProbe')) {
  Run-Dotnet @('publish',(Join-Path $root "src/$project/$project.csproj"),'-c','Release','-r','win-x64','--self-contained','true','-o',$app,'-p:PublishSingleFile=false','-p:UseSharedCompilation=false','-m:1','-nr:false','-v','minimal')
}
& (Join-Path $app 'CodexProbe.exe') --selftest
if ($LASTEXITCODE -ne 0) { throw 'Security selftest failed.' }
$ui = Start-Process -FilePath (Join-Path $app 'TaskbarWidget.exe') -ArgumentList '--selftest-all' -WindowStyle Hidden -PassThru
if (-not $ui.WaitForExit(30000)) { $ui.Kill(); throw 'UI selftest timed out.' }
if ($ui.ExitCode -ne 0) { throw 'UI selftest failed.' }
Copy-Item -LiteralPath (Join-Path $root 'README.md'),(Join-Path $root 'CLAUDE_SETUP.md'),(Join-Path $root 'SECURITY.md') -Destination $app
& (Join-Path $PSScriptRoot 'verify-public.ps1') -Root $root -ArtifactDirectory $app
$source = Join-Path $runRoot 'CodexUsageMonitor-source'
New-Item -ItemType Directory -Path $source -Force | Out-Null
foreach ($name in @('src','scripts','README.md','CLAUDE_SETUP.md','SECURITY.md','CodexUsageMonitor.sln','Directory.Build.props','global.json','NuGet.Config','.gitignore')) {
  $item = Join-Path $root $name
  if ((Get-Item -LiteralPath $item -Force).PSIsContainer) {
    foreach ($file in (Get-ChildItem -LiteralPath $item -Recurse -File -Force | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
      $dest=Join-Path $source ([IO.Path]::GetRelativePath($root,$file.FullName))
      New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
      Copy-Item -LiteralPath $file.FullName -Destination $dest
    }
  } else { Copy-Item -LiteralPath $item -Destination $source }
}
& (Join-Path $PSScriptRoot 'verify-public.ps1') -Root $source
Compress-Archive -LiteralPath $app -DestinationPath (Join-Path $runRoot 'CodexUsageMonitor-win-x64.zip')
# Compress-Archive omits hidden files, so use ZipFile to retain .gitignore.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($source,(Join-Path $runRoot 'CodexUsageMonitor-source.zip'))
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $runRoot 'CodexUsageMonitor-win-x64.zip'),(Join-Path $runRoot 'CodexUsageMonitor-source.zip') | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $runRoot 'SHA256SUMS.txt')
Write-Output "Release ready: $runRoot"
