# AWiki Exporter — Plano de Melhorias

Roadmap derivado da análise da aplicação. Ordenado por **risco** e por
dependências (itens que reduzem retrabalho vêm antes). Esforço: **S** (≤1 dia),
**M** (2–4 dias), **L** (≥1 semana). Cada item tem critério de aceite (DoD).

Ordem de execução recomendada:
**M1 → M3.1 → M4.1 → M2 → M3.2/M3.3 → M5 → M6 → M4.2/M4.3 → M7.**

---

## M1 — Riscos críticos (privacidade + estabilidade)

### M1.1 Eliminar dependência de `mermaid.ink` (render local) — L — risco alto
- **Problema:** no export Word/PDF o código do diagrama é enviado a `mermaid.ink`
  (`ExportService.Preprocessing.cs` → `TryBuildMermaidInkUrl`, usado no Word e em
  `NormalizeMermaidForPdf`). Vazamento de conteúdo corporativo + falha offline.
- **Escopo:** renderizar Mermaid localmente via WebView2 offscreen (já embarcamos
  `style/vendor/mermaid/mermaid.min.js`): carregar o diagrama, exportar o SVG
  resultante e rasterizar para PNG (reaproveitar `RasterImageConverter.SvgToPng`).
  Remover todas as chamadas a mermaid.ink. Fallback: manter o bloco como código.
- **Arquivos:** `ExportService.Preprocessing.cs`, `WorkspaceView.xaml.cs` (host
  WebView2 offscreen), novo `MermaidRenderService`.
- **DoD:** export Word/PDF de `examples/07-mermaid.md` sem nenhuma requisição de
  rede; diagramas aparecem como imagem; teste cobrindo "nenhuma URL externa".

### M1.2 Async-all-the-way no data/auth — L — risco alto
- **Problema:** `CreateConnectionAsync().GetAwaiter().GetResult()` / `.Result`
  espalhados (`AuthenticationService` 432/504/530/582, `AuthorizationService`,
  `UnitOfWork`, `AuthenticationProviderBootstrapper`). Deadlock-prone na UI thread.
- **Escopo:** tornar repositórios/UoW assíncronos de ponta a ponta; eliminar
  sync-over-async; `ConfigureAwait(false)` nas bibliotecas. Onde a API pública
  precisar continuar síncrona, isolar num único helper seguro.
- **DoD:** zero ocorrências de `.GetAwaiter().GetResult()`/`.Result`/`.Wait()` no
  data/auth layer; login/setup/admin sem travar; testes de fumaça por engine.

### M1.3 Modo de export offline (somente cache) — M — risco médio
- **Problema:** imagens remotas (badges etc.) são baixadas no export; sem rede o
  documento sai incompleto/lento.
- **Escopo:** opção "somente cache" no export; quando ligada, usa só imagens já
  baixadas e marca ausências; timeouts curtos por padrão.
- **DoD:** export reproduzível offline; opção exposta na UI + persistida.

---

## M2 — Distribuição corporativa

### M2.1 Auto-update via `.appinstaller` — M
- **Escopo:** gerar `.appinstaller` no pipeline de release apontando para o
  feed interno (UNC/HTTP), com política de checagem automática; documentar.
- **Arquivos:** `tools/package/*`, workflow de release, `docs/INSTALL.md`.
- **DoD:** instalar a v1 e receber atualização automática para v1.1 sem reinstalar.

### M2.2 Garantir runtime do WebView2 — S
- **Escopo:** detectar Evergreen runtime ausente com mensagem clara; documentar
  bootstrapper/empacotamento para máquinas sem internet.
- **DoD:** primeira execução em máquina limpa funciona ou orienta a instalação.

---

## M3 — Manutenibilidade

### M3.1 Externalizar localização — M — habilitador (fazer cedo)
- **Problema:** `LocalizationManager.cs` tem ~213 KB de dicionário PT/EN
  hardcoded; chaves faltantes causaram vários bugs (fallback em inglês).
- **Escopo:** migrar para `.resx`/JSON por idioma; manter a fachada `AppText`;
  **teste que falha se uma chave existir em um idioma e faltar no outro**.
- **DoD:** dicionário externo; teste de paridade de chaves verde; sem regressão
  de textos.

### M3.2 De-duplicar `HtmlContentGenerator` — M
- **Escopo:** extrair um único `BuildPageHtml(...)` (template + CSS + scripts);
  hoje há dois blocos quase idênticos (~120 linhas) em `GenerateContentAsync` e
  `GenerateContentFromMarkdownAsync`.
