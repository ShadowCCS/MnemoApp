# LaTeX Rendering Examples

## Basic Usage in RichContentView

The RichContentView automatically renders LaTeX when it encounters `$...$` (inline) or `$$...$$` (display) delimiters:

```csharp
var richContent = new RichContentView
{
    Source = "The quadratic formula is $x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}$"
};
```

## Direct Usage

```csharp
using MnemoApp.Core.LaTeX;

// Simple expression
var control1 = LaTeXEngine.Render("x^2 + y^2 = r^2");

// Complex fraction
var control2 = LaTeXEngine.Render("\\frac{\\partial f}{\\partial x}");

// With Greek letters
var control3 = LaTeXEngine.Render("\\alpha + \\beta = \\gamma");

// Subscripts and superscripts
var control4 = LaTeXEngine.Render("x_i^2 + y_j^2");
```

## Example Expressions

### Algebra
```latex
x^2 + 2xy + y^2 = (x + y)^2
```

### Calculus
```latex
\\int_0^\\infty e^{-x^2} dx = \\frac{\\sqrt{\\pi}}{2}
```

### Linear Algebra
```latex
\\lambda_1 + \\lambda_2 = tr(A)
```

### Physics
```latex
E = mc^2
\\hbar\\omega = \\frac{h}{2\\pi}
```

### Set Theory
```latex
A \\cup B = \\{x : x \\in A \\vee x \\in B\\}
```

### Number Theory
```latex
\\sum_{i=1}^{n} i = \\frac{n(n+1)}{2}
```

### Complex Expressions
```latex
f(x) = \\left(\\frac{x^2 + 1}{x - 1}\\right)^{\\frac{1}{2}}
```

```latex
\\prod_{k=1}^{n} \\left(1 + \\frac{1}{k}\\right) = n + 1
```

## Supported Commands

### Structure
- `\frac{num}{denom}` - Fractions
- `\sqrt{x}` - Square root
- `\left( ... \right)` - Auto-sized delimiters
- `{...}` - Grouping
- `_` - Subscript
- `^` - Superscript

### Greek Letters (lowercase)
α β γ δ ε ζ η θ ι κ λ μ ν ξ π ρ σ τ υ φ χ ψ ω

`\alpha \beta \gamma \delta \epsilon \zeta \eta \theta \iota \kappa \lambda \mu \nu \xi \pi \rho \sigma \tau \upsilon \phi \chi \psi \omega`

### Greek Letters (uppercase)
Γ Δ Θ Λ Ξ Π Σ Φ Ψ Ω

`\Gamma \Delta \Theta \Lambda \Xi \Pi \Sigma \Phi \Psi \Omega`

### Binary Operators
× ÷ ± ∓ ⋅ ∗ ⋆ ∘ •

`\times \div \pm \mp \cdot \ast \star \circ \bullet`

### Relations
≤ ≥ ≠ ≈ ≡ ∼ ∝ ≪ ≫

`\leq \geq \neq \approx \equiv \sim \propto \ll \gg`

### Arrows
→ ← ⇒ ⇐ ↔ ⇔

`\rightarrow \leftarrow \Rightarrow \Leftarrow \leftrightarrow \Leftrightarrow \to`

### Set Theory
∈ ∉ ⊂ ⊃ ⊆ ⊇ ∪ ∩ ∅

`\in \notin \subset \supset \subseteq \supseteq \cup \cap \emptyset \varnothing`

### Calculus & Analysis
∞ ∂ ∇ ∫ ∑ ∏

`\infty \partial \nabla \int \sum \prod`

### Logic
∀ ∃ ¬ ∧ ∨

`\forall \exists \neg \wedge \vee`

### Miscellaneous
ℏ ℓ ℜ ℑ ∠ △ □ ′

`\hbar \ell \Re \Im \angle \triangle \square \prime`

## Tips

1. **Escaping**: Use `\\` for backslash in strings: `"\\frac{1}{2}"`
2. **Grouping**: Always use braces for multi-character scripts: `x^{10}` not `x^10`
3. **Spacing**: The renderer handles spacing automatically
4. **Nesting**: Commands can be nested arbitrarily: `\\frac{x^2}{\\sqrt{y}}`
5. **Error Handling**: Invalid LaTeX shows red error message with details

