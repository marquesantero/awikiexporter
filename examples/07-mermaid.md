# Mermaid diagrams

These render live in the HTML preview and are rasterized to images for Word/PDF.

## Flowchart

```mermaid
flowchart TD
    A[Start] --> B{Has wiki?}
    B -- Yes --> C[Load pages]
    B -- No --> D[Open Wiki Management]
    C --> E[Preview]
    E --> F[Export Word/PDF]
```

## Sequence diagram

```mermaid
sequenceDiagram
    participant U as User
    participant A as App
    participant G as GitHub
    U->>A: Open folder
    A->>A: Enumerate *.md recursively
    A->>G: Fetch images
    G-->>A: Image bytes
    A-->>U: Rendered preview
```

## Pie chart

```mermaid
pie title Export formats
    "Word" : 45
    "PDF" : 40
    "HTML" : 15
```

## Azure DevOps `:::mermaid` container form

:::mermaid
graph LR
    X --> Y --> Z
:::
