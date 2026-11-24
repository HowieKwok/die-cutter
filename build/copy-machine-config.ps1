param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$OutDir
)

# Deploy machine config after build:
#   config\General\*        -> <OutDir>ZSystemConfigurationFile\
#   config\<machine>\*      -> <OutDir>ZSystemConfigurationFile\<machine>\
# The machine is chosen by CraftType in config\General\SystemParam.json.
# Machine display names live in build\machine-types.txt ("<value>=<folder name>")
# so this script stays ASCII-only; the txt is read as UTF-8.
# Existing files are SKIPPED: the output folder holds tuned site parameters.

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path $RepoRoot).Path
$out = [System.IO.Path]::GetFullPath($OutDir)

$sysParam = Join-Path $repo 'config\General\SystemParam.json'
if (-not (Test-Path $sysParam)) {
    Write-Host "[config] missing $sysParam, skip machine config deployment"
    return
}

$raw = Get-Content $sysParam -Raw -Encoding UTF8
$hit = [regex]::Match($raw, '"CraftType"\s*:\s*(\d+)')
if (-not $hit.Success) {
    throw "[config] cannot read CraftType from $sysParam"
}
$key = $hit.Groups[1].Value
Write-Host "[config] CraftType=$key"

# RunMode 与 CraftType 同源：0=自动、1=仿真。缺这个键时按 0 处理——
# 旧配置文件反序列化出来也是 0，不能因为升级就悄悄把现场机器切成仿真。
$runHit = [regex]::Match($raw, '"RunMode"\s*:\s*(\d+)')
$runMode = if ($runHit.Success) { [int]$runHit.Groups[1].Value } else { 0 }
$runModeText = if ($runMode -eq 1) { 'Simulation' } else { 'Auto' }
Write-Host "[config] RunMode=$runMode ($runModeText)"

$mapFile = Join-Path $repo 'build\machine-types.txt'
if (-not (Test-Path $mapFile)) {
    throw "[config] missing machine type map: $mapFile"
}

$machine = $null
foreach ($line in (Get-Content $mapFile -Encoding UTF8)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    $parts = $trimmed.Split('=')
    if ($parts.Length -ne 2) { continue }
    if ($parts[0].Trim() -eq $key) {
        $machine = $parts[1].Trim()
        break
    }
}

if ([string]::IsNullOrEmpty($machine)) {
    throw "[config] CraftType=$key has no entry in $mapFile"
}

$machineSrc = Join-Path $repo ('config\' + $machine)
if (-not (Test-Path $machineSrc)) {
    throw "[config] machine folder not found: $machineSrc"
}

$targetRoot = Join-Path $out 'ZSystemConfigurationFile'
$targetMachine = Join-Path $targetRoot $machine
New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
New-Item -ItemType Directory -Force -Path $targetMachine | Out-Null

# Only copy files whose target is missing: the output folder is live site data,
# a rebuild must not overwrite tuned parameters (IP, machine number, accounts).
# To force a refresh, delete the file in the output folder and rebuild.
function Copy-MissingFiles([string]$SourceDir, [string]$TargetDir, [string[]]$Skip = @()) {
    if (-not (Test-Path $SourceDir)) { return }

    $root = (Get-Item $SourceDir).FullName.TrimEnd('\')
    Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($root.Length).TrimStart('\')
        if (-not $relative.Contains('\') -and $Skip -contains $relative) { return }

        $dest = Join-Path $TargetDir $relative
        if (Test-Path $dest) { return }

        $destParent = Split-Path $dest -Parent
        if (-not (Test-Path $destParent)) {
            New-Item -ItemType Directory -Force -Path $destParent | Out-Null
        }
        Copy-Item -Path $_.FullName -Destination $dest -Force
        Write-Host "[config] new: $relative"
    }
}

Copy-MissingFiles (Join-Path $repo 'config\General') $targetRoot
Copy-MissingFiles $machineSrc $targetMachine

# SystemParam.json 是「机型/模式开关」，不是现场调参数据：输出目录里那份可能被改过
# 机台号/账号，也可能在第一次投放时就是坏的。这里只保证 CraftType 与 RunMode 与仓库一致。
function Sync-SystemSwitch([string]$Source, [string]$Target) {
    if (-not (Test-Path $Target)) { return }

    $srcRaw = Get-Content $Source -Raw -Encoding UTF8
    $srcHit = [regex]::Match($srcRaw, '"CraftType"\s*:\s*(\d+)')
    if (-not $srcHit.Success) { return }
    $wantedCraft = [int]$srcHit.Groups[1].Value
    $wantedRun = $runMode

    $dst = $null
    try { $dst = Get-Content $Target -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }

    if ($null -eq $dst -or -not ($dst.PSObject.Properties.Name -contains 'CraftType')) {
        # 读不出来就没法保留现场改动，直接用仓库副本替换
        Copy-Item -Path $Source -Destination $Target -Force
        Write-Host "[config] SystemParam.json unreadable, replaced with repo copy (CraftType=$wantedCraft, RunMode=$wantedRun)"
        return
    }

    $changed = $false

    if ([int]$dst.CraftType -ne $wantedCraft) {
        Write-Host "[config] SystemParam.json CraftType $($dst.CraftType) -> $wantedCraft (repo), other fields kept"
        $dst.CraftType = $wantedCraft
        $changed = $true
    }

    # 旧版本没有 RunMode 键：必须补上，否则现场那份永远按 0(自动) 跑，
    # 改仓库里的配置不会生效——运行期读的是输出目录这份。
    if ($dst.PSObject.Properties.Name -contains 'RunMode') {
        if ([int]$dst.RunMode -ne $wantedRun) {
            Write-Host "[config] SystemParam.json RunMode $($dst.RunMode) -> $wantedRun (repo), other fields kept"
            $dst.RunMode = $wantedRun
            $changed = $true
        }
    }
    else {
        $dst | Add-Member -NotePropertyName 'RunMode' -NotePropertyValue $wantedRun
        Write-Host "[config] SystemParam.json RunMode added = $wantedRun (repo)"
        $changed = $true
    }

    if (-not $changed) { return }

    $json = $dst | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Target, $json, (New-Object System.Text.UTF8Encoding($false)))
}

Sync-SystemSwitch $sysParam (Join-Path $targetRoot 'SystemParam.json')

# 缺陷检测模型：按 config\General\SystemParam.json 的 AiPolarity 指定的极性，把对应模型摊平拷到 <OutDir>\AIModel\。
# 极性只有一份（设备出厂即定），输出根下因此只有一个 AIModel\model.onnx，
# 而仓库里保留正负极两份，用来切换。
# 缺陷检测模型：正负极两份**都**投放到 <OutDir>\AIModel\<正极|负极>\。
# 运行期由配方的极性（工艺界面基础设置）决定用哪份，因此切换极性不需要重新编译。
function DeployAiModel {
    $srcRoot = Join-Path $repo 'config\AiModels'
    if (-not (Test-Path $srcRoot)) {
        Write-Host "[config] no config\AiModels, skip AIModel deployment"
        return
    }

    $destDir = Join-Path $out 'AIModel'
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    Copy-MissingFiles $srcRoot $destDir
    Write-Host "[config] AIModel ensured (both polarities)"
}

DeployAiModel

Write-Host "[config] machine [$machine] config ensured under $targetRoot"