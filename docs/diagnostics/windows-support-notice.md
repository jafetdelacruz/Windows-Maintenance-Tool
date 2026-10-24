# Soporte de Windows: contexto separado de compatibilidad — política 1.0.0

Desarrollo local rc.5-dev, 21 de octubre de 2026. No modifica licencias,
actualizaciones ni seguridad. No se atribuye este aviso al paquete rc.4.

La clasificación del build sigue indicando si Windows está dentro del alcance
técnico. El ciclo de soporte se presenta aparte y no resta puntos a salud.
Para Windows 10 Home/Pro 22H2 identificado por edición/versión, después del
14 de octubre de 2025 se informa fin de soporte estándar. La inscripción ESU
queda desconocida: no se ha consultado ni inferido por build o conectividad.

LTSC/LTSB tiene ciclos particulares; ediciones desconocidas y versiones sin
regla exacta muestran ciclo específico desconocido junto al aviso general
Home/Pro, no una declaración de falta de soporte de esa edición. Windows 11
tampoco recibe una afirmación de soporte vigente solo por reconocer su build.
La fecha de evaluación es explícita en la política para permitir fixtures.

El diagnóstico conserva las métricas `system.support.notice`,
`system.support.status` y `system.esu.enrollment`; la CLI muestra los datos y el
TXT incluye un aviso breve y ESU desconocido. JSON conserva cobertura/fuentes.

Fuentes oficiales consultadas el 21 de octubre de 2026:

- [Windows 10 Home and Pro — Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro):
  fin general del soporte y excepción de ciclos LTSC.
- [Extended Security Updates](https://learn.microsoft.com/en-us/windows/whats-new/extended-security-updates):
  la cobertura ESU depende de inscripción; no se presume en este equipo.

Pruebas: Home/Pro/Workstations, LTSC, edición desconocida, versión anterior o
ausente, fecha histórica, Windows 11 y retención del aviso en JSON/TXT. No
sustituyen una ejecución del nuevo binario final en Windows 10.
