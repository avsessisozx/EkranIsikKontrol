@echo off
rem Ekran Isik Kontrol derleme betigi.
rem Uygulama: Visual Studio derleme araclarindaki MSBuild ile (WPF arayuzu).
rem Testler: ayni araclardaki Roslyn C# derleyicisiyle.
setlocal
cd /d "%~dp0"

set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
if not exist "%VSWHERE%" (
  echo Visual Studio derleme araclari bulunamadi.
  exit /b 1
)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%i
if not defined MSBUILD (
  echo MSBuild bulunamadi.
  exit /b 1
)
for %%m in ("%MSBUILD%") do set CSC=%%~dpmRoslyn\csc.exe

if not exist bin mkdir bin
set ORTAK=src\Gunluk.cs src\Monitorler.cs src\EkranModlari.cs src\GeceIsigi.cs src\OpenRgb.cs src\Ayarlar.cs
set REF=/r:System.Core.dll /r:System.Management.dll /r:System.Runtime.Serialization.dll

"%CSC%" /nologo /langversion:latest /platform:x64 /target:exe /codepage:65001 /out:bin\BirimTesti.exe %REF% %ORTAK% test\BirimTesti.cs || exit /b 1
bin\BirimTesti.exe || exit /b 1

"%CSC%" /nologo /langversion:latest /platform:x64 /target:exe /codepage:65001 /out:bin\DonanimTesti.exe %REF% %ORTAK% test\DonanimTesti.cs || exit /b 1

"%MSBUILD%" EkranIsikKontrol.csproj -nologo -v:minimal -p:Configuration=Release || exit /b 1

echo Derleme tamam: bin\EkranIsikKontrol.exe
