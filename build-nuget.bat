@echo off
REM FluentORM NuGet Package Build Script

echo.
echo ========================================
echo  FluentORM Package Builder
echo ========================================
echo.

if not exist "nupkg" mkdir nupkg

REM ── Build all source projects in Release ──────────────────────────────────
echo Building all projects in Release mode...
echo.

echo [1/5] FluentORM.Core
dotnet build src\FluentORM.Core\FluentORM.Core.csproj -c Release -v q
if errorlevel 1 goto :error

echo [2/5] FluentORM.Migrations
dotnet build src\FluentORM.Migrations\FluentORM.Migrations.csproj -c Release -v q
if errorlevel 1 goto :error

echo [3/5] FluentORM.Sqlite
dotnet build src\FluentORM.Sqlite\FluentORM.Sqlite.csproj -c Release -v q
if errorlevel 1 goto :error

echo [4/5] FluentORM.SqlServer
dotnet build src\FluentORM.SqlServer\FluentORM.SqlServer.csproj -c Release -v q
if errorlevel 1 goto :error

echo [5/5] FluentORM.Testing
dotnet build src\FluentORM.Testing\FluentORM.Testing.csproj -c Release -v q
if errorlevel 1 goto :error

echo.
echo ========================================
echo  Packing NuGet packages...
echo ========================================
echo.

dotnet pack src\FluentORM.Core\FluentORM.Core.csproj         -c Release -o nupkg --no-build
if errorlevel 1 goto :error

dotnet pack src\FluentORM.Migrations\FluentORM.Migrations.csproj -c Release -o nupkg --no-build
if errorlevel 1 goto :error

dotnet pack src\FluentORM.Sqlite\FluentORM.Sqlite.csproj     -c Release -o nupkg --no-build
if errorlevel 1 goto :error

dotnet pack src\FluentORM.SqlServer\FluentORM.SqlServer.csproj -c Release -o nupkg --no-build
if errorlevel 1 goto :error

dotnet pack src\FluentORM.Testing\FluentORM.Testing.csproj   -c Release -o nupkg --no-build
if errorlevel 1 goto :error

REM Meta-package (bundles all providers in one install)
dotnet pack src\FluentORM\FluentORM.csproj                   -c Release -o nupkg
if errorlevel 1 goto :error

echo.
echo ========================================
echo  Done! Generated packages:
echo ========================================
echo.
dir /b nupkg\*.nupkg
echo.
echo To publish all packages to NuGet.org:
echo   for %%f in (nupkg\*.nupkg) do dotnet nuget push "%%f" --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
echo.
goto :end

:error
echo.
echo ERROR: Build or pack failed!
echo.
pause
exit /b 1

:end
pause
exit /b 0
