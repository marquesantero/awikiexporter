# Math

## Inline math

The mass–energy equivalence is $E = mc^2$, and the quadratic root is
$x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}$.

A value with a subscript like $a_{i}$ or a superscript like $x^{2}$ should
render as math, while a plain `$5.00` price should not.

## Block math (display)

$$
\int_{0}^{\infty} e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}
$$

$$
\sum_{i=1}^{n} i = \frac{n(n+1)}{2}
$$

## Matrix

$$
A = \begin{bmatrix} 1 & 2 \\ 3 & 4 \end{bmatrix}
$$

## Math should be ignored inside code

```text
This $E = mc^2$ is literal, not math.
```
