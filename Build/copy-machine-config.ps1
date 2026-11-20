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