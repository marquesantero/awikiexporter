# Build do Instalador (`AExportWikiSetup`)

## Pré-requisitos
- Visual Studio 2022 (com `devenv.com`)
- Extensão **Microsoft Visual Studio Installer Projects**

## Gerar instalador
No PowerShell, na raiz da solução:

```powershell
.\AExportWikiSetup\BuildInstaller.ps1 -Configuration Release
```

## Saída esperada
- `AExportWikiSetup\Release\AExportWikiSetup.msi`
- `AExportWikiSetup\Release\setup.exe` (quando bootstrapper for gerado)

## Observação
`dotnet build` não compila `.vdproj`; a etapa do setup depende do `devenv`.
