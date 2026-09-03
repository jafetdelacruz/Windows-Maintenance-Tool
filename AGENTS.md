# Instrucciones del repositorio

Antes de modificar código, leer en este orden:

1. `SPEC.md`
2. `ARCHITECTURE.md`
3. `ROADMAP.md`
4. `docs/architecture/*.md`

Reglas:

- Target exclusivo: Windows 10/11 x64.
- TUI, no GUI.
- .NET 10 LTS y Spectre.Console.
- No afirmar que una función Windows fue validada desde macOS.
- Diagnosticar antes de modificar.
- Mantener prioridad P0-P3 separada de riesgo R0-R3.
- No tratar datos ausentes como resultados saludables.
- No inventar porcentajes ni progreso.
- No añadir optimizaciones agresivas a v1.
- No desactivar Defender, Windows Update ni servicios esenciales.
- No ejecutar comandos construidos mediante interpolación de shell.
- Probar acciones mutables en VM/equipo de laboratorio recuperable.
- Preservar reportes locales y ocultar seriales por defecto.
- No añadir campo cliente/folio.
- Documentar fuentes oficiales para reglas de mantenimiento.
- Completar la puerta de cada fase antes de avanzar.
