# Details and raw HTML

## Collapsible details

In HTML preview this is collapsible; for Word it is flattened so the content is
preserved (summary becomes a bold heading).

<details>
<summary>Click to expand the configuration</summary>

Hidden content that must NOT be lost in Word export:

```yaml
database:
  type: SqlServer
  port: 1433
```

- point one
- point two

</details>

<details>
<summary>A second collapsible without code</summary>

Just a paragraph of hidden text with a [link](https://example.com) inside.

</details>

## Raw inline HTML

Text with <kbd>Ctrl</kbd> + <kbd>C</kbd> keys, an <abbr title="Hypertext Markup Language">HTML</abbr>
abbreviation, and <mark>highlighted</mark> text.

## Raw block HTML

<div style="padding:8px;border:1px solid #ccc;border-radius:6px">
  <strong>Boxed note</strong> rendered from raw block HTML.
</div>

## Multiple horizontal rules

Section one.

---

Section two.

***

Section three.
