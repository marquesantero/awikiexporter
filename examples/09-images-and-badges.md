# Images and badges

Covers the image formats the renderer must handle, including the ones that need
rasterization for Word (SVG, WebP).

## Badge row (SVG from shields.io)

These are SVG images; they must be rasterized to PNG for the Word export.

![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)
![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)
![Platform](https://img.shields.io/badge/platform-Windows-informational.svg)

## PNG image

![Markdown logo (PNG)](https://upload.wikimedia.org/wikipedia/commons/4/48/Markdown-mark.svg)

## Remote WebP image

![Google WebP sample](https://www.gstatic.com/webp/gallery/1.webp)

## Inline SVG

<svg xmlns="http://www.w3.org/2000/svg" width="120" height="60">
  <rect width="120" height="60" rx="8" fill="#0F6CBD"/>
  <text x="60" y="36" font-family="Segoe UI, Arial" font-size="16"
        fill="#ffffff" text-anchor="middle">SVG</text>
</svg>

## Data URI image (tiny PNG)

![dot](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==)

## Image with explicit size

<img src="https://img.shields.io/badge/sized-200px-orange.svg" width="200" alt="sized badge" />

## Linked image

[![clickable badge](https://img.shields.io/badge/click-me-purple.svg)](https://example.com)
