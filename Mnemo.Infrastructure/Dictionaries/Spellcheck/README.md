Place Hunspell dictionary files here.

Expected file pairs (either flat or per-language folder):

- `en.aff` + `en.dic` or `en/en.aff` + `en/en.dic`
- `de.aff` + `de.dic` or `de/de.aff` + `de/de.dic`
- `es.aff` + `es.dic` or `es/es.aff` + `es/es.dic`
- `ja.aff` + `ja.dic` or `ja/ja.aff` + `ja/ja.dic` (if available)

The spellcheck catalog resolves language tags by exact code first (for example `en-US`)
and then falls back to the primary language (`en`).
