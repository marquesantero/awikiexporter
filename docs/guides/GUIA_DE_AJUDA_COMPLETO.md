# Guia de Ajuda Completo - ExportAzureWiki

## 1. Visão Geral

O **ExportAzureWiki** é uma aplicação desktop para:

- conectar em wikis (principalmente Azure DevOps),
- visualizar conteúdo com suporte a Markdown e HTML misto,
- exportar documentação para PDF e Word,
- gerenciar autenticação e autorização para acesso controlado.

Objetivo principal: facilitar distribuição de documentação para usuários que não precisam de acesso direto à wiki de origem.

---

## 2. Requisitos

## 2.1 Ambiente

- Windows 10/11
- .NET 8 SDK/Runtime
- WebView2 Runtime instalado

## 2.2 Opcional

- Banco externo (SQL Server, PostgreSQL, MySQL), se desejar substituir SQLite
- Conta/provedor OAuth para login federado (Azure AD, Microsoft, GitHub ou Google)

---

## 3. Primeira Execução (Onboarding)

Na primeira execução, o assistente de configuração abre automaticamente.

## 3.1 Banco de Dados

- Se nenhum banco for definido, a aplicação usa **SQLite por padrão**.
- O SQLite é suficiente para iniciar e operar normalmente.
- Se você escolher outro banco, o schema é criado automaticamente nesse banco.

## 3.2 Usuário Administrador

- Você define um usuário administrador inicial.
- Esse usuário permite acesso às telas administrativas de autenticação/permissões.

## 3.3 Método de Autenticação

Você pode habilitar:

- Local (usuário/senha da aplicação),
- Windows,
- Azure AD,
- OAuth providers.

## 3.4 Provedores OAuth no Onboarding

Durante o onboarding, o passo de provedores permite configurar:

- Azure AD
- Microsoft
- GitHub
- Google

Cada provedor possui **formulário próprio** com campos fixos.

---

## 4. Autenticação e Login

## 4.1 Tela de Login

A tela mostra apenas opções válidas:

- se houver provedores configurados, eles aparecem;
- se não houver, o login local permanece disponível (quando habilitado).

## 4.2 Sempre pedir login

A aplicação pode ser configurada para:

- manter sessão entre aberturas, ou
- sempre exigir login ao iniciar.

## 4.3 Logout

No logout, a sessão atual é encerrada e o status de autenticação é atualizado.

---

## 5. Configuração de Provedores OAuth

Menu de configuração permite:

- adicionar provedor,
- editar credenciais,
- ativar/desativar,
- excluir provedor.

Para cada provider há uma tela específica:

- `AzureAdProviderForm`
- `MicrosoftProviderForm`
- `GitHubProviderForm`
- `GoogleProviderForm`

Campos típicos:

- Client ID
- Client Secret (quando aplicável)
- Tenant ID (Azure AD)
- Redirect URI
- Scopes

---

## 6. Gestão de Usuários, Grupos e Permissões

## 6.1 Usuários e Grupos

A tela administrativa permite:

- listar usuários e grupos,
- ajustar vínculos de usuário-grupo,
- alterar grupos/membros.

## 6.2 Permissões

Permissões podem ser atribuídas por wiki/grupo com níveis de acesso.

Boas práticas:

- evitar uso excessivo de permissões amplas,
- manter grupos funcionais por contexto de projeto/cliente,
- revisar permissões periodicamente.

---

## 7. Conexão com Wiki e Visualização

Fluxo padrão:

1. selecionar/configurar wiki,
2. carregar árvore de páginas,
3. visualizar conteúdo no painel principal.

Renderização atual contempla:

- Markdown,
- blocos HTML mistos,
- tabelas,
- Mermaid (com suporte aos formatos utilizados no Azure Wiki).

---

## 8. Exportação

## 8.1 PDF

- Exporta conteúdo renderizado para PDF.
- Em cenários visuais complexos, pode usar captura visual para preservar estética.

## 8.2 Word

- Exporta para `.docx`.
- Conteúdo híbrido (Markdown + HTML + Mermaid) pode exigir ajustes visuais conforme a origem.

