# FluentORM NuGet Package Publication Guide

## Overview
This guide explains how to create and publish NuGet packages for the FluentORM projects.

## Projects Configured for NuGet Publishing

The following projects have been configured with NuGet metadata:

1. **FluentORM.Core** - Core ORM framework
2. **FluentORM.Migrations** - Database migration engine
3. **FluentORM.Sqlite** - SQLite provider
4. **FluentORM.SqlServer** - SQL Server provider
5. **FluentORM.Testing** - Testing utilities

## Prerequisites

1. **.NET 8 SDK** - Already targeting .NET 8
2. **NuGet Account** - Register at https://www.nuget.org/
3. **API Key** - Get from your NuGet account settings

## Step 1: Get Your NuGet API Key

1. Go to https://www.nuget.org/ and sign in
2. Click your profile → Settings → API Keys
3. Create a new API key with "Push" permission
4. Copy and save your API key securely

## Step 2: Update Version Numbers

Edit each `.csproj` file and update the `<Version>` tag:

```xml
<Version>1.0.0</Version>  <!-- Change this version number -->
```

Files to update:
- `src/FluentORM.Core/FluentORM.Core.csproj`
- `src/FluentORM.Migrations/FluentORM.Migrations.csproj`
- `src/FluentORM.Sqlite/FluentORM.Sqlite.csproj`
- `src/FluentORM.SqlServer/FluentORM.SqlServer.csproj`
- `src/FluentORM.Testing/FluentORM.Testing.csproj`

## Step 3: Create NuGet Packages

### Option A: Using PowerShell Script (Recommended)

From the repository root, run:

```powershell
.\pack-nuget.ps1
```

This will:
- Build all projects in Release mode
- Create `.nupkg` files in the `nupkg` folder
- Display the status of each package creation

### Option B: Manual Packing

Pack individual projects:

```powershell
dotnet pack src\FluentORM.Core\FluentORM.Core.csproj -c Release -o nupkg
dotnet pack src\FluentORM.Migrations\FluentORM.Migrations.csproj -c Release -o nupkg
dotnet pack src\FluentORM.Sqlite\FluentORM.Sqlite.csproj -c Release -o nupkg
dotnet pack src\FluentORM.SqlServer\FluentORM.SqlServer.csproj -c Release -o nupkg
dotnet pack src\FluentORM.Testing\FluentORM.Testing.csproj -c Release -o nupkg
```

## Step 4: Verify Packages

Check the generated `.nupkg` files:

```powershell
ls nupkg\*.nupkg
```

You should see files like:
- `FluentORM.Core.1.0.0.nupkg`
- `FluentORM.Migrations.1.0.0.nupkg`
- `FluentORM.Sqlite.1.0.0.nupkg`
- `FluentORM.SqlServer.1.0.0.nupkg`
- `FluentORM.Testing.1.0.0.nupkg`

## Step 5: Publish to NuGet

### Option A: Push All Packages at Once

```powershell
dotnet nuget push nupkg\*.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

### Option B: Push Individual Packages

```powershell
dotnet nuget push nupkg\FluentORM.Core.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

### Option C: Configure API Key Permanently (Optional)

Store your API key locally so you don't need to include it in every command:

```powershell
dotnet nuget update source nuget.org --username nuget --password YOUR_API_KEY --store-password-in-clear-text
```

Then push without the API key:

```powershell
dotnet nuget push nupkg\*.nupkg --source https://api.nuget.org/v3/index.json
```

## Step 6: Verify Publication

1. Visit https://www.nuget.org/packages/FluentORM.Core (or your package name)
2. Check that the version appears and all package details are correct
3. Try installing from NuGet in a test project:

```powershell
dotnet add package FluentORM.Core
```

## Package Metadata

Each project includes the following metadata:

- **Authors**: Marinus Maurice
- **License**: MIT
- **Repository**: https://github.com/marinusmaurice/FluentORM
- **Project URL**: https://github.com/marinusmaurice/FluentORM
- **Tags**: orm, database, fluent, sql, querybuilder (varies by package)

## Troubleshooting

### "Package with id and version already exists"
- The version already exists on NuGet. Increment the version number in the `.csproj` file.

### "API key is invalid"
- Verify your API key is correct and has push permissions.
- Check that you're using the correct NuGet source URL.

### "Assembly strong name validation failed"
- Ensure all dependencies are properly built before packing.
- Run: `dotnet clean` then try again.

### "Dependency version conflict"
- Check that all referenced packages in `.csproj` files are compatible.
- Ensure FluentORM.Migrations depends on FluentORM.Core, etc.

## Version Management

Follow **Semantic Versioning**:
- **MAJOR.MINOR.PATCH** (e.g., 1.0.0, 1.1.0, 1.0.1)
- MAJOR: Breaking changes
- MINOR: New features (backward compatible)
- PATCH: Bug fixes

## Next Steps

After publishing:
1. Add badges to your README
2. Update documentation with installation instructions
3. Create GitHub releases for each version
4. Consider using CI/CD to automate publishing

## Resources

- NuGet Official Docs: https://learn.microsoft.com/en-us/nuget/
- Semantic Versioning: https://semver.org/
- GitHub Actions for NuGet: https://github.com/marketplace/actions/nuget-push

