param(
	[Parameter(Position = 0)]
	[ValidateSet("arch", "debian13", "ubuntu2404", "ubuntu2604", "alpine")]
	[string]$Target,

	[switch]$NoCache,

	[switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Help {
	@"
Usage: gnomesurface-docker.ps1 [options] <target>

Build Docker image for the selected target.

Targets:
	arch        Build Arch Linux GNOME image
	debian13    Build Debian 13 GNOME image
	ubuntu2404  Build Ubuntu 24.04 GNOME image
	ubuntu2604  Build Ubuntu 26.04 GNOME image
	alpine      Build Alpine Linux GNOME image

Options:
	-NoCache    Build without Docker cache
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

foreach ($envFile in @((Join-Path $ScriptDir "shared.env"), (Join-Path $ScriptDir "$Target/$Target.env"))) {
	Get-Content $envFile | ForEach-Object {
		if ($_ -match "^([^#=]+)=(.*)$") {
			Set-Variable -Name $matches[1] -Value $matches[2]
		}
	}
}

switch ($Target) {
	"arch" {
		$ImageTag = "gnomesurface/arch:$GM_TARGETARCH"
		$Dockerfile = Join-Path $ScriptDir "arch/Dockerfile"
	}
	"debian13" {
		$ImageTag = "gnomesurface/debian13:$GM_TARGETARCH"
		$Dockerfile = Join-Path $ScriptDir "debian13/Dockerfile"
	}
	"ubuntu2404" {
		$ImageTag = "gnomesurface/ubuntu2404:$GM_TARGETARCH"
		$Dockerfile = Join-Path $ScriptDir "ubuntu2404/Dockerfile"
	}
	"ubuntu2604" {
		$ImageTag = "gnomesurface/ubuntu2604:$GM_TARGETARCH"
		$Dockerfile = Join-Path $ScriptDir "ubuntu2604/Dockerfile"
	}
	"alpine" {
		$ImageTag = "gnomesurface/alpine:$GM_TARGETARCH"
		$Dockerfile = Join-Path $ScriptDir "alpine/Dockerfile"
	}
}

$ContextDir = $ScriptDir

function New-VolumeIfMissing {
	[CmdletBinding(SupportsShouldProcess = $true)]
	param([string]$Name, [string]$Device)
	New-Item -ItemType Directory -Force -Path $Device | Out-Null
	docker volume inspect $Name *> $null
	if ($LASTEXITCODE -ne 0) {
		docker volume create `
			--driver local `
			--opt type=none `
			--opt device=$Device `
			--opt o=bind `
			$Name
		Write-Output "Volume '$Name' created."
	}
	else {
		Write-Output "Volume '$Name' already exists, skipping."
	}
}

New-VolumeIfMissing "gnomesurface_home_local_share_gnomesurface" (Join-Path $ScriptDir "volumes/shared/home_local_share_gnomesurface")
New-VolumeIfMissing "gnomesurface_home_local_share_flatpak"      (Join-Path $ScriptDir "volumes/shared/home_local_share_flatpak")
New-VolumeIfMissing "gnomesurface_home_var_app"                  (Join-Path $ScriptDir "volumes/shared/home_var_app")
New-VolumeIfMissing "gnomesurface_var_lib_flatpak"               (Join-Path $ScriptDir "volumes/shared/var_lib_flatpak")

$BuildArgs = @(
	"build"
	"-f", $Dockerfile
	"--build-arg", "GM_TARGETARCH=$GM_TARGETARCH"
	"--build-arg", "GM_HOSTNAME=$GM_HOSTNAME"
	"--build-arg", "GM_RDP_DOCKER_PORT=$GM_RDP_DOCKER_PORT"
	"--build-arg", "GM_RDP_USER_UID=$GM_RDP_USER_UID"
	"--build-arg", "GM_RDP_USER_GID=$GM_RDP_USER_GID"
	"--build-arg", "GM_RDP_USER=$GM_RDP_USER"
	"--build-arg", "GM_TIMEZONE=$GM_TIMEZONE"
	"--build-arg", "GM_LOCALES=$GM_LOCALES"
	"--build-arg", "GM_LOCALE=$GM_LOCALE"
	"--build-arg", "GM_LANGUAGE=$GM_LANGUAGE"
	"--build-arg", "GM_KEYMAP=$GM_KEYMAP"
	"--build-arg", "GM_KEYMODEL=$GM_KEYMODEL"
)

if ($NoCache) {
	$BuildArgs += "--no-cache"
}

$BuildArgs += @("-t", $ImageTag, $ContextDir)

& docker @BuildArgs
