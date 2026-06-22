Offline web assets for markdown preview.

Put these files here to ship with the installer:

1. `style/vendor/mermaid/mermaid.min.js`
2. `style/vendor/mathjax/tex-svg.js`

Download/update them with:

```powershell
pwsh -File ExportAzureWiki/style/vendor/download-assets.ps1
```

Force re-download:

```powershell
pwsh -File ExportAzureWiki/style/vendor/download-assets.ps1 -Force
```

Resolution order used by the app:

1. Project vendor path (this folder, copied to output)
2. Local cache (`%LOCALAPPDATA%/ExportAzureWiki/Cache/WebAssets`)
3. CDN (last fallback)