- **DoD:** uma única fonte do template; render idêntico antes/depois (snapshot).

### M3.3 Quebrar `WorkspaceViewModel` — M
- **Escopo:** separar responsabilidades (workspace/preview, export, IA,
  navegação, sessões de aba) em colaboradores; `WorkspaceViewModel` < 30 KB.
- **DoD:** mesmas funcionalidades; testes de VM (ver M4.2) passando.

---

## M4 — Qualidade e testes

### M4.1 Testes de integração de banco (Testcontainers) — M — fazer cedo
- **Problema:** criação de schema/seed só validada à mão (origem dos bugs de
  OAuth seed e do deadlock).
- **Escopo:** Testcontainers para SQL Server/PostgreSQL/MySQL: criar DB, rodar
  schema, seed, CRUD básico e login.
- **DoD:** job de CI (opcional/nightly) verde nos 3 engines.

### M4.2 Testes de ViewModel — M
- **Escopo:** cobrir workspace: troca de aba (escopo por aba), abrir arquivo/
  pasta, navegação, escopo de export.
- **DoD:** cobertura significativa dos fluxos de UI lógica.

### M4.3 Gate de cobertura no CI — S
- **Escopo:** coletar cobertura (coverlet) e falhar abaixo de um limiar acordado.
- **DoD:** PR falha se cobertura cair.

---

## M5 — Fontes de wiki

### M5.1 Finalizar GitLab + Bitbucket — M
- **Escopo:** concluir os providers já esboçados (campos, auth, listagem,
  conteúdo, imagens) ou ocultá-los até prontos.
- **DoD:** conectar, listar e exportar de um repo real de cada.

### M5.2 Confluence — L
- **Escopo:** provider Confluence (Cloud/Server) via API; mapear espaços/páginas.
- **DoD:** listar e exportar um espaço.

### M5.3 SharePoint / OneNote — L
- **Escopo:** avaliar Graph API; provider de páginas/seções.
- **DoD:** PoC de listagem + export de uma seção.

### M5.4 Importar wiki em `.zip` — S
- **Escopo:** abrir um zip de markdown como fonte local (reaproveita o fluxo de
  pasta recursiva).
- **DoD:** abrir um `.zip` e navegar/exportar.

---

## M6 — UX e acessibilidade

### M6.1 Acessibilidade — M
- **Escopo:** `AutomationProperties` (nome/rótulo) nos controles, navegação por
  teclado, foco visível, contraste.
- **DoD:** varredura de acessibilidade sem erros críticos; navegável só por teclado.

### M6.2 Busca/filtro nas árvores (online e local) — S
- **DoD:** filtrar páginas por texto em ambas as árvores.

### M6.3 Persistir preferências — S
- **Escopo:** último wiki, tema de código, dark mode, última pasta local, última aba.
- **DoD:** preferências sobrevivem ao reinício.

### M6.4 Render de pasta sob demanda — M
- **Problema:** abrir pasta renderiza tudo de uma vez (lento em pastas grandes).
- **Escopo:** listar a árvore imediatamente e renderizar a página só ao
  selecionar; manter "exportar todas" rendendo em lote com progresso.
- **DoD:** abrir pasta grande é instantâneo; seleção renderiza on-demand.

---

## M7 — Operação

### M7.1 Captura global de exceções + crash report — S
- **Escopo:** handlers de `DispatcherUnhandledException`/`AppDomain`/`TaskScheduler`;
  gerar bundle (reusa `DiagnosticBundleService`) e mensagem amigável.
- **DoD:** exceção não tratada não fecha em silêncio; bundle gerado.

### M7.2 Changelog/versionamento automatizados — S
- **Escopo:** versão semântica + changelog no pipeline de release.
- **DoD:** release publica notas e versão automaticamente.

---

## Sequência sugerida por entrega

1. **Release A (estabilidade/privacidade):** M1.1, M1.2, M1.3.
2. **Release B (base saudável):** M3.1, M4.1, M2.1, M2.2.
3. **Release C (limpeza):** M3.2, M3.3, M6.4.
4. **Release D (fontes):** M5.1 → M5.4 → M5.2 → M5.3.
5. **Release E (polimento/ops):** M6.1–M6.3, M4.2, M4.3, M7.1, M7.2.
