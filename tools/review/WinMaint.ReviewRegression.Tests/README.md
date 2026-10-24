# Regresiones de revisión — control habitual local

Esta suite reproduce H01–H03 del informe independiente del 21 de octubre.
Usa únicamente datos, probes y almacenes en memoria. No hace carga intensiva,
no llama proveedores Windows y no ejecuta borrado ni guardado de reportes.

Ejecución explícita desde la raíz:

```powershell
dotnet test tools/review/WinMaint.ReviewRegression.Tests/WinMaint.ReviewRegression.Tests.csproj -c Release
```

Resultado inicial esperado y observado: 20 casos, 14 fallos de comportamiento
y 6 controles aprobados; exit 1. Esto confirma defectos pendientes, no acepta el
producto. No se usan Skip ni aserciones que exijan conservar el comportamiento
defectuoso. Esa evidencia roja queda preservada. Tras corregir H01–H03, los
20 casos originales pasan y la suite ampliada está integrada en WinMaint.sln;
`dotnet test WinMaint.sln -c Release` ya la ejecuta sin filtros ni Skip.

Los casos adicionales cubren evaluación final explícita, compatibilidad de
reportes históricos, retención de fallos, cancelación, estados desconocidos,
valores inválidos, segundos volúmenes, cobertura ausente/parcial y sensores
opcionales. No acreditan carga nativa, UAC ni una candidata empaquetada nueva.

La línea base separada conserva 169 aprobadas. No deben presentarse como si
cubrieran estos escenarios nuevos. Consulta
`docs/reviews/acceptance-matrix-local.md` para alcance, evidencia y siguientes pasos.
