# Nested file (recursion test)

This file lives in `examples/subfolder/`. It exists to confirm that **Open
folder** gathers Markdown files **recursively**, not just the top-level ones.

When loaded, its navigation title should read `subfolder/nested-deep.md`.

## A bit of everything

> [!NOTE]
> If you can see this callout, recursion + alert rendering both work.

| Check | Result |
| --- | --- |
| Found in subfolder | ✅ |
| Relative path shown | ✅ |

```python
def deep():
    return "rendered from a nested folder"
```
