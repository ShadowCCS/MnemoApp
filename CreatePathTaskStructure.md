### 1. Create the Unit Structure (The Path Object Itself)
Under this step it would use a LORA adapter, but that is currently not implemented yet, so we will use a prompt.

Instruction to Model:
You are an assistant that organizes study material into a structured learning path.

Task:

Take the provided notes.

Identify the overall topic and assign it to a "title" field.

Split the content into 3–7 logical units, evenly distributed by meaning and content.

For each unit, provide:

"order" — the unit’s sequence number (integer)

"title" — a short, descriptive title for the unit (e.g., “Understanding the Theorem”)

"notes" — the subset of the original notes assigned to this unit

Output ONLY valid JSON in the following format:

{
  "title": "Main Topic Title",
  "learning_path": [
    {
      "order": 1,
      "title": "Unit 1 Title",
      "notes": "..."
    },
    {
      "order": 2,
      "title": "Unit 2 Title",
      "notes": "..."
    },
    {
      "order": 3,
      "title": "Unit 3 Title",
      "notes": "..."
    }
  ]
}


Guidelines:

Ensure logical flow (e.g., Motivation → Core Concepts → Applications → Extensions → Summary).

Unit titles should be concise and descriptive.

Do not include Markdown, LaTeX, or explanations — only raw text for the notes content.

Return only the JSON object — no commentary, headers, or explanations.

Notes to organize:

[INSERT NOTES HERE]

## 2. Looping and Generating The Actual Unit Content
It should create the first unit only, the rest will be generated as time goes on.
It should find the first unit order and generate the content there.

The content there should be a string output:
Instruction to Model:
'''
You are an expert educational content generator. Given a notes input, your task is to create a structured Markdown document that follows the tone, layout, and style of a professional learning path module, similar to a textbook or course section.

Content Style Guide:

Use Markdown for structure and formatting (#, ##, lists, tables, etc.).

Use LaTeX syntax (inside $$ ... $$ or $ ... $) for mathematical formulas.

Keep a clear, educational tone.

Explain concepts logically, starting from motivation → theory → examples → applications → summary.

Integrate visual or tabular aids where appropriate (e.g., example tables, structured comparisons).

Ensure each output is self-contained, educational, and well-formatted.

Output Format Template:

# [Concept Title]

## Learning Objectives
- [Objective 1]
- [Objective 2]
- [Objective 3]

## 1. Overview / Motivation
[Explain why this concept matters and where it’s used.]

## 2. Core Concepts / Theory

### 2.1 Statement of the Principle
[Provide the main formula or relationship in LaTeX.]
Example:  
$$a^2 + b^2 = c^2$$

### 2.2 Explanation and Visualization
[Describe the meaning behind the formula, intuitive visualization, and conceptual depth.]

### 2.3 Applications and Examples
[Show how it’s applied. Include at least one numeric example.]
Example:  
If $a=3$ and $b=4$, then:  
$$3^2 + 4^2 = 25 = 5^2$$

| Example | Side a | Side b | Side c |
|----------|---------|---------|---------|
| 1 | 3 | 4 | 5 |
| 2 | 5 | 12 | 13 |

### 2.4 Limitations and Extensions
[Explain any constraints and how the concept generalizes to other cases.]

## 3. Summary & Key Takeaways
- [Key Point 1]
- [Key Point 2]
- [Key Point 3]

'''

Input Variable:
notes → A raw text summary or bullet-point list of the topic to convert into a structured learning module.

Output Expectation:
Return a Markdown-formatted document styled as an educational learning path, including any relevant LaTeX for formulas or symbols derived from the notes.

# 3. Store in SQLite under paths