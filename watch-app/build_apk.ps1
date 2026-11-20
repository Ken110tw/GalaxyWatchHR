$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ProjectDir
$BuildDir = Join-Path $ProjectDir "build"
$DistDir = Join-Path $RootDir "dist"

$SdkDir = Join-Path $env:LOCALAPPDATA "Android\Sdk"
$BuildTools = Join-Path $SdkDir "build-tools\33.0.2"
$AndroidJar = Join-Path $SdkDir "platforms\android-UpsideDownCake\android.jar"

$JdkDir = "C:\Program Files\Java\jdk-20"
$JavaExe = Join-Path $JdkDir "bin\java.exe"
$Javac = Join-Path $JdkDir "bin\javac.exe"
$Keytool = Join-Path $JdkDir "bin\keytool.exe"
$Aapt2 = Join-Path $BuildTools "aapt2.exe"
$D8Jar = Join-Path $BuildTools "lib\d8.jar"
$Zipalign = Join-Path $BuildTools "zipalign.exe"
$ApksignerJar = Join-Path $BuildTools "lib\apksigner.jar"

$env:JAVA_HOME = $JdkDir
$env:PATH = "$JdkDir\bin;$env:PATH"

if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
New-Item -ItemType Directory -Force -Path "$BuildDir\compiled_res","$BuildDir\gen","$BuildDir\classes","$BuildDir\dex","$DistDir" | Out-Null

Write-Host "[1/6] Compiling resources with aapt2..."
& $Aapt2 compile --dir "$ProjectDir\res" -o "$BuildDir\compiled_res\res.zip"
if ($LASTEXITCODE -ne 0) { throw "aapt2 compile failed" }

Write-Host "[2/6] Linking resources and generating R.java..."
& $Aapt2 link `
    -I $AndroidJar `
    --manifest "$ProjectDir\AndroidManifest.xml" `
    --java "$BuildDir\gen" `
    -o "$BuildDir\unsigned.apk" `
    "$BuildDir\compiled_res\res.zip"
if ($LASTEXITCODE -ne 0) { throw "aapt2 link failed" }

Write-Host "[3/6] Compiling Java sources..."
$JavaFiles = @(Get-ChildItem -Recurse -Filter "*.java" -Path "$ProjectDir\src","$BuildDir\gen" | ForEach-Object { $_.FullName })
& $Javac --release 11 -classpath $AndroidJar -d "$BuildDir\classes" @JavaFiles
if ($LASTEXITCODE -ne 0) { throw "javac failed" }

Write-Host "[4/6] Converting bytecode to DEX with d8..."
$ClassFiles = @(Get-ChildItem -Recurse -Filter "*.class" -Path "$BuildDir\classes" | ForEach-Object { $_.FullName })
& $JavaExe -cp $D8Jar com.android.tools.r8.D8 --min-api 30 --lib $AndroidJar --output "$BuildDir\dex" @ClassFiles
if ($LASTEXITCODE -ne 0) { throw "d8 failed" }

Write-Host "[5/6] Packaging classes.dex into APK and zipaligning..."
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Zip = [System.IO.Compression.ZipFile]::Open("$BuildDir\unsigned.apk", [System.IO.Compression.ZipArchiveMode]::Update)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Zip, "$BuildDir\dex\classes.dex", "classes.dex", [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally {
    $Zip.Dispose()
}

& $Zipalign -f 4 "$BuildDir\unsigned.apk" "$BuildDir\aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }

Write-Host "[6/6] Signing APK with local debug keystore..."
$Keystore = Join-Path $ProjectDir "debug.keystore"
if (-not (Test-Path $Keystore)) {
    & $Keytool -genkeypair -v -keystore $Keystore -storepass android -alias watchhr -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=GalaxyWatchHR,O=Local,C=TW" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "keytool failed" }
}

$OutApk = Join-Path $DistDir "GalaxyWatchHR.apk"
& $JavaExe -jar $ApksignerJar sign --ks $Keystore --ks-pass pass:android --key-pass pass:android --out $OutApk "$BuildDir\aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }

Write-Host "SUCCESS: Built $OutApk"
Get-Item $OutApk | Select-Object FullName, Length
