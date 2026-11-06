param([string]$Root = (Split-Path $PSScriptRoot), [string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
$findings = [Collections.Generic.List[string]]::new()
$secretPattern = '(?<![A-Za-z0-9])(?:sk-(?:proj-|ant-|svcacct-)[A-Za-z0-9_-]{20,}|sk-[A-Za-z0-9]{32,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})'
$privatePathPattern = '(?i)(?:[A-Z]:[\\/]+Users[\\/]+(?!Public[\\/]|Default[\\/])[^\\/\s"<>]+)'
function Check-Text([string]$Text, [string]$Label) {
  if ($Text -match $secretPattern) { $findings.Add("Possible credential: $Label") }
  if ($Text -match $privatePathPattern) { $findings.Add("Personal build path: $Label") }
  if ($Text -match '(?i)[A-Z0-9._%+-]+@(?:hotmail|gmail|outlook|yahoo)\.[A-Z.]+') { $findings.Add("Personal email: $Label") }
}
function Check-File([string]$Path, [string]$Label) {
  $bytes = [IO.File]::ReadAllBytes($Path)
  Check-Text ([Text.Encoding]::UTF8.GetString($bytes)) $Label
  if ($Path -match '\.(exe|dll|pdb)$') { Check-Text ([Text.Encoding]::Unicode.GetString($bytes)) $Label }
}
$files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force | Where-Object {
  [IO.Path]::GetRelativePath($Root, $_.FullName) -notmatch '(^|[\\/])(\.git|\.superpowers|bin|obj|artifacts|publish[^\\/]*)([\\/]|$)'
}
foreach ($file in $files) {
  $relative = [IO.Path]::GetRelativePath($Root, $file.FullName)
  if ($file.Name -match '^(\.env(\..*)?|auth\.json|\.credentials\.json|\.last-token)$' -and $file.Name -ne '.env.example') {
    $findings.Add("Private file: $relative")
  }
  Check-File $file.FullName $relative
}
if (Test-Path -LiteralPath (Join-Path $Root '.git')) {
  $objects = & git -C $Root rev-list --objects --all
  if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git history.' }
  foreach ($entry in $objects) {
    $parts = $entry -split ' ', 2; $oid = $parts[0]
    if ($parts.Count -eq 2 -and $parts[1] -match '(^|/)(bin|obj|\.superpowers)/|(^|/)(auth\.json|\.credentials\.json|\.env|\.last-token)$') { $findings.Add("Private path in Git history: $($parts[1])") }
    $kind = & git -C $Root cat-file -t $oid
    if ($kind -eq 'blob') { Check-Text ((& git -C $Root cat-file blob $oid) -join "`n") "Git object $oid" }
  }
  $metadata = (& git -C $Root log --all --format='%H%n%an%n%ae%n%cn%n%ce%n%B') -join "`n"
  Check-Text $metadata 'Git metadata'
  foreach ($email in (& git -C $Root log --all --format='%ae%n%ce')) {
    if ($email -notmatch '@(?:users\.noreply\.github\.com|example\.invalid)$') { $findings.Add('Git identity needs a public/noreply email'); break }
  }
}
if ($ArtifactDirectory) {
  foreach ($file in (Get-ChildItem -LiteralPath $ArtifactDirectory -File -Recurse)) {
    if ($file.Extension -in '.pdb','.log','.dmp') { $findings.Add("Unexpected release file: $($file.Name)") }
    Check-File $file.FullName "release/$($file.Name)"
  }
}
if ($findings.Count) { $findings | Sort-Object -Unique | Write-Output; throw 'Public release verification failed. Values were not printed.' }
Write-Output 'Public release verification PASS (source, available Git history, selected artifacts).'
