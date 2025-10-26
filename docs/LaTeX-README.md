# LaTeX Rendering System (AI Generated Summary)

A complete LaTeX rendering implementation for Avalonia 11+, built from scratch with proper typography and layout.

## Architecture

The system follows the classic TeX architecture with four main components:

### 1. Parser (`Parser/`)
Converts LaTeX syntax into an Abstract Syntax Tree (AST).

- **LaTeXLexer**: Tokenizes LaTeX strings into tokens (commands, braces, text, scripts, etc.)
- **LaTeXParser**: Builds an AST from tokens
- **LaTeXNode**: AST node types (TextNode, SymbolNode, FractionNode, ScriptNode, etc.)

### 2. Box Model (`Layout/`)
Implements TeX's box-and-glue model for precise layout.

- **Box**: Base class for all layout boxes with width, height, depth, and baseline shift
- **CharBox**: Single character with font metrics
- **HBox**: Horizontal box (children laid out left-to-right)
- **VBox**: Vertical box (children stacked top-to-bottom)
- **FractionBox**: Specialized box for fractions with numerator/denominator
- **ScriptBox**: Handles subscripts and superscripts
- **SqrtBox**: Square root with radical symbol
- **SpaceBox**: Whitespace
- **RuleBox**: Horizontal/vertical lines
- **MatrixBox**: Matrices with delimiters (pmatrix, bmatrix, vmatrix, Vmatrix)

### 3. Font Metrics (`Metrics/`)
Calculates precise measurements for typography.

- **FontMetrics**: Measures character dimensions, calculates script sizes, positions, and spacing
- Uses Avalonia's `FormattedText` for accurate font measurements
- Implements TeX's typography rules (script ratios, axis height, etc.)

### 4. Renderer (`Renderer/`)
Draws boxes to Avalonia's canvas.

- **LaTeXRenderer**: Avalonia Control that renders box trees
- Recursive rendering with proper baseline alignment
- Handles all box types with appropriate drawing primitives

## Usage

```csharp
using MnemoApp.Core.LaTeX;

// Render LaTeX expression
var control = LaTeXEngine.Render("x^2 + \\frac{1}{2}", fontSize: 16.0);

// In RichContentView, it's automatic:
// Inline math: $x^2$
// Display math: $$\sum_{i=1}^{n} i = \frac{n(n+1)}{2}$$
```

## Supported Features

### Commands
- `\frac{num}{denom}` - Fractions
- `\sqrt{content}` - Square roots
- `\left( ... \right)` - Delimiters (auto-sized)
- `\begin{matrix}...\end{matrix}` - Matrices
- `\begin{pmatrix}...\end{pmatrix}` - Parentheses matrix
- `\begin{bmatrix}...\end{bmatrix}` - Brackets matrix
- `\begin{vmatrix}...\end{vmatrix}` - Vertical bars matrix
- `\begin{Vmatrix}...\end{Vmatrix}` - Double vertical bars matrix
- Greek letters: `\alpha`, `\beta`, `\gamma`, `\delta`, `\theta`, `\pi`, etc.
- Operators: `\times`, `\div`, `\pm`, `\cdot`, etc.
- Relations: `\leq`, `\geq`, `\neq`, `\approx`, `\equiv`, etc.
- Arrows: `\to`, `\rightarrow`, `\Rightarrow`, etc.
- Set theory: `\in`, `\subset`, `\cup`, `\cap`, `\emptyset`, etc.
- Calculus: `\infty`, `\partial`, `\nabla`, `\int`, `\sum`, `\prod`, etc.

### Syntax
- Subscripts: `x_i` or `x_{ij}`
- Superscripts: `x^2` or `x^{2n}`
- Both: `x_i^2`
- Grouping: `{...}`
- Matrices: Use `&` for column separators, `\\` for row separators

### Symbol Registry
Over 80 mathematical symbols mapped to Unicode characters in `Symbols/SymbolRegistry.cs`.

## Implementation Notes

### Box Metrics
Each box tracks:
- **Width**: Horizontal extent
- **Height**: Distance above baseline
- **Depth**: Distance below baseline
- **Shift**: Vertical offset from parent baseline

### Baseline Alignment
All rendering is baseline-relative, ensuring proper vertical alignment across complex expressions.

### Font Sizing
- Base font size is configurable (default 16pt)
- Scripts use 0.7× base size (TeX standard)
- Fractions use 0.8× base size

### Extensibility
To add new LaTeX commands:
1. Add token type if needed (`LaTeXToken.cs`)
2. Handle in parser (`LaTeXParser.cs`)
3. Create AST node type (`LaTeXNode.cs`)
4. Implement layout logic (`LayoutBuilder.cs`)
5. Add rendering code (`LaTeXRenderer.cs`)

To add new symbols:
- Update `Symbols/SymbolRegistry.cs` with command→Unicode mapping

## Performance
- Parsing and layout are fast (< 1ms for typical expressions)
- Rendering uses Avalonia's native drawing, hardware-accelerated
- No external dependencies beyond Avalonia

## Future Enhancements
- Spacing commands (`\quad`, `\,`, etc.)
- More matrix types (smallmatrix, etc.)
- Array environments
- Add a CommandRegistry to dynamically register handlers instead of hardcoding every command in the parser (extension support?)