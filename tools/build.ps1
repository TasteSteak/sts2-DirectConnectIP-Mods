# 极简版 build.ps1
$ErrorActionPreference = "Stop"

# 设置 Godot 路径（请修改为你的实际路径）
$godotPath = "D:/桌面文件/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64.exe"

# 获取项目根目录（假设脚本在 tools 文件夹中）
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-Host "项目根目录: $projectRoot"

# 检查 Godot 是否存在
if (-not (Test-Path $godotPath)) {
    Write-Host "错误：找不到 Godot，请修改 `$godotPath` 变量。" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}

# 调用 Godot 执行 build_pck.gd
Write-Host "正在生成 PCK..." -ForegroundColor Green
& $godotPath --headless --path $projectRoot --script "res://tools/build_pck.gd"

# 检查执行结果
if ($LASTEXITCODE -ne 0) {
    Write-Host "PCK 生成失败，退出码: $LASTEXITCODE" -ForegroundColor Red
} else {
    $pckFile = Join-Path $projectRoot "build/DirectConnectIP.pck"
    if (Test-Path $pckFile) {
        Write-Host "PCK 生成成功: $pckFile" -ForegroundColor Green
    }
}

Read-Host "按 Enter 退出"