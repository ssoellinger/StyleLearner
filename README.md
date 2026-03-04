# StyleLearner

A C# style analysis and auto-fixing tool built on Roslyn. It learns your project's coding conventions by analyzing source files, then enforces them automatically.

## How it works

1. **Analyze** — scans all `.cs` files, runs 17 detectors to identify patterns (indentation, bracing, spacing, naming, etc.)
2. **Report** — shows detected styles with confidence percentages and conforming/non-conforming examples
3. **Fix** — applies fixers to reformat code to match the dominant style

## Usage

```
StyleLearner <directory> [options]
```

### Analyze and report

```bash
# Console report
StyleLearner "C:\MyProject"

# Generate .editorconfig
StyleLearner "C:\MyProject" --output editorconfig

# Generate HTML report
StyleLearner "C:\MyProject" --output html --report report.html

# Exclude paths
StyleLearner "C:\MyProject" --exclude "**/obj/**" --exclude "**/*.g.cs"
```

### Fix

```bash
# Apply fixes
StyleLearner "C:\MyProject" --fix

# Preview changes without modifying files
StyleLearner "C:\MyProject" --fix --dry-run

# Require higher confidence (default: 80%)
StyleLearner "C:\MyProject" --fix --min-confidence 90

# Learn style from one project, apply to another
StyleLearner "C:\MyProject" --fix --fix-path "C:\OtherDir"

# Fix only git-changed files
StyleLearner "C:\MyProject" --fix --git-changed
```

### Save and load style

Analyze once, reuse the learned style without re-analyzing:

```bash
# Save learned style to JSON
StyleLearner "C:\MyProject" --save-style style.json

# Fix using a saved style (skips analysis)
StyleLearner "C:\MyProject" --fix --load-style style.json --dry-run
```

The saved JSON uses `null` for rules where confidence was too low. You can hand-edit the file to enable or disable specific rules.

## Detectors

| Detector | What it detects |
|---|---|
| Indentation | Tabs vs spaces, indent size |
| Brace Style | Allman vs K&R |
| Parameter Layout | Multi-line thresholds, closing paren placement |
| Expression Body | Arrow placement (same line vs new line) |
| Inheritance Layout | Colon placement on inheritance clauses |
| Object Initializer | Trailing comma preference |
| Method Chaining | Single-line vs multi-line chain thresholds |
| Lambda | Lambda expression formatting |
| Ternary | Single-line vs multi-line ternary thresholds |
| Line Length | Line length distribution |
| Using Layout | Namespace style (file-scoped vs block-scoped) |
| Blank Lines | Blank lines around braces and regions |
| Spacing | Space after cast, space after keywords |
| Newline Keywords | Newline before catch/else/finally |
| Continuation Indent | Relative vs column continuation indent |
| Using Directives | Sort order, System-first, grouping, placement |
| Var Style | `var` vs explicit type usage |

## Fixers

Rules with sufficient confidence are automatically applied. Low-confidence rules are skipped (or can be forced via `--load-style` with a hand-edited JSON).

- Whitespace (trailing whitespace, BOM, line endings, final newline) — always active
- Blank line collapsing (2+ consecutive to 1) — always active
- Brace style, trailing commas, parameter layout, expression body arrows
- Inheritance layout, ternary layout, method chaining, argument layout
- Namespace style, spacing, newline keywords, continuation indent
- Using directive sorting/placement, var style

## Requirements

- .NET 8.0 SDK

## Building

```bash
dotnet build src/StyleLearner/StyleLearner.csproj
```
