# ForgeFlow working agreement

- Read the authoritative current section of `ForgeFlow.md` before changes. Historical deliveries are not verification evidence for this greenfield repository.
- Work one small increment within the current slice. Define its scope and acceptance evidence, implement it, run relevant checks, and record actual results and remaining gaps before advancing. Never declare a UI slice accepted until Persian and English, RTL/LTR, and mobile acceptance are complete.
- Use English for commands, scripts, identifiers, and implementation reports. Preserve UTF-8 text.
- Use ASP.NET Core Localization, `IStringLocalizer`, and explicit `fa-IR`/`en-US` `.resx` resources for all display text. No inline translated literals, culture comparisons selecting messages, or concatenated translated sentence fragments. Centralize culture, direction, and formatting. Keep business data and identifiers language-independent.
- Domain has no infrastructure or presentation dependencies. Application references Domain. Infrastructure references Application. Web is the composition root and references Application and Infrastructure. Do not add speculative entities, persistence, authentication, or workflows.
- Verify stable dependencies from official sources, pin SDK/package versions, retain lock files, and report exact build/test commands and failures. Never use historical results as evidence.
- Update `ForgeFlow.md` after each increment with changes, evidence, outstanding acceptance gaps, and the next small step. Do not advance to later slices with an unaccepted current slice.
- Try non-escalated workspace commands first. Do not request disabled escalation, change security settings, or bypass restrictions. Report blocked commands and their exact rejection. Never claim unperformed checks.
- Do not initialize Git, commit, push, merge, or deploy without explicit authorization.
