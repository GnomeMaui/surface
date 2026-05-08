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
Usage: gnomesurface-down.ps1 <target>

Stop the selected Docker stack.

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
& docker compose --env-file (Join-Path $ScriptDir "shared.env") --env-file (Join-Path $ScriptDir "$Target/$Target.env") -f (Join-Path $ScriptDir "$Target/docker-compose.yml") down
