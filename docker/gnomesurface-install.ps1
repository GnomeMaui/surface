param(
	[Parameter(ValueFromRemainingArguments = $true)]
	[string[]]$Arguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Help {
	@"
Usage: gnomesurface-install.ps1 [options]

Configure shared Docker prerequisites for all stacks.

Actions:
	-Cert    Generate persistent RDP TLS certificate
	-Net     Create the shared Docker network from .env values
	-All     Run both actions

Options:
	-Force   Overwrite existing certificate
	-Help    Show this help message
"@
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnvPath = Join-Path $ScriptDir "shared.env"
$SecretDir = Join-Path $ScriptDir ".secret"
$KeyFile = Join-Path $SecretDir "rdp-tls.key"
$CrtFile = Join-Path $SecretDir "rdp-tls.crt"
$NetworkName = "gnomesurface-network"
$Arguments = @($Arguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

$RunCert = $false
$RunNet = $false
$Force = $false

function Import-EnvFile {
	Get-Content $EnvPath | ForEach-Object {
		if ($_ -match "^([^#=]+)=(.*)$") {
			Set-Variable -Name $matches[1] -Value $matches[2] -Scope Script
		}
	}
}

function New-Certificate {
	[CmdletBinding(SupportsShouldProcess = $true)]
	param()

	New-Item -ItemType Directory -Force -Path $SecretDir | Out-Null

	if ((Test-Path $KeyFile) -and (Test-Path $CrtFile) -and -not $Force) {
		Write-Output "RDP TLS certificate already exists, skipping generation."
		Write-Output "Use -Force to regenerate."
		return
	}

	if ($PSCmdlet.ShouldProcess("RDP TLS certificate", "Generate")) {
		Write-Output "Generating RDP TLS certificate..."
		$rsa = [System.Security.Cryptography.RSA]::Create(2048)
		try {
			$req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
				"CN=gnomesurface-rdp",
				$rsa,
				[System.Security.Cryptography.HashAlgorithmName]::SHA256,
				[System.Security.Cryptography.RSASignaturePadding]::Pkcs1
			)
			$notBefore = [System.DateTimeOffset]::UtcNow
			$notAfter = $notBefore.AddDays(3650)
			$cert = $req.CreateSelfSigned($notBefore, $notAfter)

			Set-Content -Path $KeyFile -Value $rsa.ExportRSAPrivateKeyPem() -NoNewline
			Set-Content -Path $CrtFile -Value $cert.ExportCertificatePem() -NoNewline
		}
		finally {
			$rsa.Dispose()
		}

		if ($IsLinux -or $IsMacOS) {
			& chmod 600 $KeyFile
			& chmod 644 $CrtFile
		}
		else {
			$acl = Get-Acl $KeyFile
			$acl.SetAccessRuleProtection($true, $false)
			$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
				[System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
				"FullControl",
				"Allow"
			)
			$acl.SetAccessRule($rule)
			Set-Acl -Path $KeyFile -AclObject $acl
		}

		Write-Output "Certificate generated:"
		Write-Output "  $KeyFile"
		Write-Output "  $CrtFile"
	}
}

function New-DockerNetwork {
	[CmdletBinding(SupportsShouldProcess = $true)]
	param()

	Import-EnvFile

	& docker network inspect $NetworkName *> $null
	if ($LASTEXITCODE -eq 0) {
		Write-Output "Docker network '$NetworkName' already exists, skipping creation."
		return
	}

	if ($PSCmdlet.ShouldProcess("Docker network '$NetworkName'", "Create")) {
		Write-Output "Creating Docker network '$NetworkName'..."
		& docker network create --subnet=$GM_SUBNET --gateway=$GM_GATEWAY $NetworkName
	}
}

foreach ($argument in $Arguments) {
	switch ($argument) {
		'-Cert' { $RunCert = $true; continue }
		'-Net' { $RunNet = $true; continue }
		'-All' { $RunCert = $true; $RunNet = $true; continue }
		'-Force' { $Force = $true; continue }
		'-Help' { Show-Help; exit 0 }
		default {
			[Console]::Error.WriteLine("Unknown argument '$argument'.")
			Show-Help
			exit 1
		}
	}
}

if ($Arguments.Count -eq 0 -or (-not $RunCert -and -not $RunNet)) {
	Show-Help
	exit 0
}

if ($RunCert) {
	New-Certificate
}

if ($RunNet) {
	New-DockerNetwork
}
