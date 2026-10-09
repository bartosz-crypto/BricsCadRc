---
description: Niezależna recenzja zmian (albo planu) przez ChatGPT/Codex, potem weryfikacja uwag przez Claude
argument-hint: "[-Plan ścieżka] [-Baza ref] [-Uwagi \"kontekst\"]"
---

1. Uruchom (PowerShell, timeout 600000 ms):
   `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/recenzja-gpt.ps1 $ARGUMENTS`
2. Każdą uwagę ChatGPT zweryfikuj w kodzie i w ARCHITECTURE.md — ChatGPT też się myli.
   Odrzucone uwagi wypisz z jednym zdaniem uzasadnienia.
3. Pokaż użytkownikowi po polsku tabelę: uwaga · waga · decyzja (przyjęta / odrzucona) · co poprawić.
4. Przyjęte poprawki przekaż agentowi `implementator` (max 3 rundy recenzji na zadanie).
   Nie commituj.
