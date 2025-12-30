using System.Collections.Generic;

namespace Mnemo.Infrastructure.Services.LaTeX.Symbols;

public static class SymbolRegistry
{
    private static readonly Dictionary<string, string> _symbols = new()
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
        _symbols[command] = unicode;
    }
}

