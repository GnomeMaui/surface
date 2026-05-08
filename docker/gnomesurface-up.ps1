param(
	[Parameter(Position = 0)]
	[ValidateSet("arch", "debian13", "ubuntu2404", "ubuntu2604", "alpine")]
	[string]$Target,

	[switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Help {
	@"
Usage: gnomesurface-up.ps1 <target>

Start the selected Docker stack.

Targets:
	arch
	debian13
	ubuntu2404
	ubuntu2604
	alpine

Options:
	-Help       Show this help message
"@
}

function Write-ReadyInfo {
	param([string]$ScriptDir, [string]$Target)

	$env = @{}
	foreach ($file in @("$ScriptDir/shared.env", "$ScriptDir/$Target/$Target.env")) {
		Get-Content $file | ForEach-Object {
			if ($_ -match "^([^#=]+)=(.*)$") {
				$env[$matches[1]] = $matches[2]
			}
		}
	}

	$g = $PSStyle.Foreground.Green
	$y = $PSStyle.Foreground.Yellow
	$r = $PSStyle.Reset
	Write-Output " ${g}`u{2714}${r} RDP is available."
	Write-Output " ${g}`u{2714}${r} RDP endpoint: ${y}$($env['GM_IP']):$($env['GM_RDP_HOST_PORT'])${r}"
	Write-Output " ${g}`u{2714}${r} Hosts entry: ${y}$($env['GM_IP']) $($env['GM_TARGET'])${r}"
}

if ($Help) {
	Show-Help
	exit 0
}

if (-not $Target) {
	[Console]::Error.WriteLine("Missing target.")
	Show-Help
	exit 1
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& docker compose --env-file (Join-Path $ScriptDir "shared.env") --env-file (Join-Path $ScriptDir "$Target/$Target.env") -f (Join-Path $ScriptDir "$Target/docker-compose.yml") up -d --wait --wait-timeout 120
if ($LASTEXITCODE -eq 0) {
	Write-ReadyInfo -ScriptDir $ScriptDir -Target $Target
}
