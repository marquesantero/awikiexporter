# Lists, tasks and footnotes

## Unordered list

- First
- Second
  - Nested 2.1
  - Nested 2.2
    - Deep 2.2.1
- Third

## Ordered list

1. Step one
2. Step two
   1. Sub-step a
   2. Sub-step b
3. Step three

## Mixed list with content

1. Install dependencies

   ```bash
   dotnet restore
   ```

2. Run the build

   > Tip: use `-c Release` for production.

## Task list

- [x] Write the parser
- [x] Render to HTML
- [ ] Render to Word
- [ ] Render to PDF
  - [x] Sub-task done
  - [ ] Sub-task pending

## Definition list

Term A
: Definition of term A.

Term B
: Definition of term B, which can be longer and wrap across multiple lines as
needed.

## Footnotes

Here is a statement that needs a citation.[^1] And another one.[^note]

[^1]: The first footnote definition.
[^note]: Footnotes can contain `code` and [links](https://example.com).
