@echo off
REM FluentORM NuGet Package Build Script
REM This batch file builds and packs the FluentORM.Complete package

echo.
echo ========================================
echo FluentORM Package Builder
echo ========================================
echo.

REM Create nupkg directory if it doesn't exist
if not exist "nupkg" (
	mkdir nupkg
	echo Created nupkg directory
)

REM Build all projects
echo.
echo Building projects in Release mode...
echo.

echo Building FluentORM.Core...
dotnet build src\FluentORM.Core\FluentORM.Core.csproj -c Release
if errorlevel 1 goto :error

echo Building FluentORM.Migrations...
dotnet build src\FluentORM.Migrations\FluentORM.Migrations.csproj -c Release
if errorlevel 1 goto :error

echo Building FluentORM.Sqlite...
dotnet build src\FluentORM.Sqlite\FluentORM.Sqlite.csproj -c Release
if errorlevel 1 goto :error

echo Building FluentORM.SqlServer...
dotnet build src\FluentORM.SqlServer\FluentORM.SqlServer.csproj -c Release
if errorlevel 1 goto :error

echo Building FluentORM meta-package...
dotnet build src\FluentORM\FluentORM.csproj -c Release
if errorlevel 1 goto :error

REM Pack the package
echo.
echo ========================================
echo Creating NuGet package...
echo ========================================
echo.

dotnet pack src\FluentORM\FluentORM.csproj -c Release -o nupkg
if errorlevel 1 goto :error

REM Display results
echo.
echo ========================================
echo Package Creation Complete!
echo ========================================
echo.
echo Generated packages:
dir /b nupkg\*.nupkg
echo.
echo To publish to NuGet:
echo   dotnet nuget push nupkg\FluentORM.Complete.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
echo.
goto :end

:error
echo.
echo ERROR: Build or pack failed!
echo.
pause
exit /b 1

:end
echo.
pause
exit /b 0
