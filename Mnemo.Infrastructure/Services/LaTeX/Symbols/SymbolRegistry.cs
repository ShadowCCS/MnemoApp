using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Mnemo.Infrastructure.Services.LaTeX.Symbols;

public static class SymbolRegistry
{
    private static readonly ConcurrentDictionary<string, string> _symbols = new()
    {
        // Greek lowercase
        ["alpha"] = "α",
        ["beta"] = "β",
        ["gamma"] = "γ",
        ["delta"] = "δ",
        ["epsilon"] = "ε",
        ["zeta"] = "ζ",
        ["eta"] = "η",
        ["theta"] = "θ",
        ["iota"] = "ι",
        ["kappa"] = "κ",
        ["lambda"] = "λ",
        ["mu"] = "μ",
        ["nu"] = "ν",
        ["xi"] = "ξ",
        ["pi"] = "π",
        ["rho"] = "ρ",
        ["sigma"] = "σ",
        ["tau"] = "τ",
        ["upsilon"] = "υ",
        ["phi"] = "φ",
        ["chi"] = "χ",
        ["psi"] = "ψ",
        ["omega"] = "ω",

        // Greek uppercase
        ["Gamma"] = "Γ",
        ["Delta"] = "Δ",
        ["Theta"] = "Θ",
        ["Lambda"] = "Λ",
        ["Xi"] = "Ξ",
        ["Pi"] = "Π",
        ["Sigma"] = "Σ",
        ["Phi"] = "Φ",
        ["Psi"] = "Ψ",
        ["Omega"] = "Ω",

        // Mathematical operators
        ["times"] = "×",
        ["div"] = "÷",
        ["pm"] = "±",
        ["mp"] = "∓",
        ["cdot"] = "⋅",
        ["ast"] = "∗",
        ["star"] = "⋆",
        ["circ"] = "∘",
        ["bullet"] = "•",

        // Relations
        ["leq"] = "≤",
        ["geq"] = "≥",
        ["neq"] = "≠",
        ["approx"] = "≈",
        ["equiv"] = "≡",
        ["sim"] = "∼",
        ["propto"] = "∝",
        ["ll"] = "≪",
        ["gg"] = "≫",

        // Arrows
        ["rightarrow"] = "→",
        ["leftarrow"] = "←",
        ["Rightarrow"] = "⇒",
        ["Leftarrow"] = "⇐",
        ["leftrightarrow"] = "↔",
        ["Leftrightarrow"] = "⇔",
        ["to"] = "→",

        // Set theory
        ["in"] = "∈",
        ["notin"] = "∉",
        ["subset"] = "⊂",
        ["supset"] = "⊃",
        ["subseteq"] = "⊆",
        ["supseteq"] = "⊇",
        ["cup"] = "∪",
        ["cap"] = "∩",
        ["emptyset"] = "∅",
        ["varnothing"] = "∅",

        // Calculus
        ["infty"] = "∞",
        ["partial"] = "∂",
        ["nabla"] = "∇",
        ["int"] = "∫",
        ["sum"] = "∑",
        ["prod"] = "∏",

        // Logic
        ["forall"] = "∀",
        ["exists"] = "∃",
        ["neg"] = "¬",
        ["wedge"] = "∧",
        ["vee"] = "∨",

        // Miscellaneous
        ["hbar"] = "ℏ",
        ["ell"] = "ℓ",
        ["Re"] = "ℜ",
        ["Im"] = "ℑ",
        ["angle"] = "∠",
        ["triangle"] = "△",
        ["square"] = "□",
        ["prime"] = "′",
        ["degree"] = "°",
        ["backslash"] = "\\",
        ["mid"] = "|",
        ["parallel"] = "∥",
        ["perp"] = "⊥",

        // Additional Greek letters
        ["varepsilon"] = "ε",
        ["varphi"] = "φ",
        ["vartheta"] = "ϑ",
        ["varpi"] = "ϖ",
        ["varrho"] = "ϱ",
        ["varsigma"] = "ς",

        // Binary operators
        ["oplus"] = "⊕",
        ["ominus"] = "⊖",
        ["otimes"] = "⊗",
        ["oslash"] = "⊘",
        ["odot"] = "⊙",
        ["dagger"] = "†",
        ["ddagger"] = "‡",
        ["amalg"] = "⨿",
        ["bigcirc"] = "◯",
        ["bigtriangleup"] = "△",
        ["bigtriangledown"] = "▽",
        ["sqcup"] = "⊔",
        ["sqcap"] = "⊓",
        ["uplus"] = "⊎",
        ["wr"] = "≀",
        ["setminus"] = "∖",

        // Relations
        ["cong"] = "≅",
        ["simeq"] = "≃",
        ["asymp"] = "≍",
        ["doteq"] = "≐",
        ["prec"] = "≺",
        ["succ"] = "≻",
        ["preceq"] = "⪯",
        ["succeq"] = "⪰",
        ["sqsubseteq"] = "⊑",
        ["sqsupseteq"] = "⊒",
        ["models"] = "⊨",
        ["vdash"] = "⊢",
        ["dashv"] = "⊣",
        ["bowtie"] = "⋈",

        // Arrows
        ["uparrow"] = "↑",
        ["downarrow"] = "↓",
        ["updownarrow"] = "↕",
        ["Uparrow"] = "⇑",
        ["Downarrow"] = "⇓",
        ["Updownarrow"] = "⇕",
        ["nearrow"] = "↗",
        ["searrow"] = "↘",
        ["swarrow"] = "↙",
        ["nwarrow"] = "↖",
        ["mapsto"] = "↦",
        ["hookleftarrow"] = "↩",
        ["hookrightarrow"] = "↪",
        ["longrightarrow"] = "⟶",
        ["longleftarrow"] = "⟵",
        ["longleftrightarrow"] = "⟷",
        ["Longrightarrow"] = "⟹",
        ["Longleftarrow"] = "⟸",
        ["Longleftrightarrow"] = "⟺",

        // Large operators
        ["bigcup"] = "⋃",
        ["bigcap"] = "⋂",
        ["bigoplus"] = "⨁",
        ["bigotimes"] = "⨂",
        ["bigodot"] = "⨀",
        ["biguplus"] = "⨄",
        ["bigsqcup"] = "⨆",
        ["coprod"] = "∐",
        ["oint"] = "∮",
        ["iint"] = "∬",
        ["iiint"] = "∭",

        // Delimiters
        ["langle"] = "⟨",
        ["rangle"] = "⟩",
        ["lfloor"] = "⌊",
        ["rfloor"] = "⌋",
        ["lceil"] = "⌈",
        ["rceil"] = "⌉",
        ["lbrace"] = "{",
        ["rbrace"] = "}",
        ["lbrack"] = "[",
        ["rbrack"] = "]",

        // Additional symbols
        ["aleph"] = "ℵ",
        ["beth"] = "ℶ",
        ["gimel"] = "ℷ",
        ["daleth"] = "ℸ",
        ["wp"] = "℘",
        ["complement"] = "∁",
        ["top"] = "⊤",
        ["bot"] = "⊥",
        ["flat"] = "♭",
        ["natural"] = "♮",
        ["sharp"] = "♯",
        ["clubsuit"] = "♣",
        ["diamondsuit"] = "♢",
        ["heartsuit"] = "♡",
        ["spadesuit"] = "♠",
        ["checkmark"] = "✓",
        ["maltese"] = "✠",

        // Dots
        ["cdots"] = "⋯",
        ["ldots"] = "…",
        ["vdots"] = "⋮",
        ["ddots"] = "⋱",
        ["dots"] = "…",

        // Blackboard bold (mathbb)
        ["mathbb{R}"] = "ℝ",
        ["mathbb{N}"] = "ℕ",
        ["mathbb{Z}"] = "ℤ",
        ["mathbb{Q}"] = "ℚ",
        ["mathbb{C}"] = "ℂ",
        ["mathbb{P}"] = "ℙ",
        ["mathbb{F}"] = "𝔽",
        ["mathbb{H}"] = "ℍ",
        ["mathbb{A}"] = "𝔸",
        ["mathbb{E}"] = "𝔼",

        // Bold (mathbf) - Mathematical Bold characters
        ["mathbf{A}"] = "\U0001D400",
        ["mathbf{B}"] = "\U0001D401",
        ["mathbf{C}"] = "\U0001D402",
        ["mathbf{D}"] = "\U0001D403",
        ["mathbf{E}"] = "\U0001D404",
        ["mathbf{F}"] = "\U0001D405",
        ["mathbf{G}"] = "\U0001D406",
        ["mathbf{H}"] = "\U0001D407",
        ["mathbf{I}"] = "\U0001D408",
        ["mathbf{J}"] = "\U0001D409",
        ["mathbf{K}"] = "\U0001D40A",
        ["mathbf{L}"] = "\U0001D40B",
        ["mathbf{M}"] = "\U0001D40C",
        ["mathbf{N}"] = "\U0001D40D",
        ["mathbf{O}"] = "\U0001D40E",
        ["mathbf{P}"] = "\U0001D40F",
        ["mathbf{Q}"] = "\U0001D410",
        ["mathbf{R}"] = "\U0001D411",
        ["mathbf{S}"] = "\U0001D412",
        ["mathbf{T}"] = "\U0001D413",
        ["mathbf{U}"] = "\U0001D414",
        ["mathbf{V}"] = "\U0001D415",
        ["mathbf{W}"] = "\U0001D416",
        ["mathbf{X}"] = "\U0001D417",
        ["mathbf{Y}"] = "\U0001D418",
        ["mathbf{Z}"] = "\U0001D419",
        ["mathbf{a}"] = "\U0001D41A",
        ["mathbf{b}"] = "\U0001D41B",
        ["mathbf{c}"] = "\U0001D41C",
        ["mathbf{d}"] = "\U0001D41D",
        ["mathbf{e}"] = "\U0001D41E",
        ["mathbf{f}"] = "\U0001D41F",
        ["mathbf{g}"] = "\U0001D420",
        ["mathbf{h}"] = "\U0001D421",
        ["mathbf{i}"] = "\U0001D422",
        ["mathbf{j}"] = "\U0001D423",
        ["mathbf{k}"] = "\U0001D424",
        ["mathbf{l}"] = "\U0001D425",
        ["mathbf{m}"] = "\U0001D426",
        ["mathbf{n}"] = "\U0001D427",
        ["mathbf{o}"] = "\U0001D428",
        ["mathbf{p}"] = "\U0001D429",
        ["mathbf{q}"] = "\U0001D42A",
        ["mathbf{r}"] = "\U0001D42B",
        ["mathbf{s}"] = "\U0001D42C",
        ["mathbf{t}"] = "\U0001D42D",
        ["mathbf{u}"] = "\U0001D42E",
        ["mathbf{v}"] = "\U0001D42F",
        ["mathbf{w}"] = "\U0001D430",
        ["mathbf{x}"] = "\U0001D431",
        ["mathbf{y}"] = "\U0001D432",
        ["mathbf{z}"] = "\U0001D433",
        ["mathbf{0}"] = "\U0001D7CE",
        ["mathbf{1}"] = "\U0001D7CF",
        ["mathbf{2}"] = "\U0001D7D0",
        ["mathbf{3}"] = "\U0001D7D1",
        ["mathbf{4}"] = "\U0001D7D2",
        ["mathbf{5}"] = "\U0001D7D3",
        ["mathbf{6}"] = "\U0001D7D4",
        ["mathbf{7}"] = "\U0001D7D5",
        ["mathbf{8}"] = "\U0001D7D6",
        ["mathbf{9}"] = "\U0001D7D7",
    };

    public static string? GetSymbol(string command)
    {
        return _symbols.TryGetValue(command, out var symbol) ? symbol : null;
    }

    public static bool IsSymbol(string command)
    {
        return _symbols.ContainsKey(command);
    }

    public static bool TryGetSymbol(string command, out string? symbol)
    {
        return _symbols.TryGetValue(command, out symbol);
    }

    public static IEnumerable<string> GetAllSymbolNames()
    {
        return _symbols.Keys;
    }

    public static IReadOnlyDictionary<string, string> GetAllSymbols()
    {
        return _symbols;
    }

    public static void RegisterSymbol(string command, string unicode)
    {
        _symbols.AddOrUpdate(command, unicode, (_, _) => unicode);
    }
}