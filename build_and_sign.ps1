# Automated Build, Sign, and Package Script for Partner Relation Manager

$ErrorActionPreference = "Stop"

# 1. Locate or Create Code Signing Certificate
Write-Host "=== 1. Checking Code Signing Certificate ==="
$certSubject = "CN=Work Tracker Local Testing"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*$certSubject*" } | Select-Object -First 1

if ($null -eq $cert) {
    Write-Host "No existing certificate found. Creating new self-signed code signing certificate..."
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject -CertStoreLocation Cert:\CurrentUser\My
    Write-Host "Created certificate: $($cert.Thumbprint)"
} else {
    Write-Host "Found existing certificate: $($cert.Thumbprint)"
}

# 2. Clean Build Directories
Write-Host "=== 2. Cleaning Build Output ==="
if (Test-Path "bin") { Remove-Item -Path "bin" -Recurse -Force }
if (Test-Path "obj") { Remove-Item -Path "obj" -Recurse -Force }
Write-Host "Cleaned successfully."

# 3. dotnet publish net9.0-windows win-x64 self-contained
Write-Host "=== 3. Publishing Project ==="
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true

# 4. Locate and Sign PartnerRelationManager.exe
Write-Host "=== 4. Locating signtool.exe & Signing Executable ==="
$signtoolPaths = @(
    "C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.*\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe",
    "C:\Program Files\Windows Kits\10\bin\10.0.*\x64\signtool.exe"
)

$signtool = $null
foreach ($path in $signtoolPaths) {
    $resolved = Resolve-Path $path -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($resolved) {
        $signtool = $resolved.Path
        break
    }
}

if ($null -eq $signtool) {
    # Fallback recursive search
    Write-Host "Signtool not found in default paths, scanning Windows Kits folder..."
    $signtoolFile = Get-ChildItem "C:\Program Files (x86)\Windows Kits" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($signtoolFile) {
        $signtool = $signtoolFile.FullName
    }
}

$exePath = "bin\Release\net9.0-windows\win-x64\publish\PartnerRelationManager.exe"

if ($null -ne $signtool -and (Test-Path $signtool)) {
    Write-Host "Using signtool at: $signtool"
    Write-Host "Signing $exePath..."
    & $signtool sign /sha1 $cert.Thumbprint /fd SHA256 /d "Partner Relation Manager" /t "http://timestamp.digicert.com" $exePath
    Write-Host "Signed successfully."
} else {
    Write-Warning "signtool.exe was not found. Skipping executable code-signing step."
}

# 5. Extract Product Version from compiled assembly
$productVersion = "1.0.0"
if (Test-Path $exePath) {
    $versionInfo = (Get-Item $exePath).VersionInfo
    if ($versionInfo.ProductVersion) {
        $productVersion = $versionInfo.ProductVersion.Split('+')[0].Trim()
    }
}
Write-Host "Extracted assembly version: $productVersion"

# 6. Locate and Run Inno Setup Compiler (ISCC.exe)
Write-Host "=== 6. Compiling Inno Setup Installer ==="
$isccPaths = @(
    "C:\Users\augus\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if ($null -eq $iscc) {
    Write-Host "ISCC.exe not found in default paths, scanning Program Files..."
    $isccFile = Get-ChildItem "C:\Program Files (x86)" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $isccFile) {
        $isccFile = Get-ChildItem "C:\Program Files" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($isccFile) {
        $iscc = $isccFile.FullName
    }
}

$setupExePath = "PartnerRelationManagerSetup-$productVersion.exe"

if ($null -ne $iscc -and (Test-Path $iscc)) {
    Write-Host "Using Inno Setup Compiler at: $iscc"
    & $iscc /DAppVersion=$productVersion setup.iss
    Write-Host "Installer package compiled successfully: $setupExePath"

    # 7. Sign Setup Installer
    if ($null -ne $signtool -and (Test-Path $signtool)) {
        Write-Host "Signing Installer setup package..."
        & $signtool sign /sha1 $cert.Thumbprint /fd SHA256 /d "Partner Relation Manager Setup" /t "http://timestamp.digicert.com" $setupExePath
        Write-Host "Installer signed successfully."
    } else {
        Write-Warning "signtool.exe was not found. Skipping installer code-signing step."
    }
} else {
    Write-Warning "Inno Setup Compiler (ISCC.exe) was not found. Installer setup package compilation was skipped."
}

Write-Host "=== Build Process Complete ==="
