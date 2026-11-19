# Renders the background and sign-in pictures of the earlier screens from their SVG sources.
# The logo, the icon and the poster placeholder come from tools/Screenshots (icons command).
# Uses headless Microsoft Edge (no window is shown). Run from any directory:
#   powershell -ExecutionPolicy Bypass -File assets/render-assets.ps1
$ErrorActionPreference = 'Stop'

$assets = $PSScriptRoot
$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ("film-assets-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

function Render([string]$svg, [int]$width, [int]$height, [string]$output) {
    $source = (Join-Path $assets $svg) -replace '\\', '/'
    $page = Join-Path $work 'page.html'
    Set-Content -Path $page -Encoding UTF8 -Value (
        "<!doctype html><html><body style=`"margin:0;background:transparent`">" +
        "<img src=`"file:///$source`" style=`"display:block;width:${width}px;height:${height}px`"></body></html>")
    $profile = Join-Path $work 'profile'
    $arguments = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', "--user-data-dir=$profile",
        '--default-background-color=00000000', '--allow-file-access-from-files',
        "--window-size=$width,$height", "--screenshot=$output", "file:///$($page -replace '\\', '/')")
    Start-Process -FilePath $edge -ArgumentList $arguments -WindowStyle Hidden -Wait
    if (-not (Test-Path $output)) { throw "Rendering $svg failed." }
}

try {
    Render 'background.svg' 1920 1080 (Join-Path $assets 'background.png')
    Render 'hero.svg' 545 737 (Join-Path $assets 'hero.png')
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