## 8.3 Recomendações

- validar sempre um documento de teste antes de enviar ao cliente,
- priorizar tema e largura de conteúdo coerentes com o destino (PDF/Word),
- para documentos muito grandes, exportar por seções.

---

## 9. Temas e Aparência

- Suporte a tema claro/escuro.
- Alterações de tema devem refletir no conteúdo sem necessidade de recarga manual.
- Caso observe flicker em transições, revisar configurações de WebView2 e pré-renderização.

---

## 10. Internacionalização (i18n)

A aplicação possui suporte a:

- Português
- Inglês

Itens principais já internacionalizados:

- menus e botões principais,
- mensagens comuns e diálogos,
- telas de provedores OAuth.

Se algum texto aparecer em idioma incorreto:

1. trocar idioma e voltar,
2. reabrir a tela,
3. reportar chave/texto específico para ajuste.

---

## 11. Estrutura de Dados e Configuração

## 11.1 Persistência principal

- Configurações operacionais ficam no banco de dados.
- SQLite é o padrão inicial para reduzir fricção de setup.

## 11.2 Configuração mínima de bootstrap

- Apenas o estritamente necessário para iniciar e conectar ao banco antes da aplicação estar completa.

---

## 12. Solução de Problemas (Troubleshooting)

## 12.1 Erro ao abrir tela de provedores (`invalid object name`)

Causa comum:

- schema não atualizado ou tabela com nome divergente no banco.

Ações:

1. validar criação de schema,
2. verificar scripts/mapeamento de nomes,
3. confirmar banco correto na configuração ativa.

## 12.2 Erros em exportação PDF (iText/SVG)

Causa comum:

- SVG gerado com atributos incompatíveis.

Ações:

1. testar exportação com fallback visual,
2. remover/normalizar marcadores SVG problemáticos,
3. validar trecho HTML/SVG antes da conversão.

## 12.3 Mermaid não renderiza corretamente

Ações:

1. confirmar blocos Mermaid nos formatos suportados,
2. validar carregamento do script Mermaid,
3. testar renderização na visualização antes da exportação.

## 12.4 Aplicação fecha/trava ao exportar

Ações:

1. revisar logs da aplicação,
2. reduzir tamanho da página exportada (teste por seção),
3. reexecutar com tema claro para isolar fator visual,
4. verificar operação em thread de UI quando houver WebView oculto.

---

## 13. Logs e Diagnóstico

Os logs ajudam a investigar:

- falhas de migração,
- erros de exportação,
- exceções não tratadas,
- problemas de autenticação e sessão.

Sempre que abrir chamado interno, anexe:

- horário aproximado do erro,
- ação executada,
- stack trace,
- arquivo de log mais recente.

---

## 14. Operação Recomendada em Produção

## 14.1 Segurança

- usar senha forte para admin local,
- habilitar OAuth corporativo quando possível,
- revisar permissões por grupo periodicamente.

## 14.2 Governança

- definir responsável por configurações de provider,
- definir padrão de nomes para wikis/configs,
- manter rotina de backup do banco.

## 14.3 Qualidade de Exportação

- manter um conjunto de páginas de referência para regressão visual,
- revisar documentos gerados após alterações de renderização.

---

## 15. Fluxo Rápido de Uso

1. Abrir aplicação.
2. Fazer login (se exigido).
3. Selecionar wiki e página.
4. Validar conteúdo na visualização.
5. Exportar para PDF/Word.
6. Revisar saída final.
7. Distribuir documentação.

---

## 16. Checklist de Publicação

- setup inicial concluído sem erros,
- autenticação validada para cenários local + provider,
- permissões revisadas,
- exportação PDF e Word validada em páginas complexas,
- internacionalização validada em PT/EN,
- logs sem erros críticos recorrentes.

---

## 17. Contatos e Manutenção

Para manutenção técnica, registrar no chamado:

- versão da aplicação,
- tipo de banco em uso,
- idioma ativo,
- provedor de autenticação utilizado,
- print/erro reproduzível.

Com isso, o diagnóstico e correção ficam muito mais rápidos.
