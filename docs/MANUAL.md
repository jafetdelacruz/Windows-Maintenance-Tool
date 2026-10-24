# WinMaint v1 — guía breve (21 de octubre de 2026)

Esta guía describe la candidata utilizada en validaciones locales. El repositorio
incluye código fuente, pruebas y documentación, pero no publica un ejecutable
oficial ni reportes de equipos reales. Las rutas de ejemplo son ilustrativas.
Consulta [STATUS.md](STATUS.md) para los pendientes actuales.

Esta es una candidata en validación, no una versión final. Funciona sin instalar
.NET. El paquete incluye un ejecutable x64, esta guía y un comprobador opcional.

El flujo R0 de `rc.5-dev` tiene aceptación local de diagnóstico, exportación e
inventario, no de candidata final. La aceptación histórica de `rc.4` con
`rc4-no-preamble-5` se conserva aparte. No se transfieren puertas entre binarios.

## Uso

1. Abre `WinMaint.Cli.exe` y acepta la solicitud de administrador de Windows.
2. Elige **1. Diagnóstico completo**. Espera a que termine.
3. Lee los avisos. `Desconocido` significa que no se obtuvo información.
4. Elige **9. Reportes** para ver las rutas de TXT y JSON.
5. Elige **10. Salir** para registrar el cierre de la sesión.

El TXT es el resumen para leer. El JSON conserva las mediciones técnicas. Se
guardan por defecto en `%ProgramData%\WinMaint\Reports`. Con
`--report-dir "D:\Mis reportes"` puedes elegir otra carpeta.

Para comparar, ejecuta el diagnóstico dos veces en la misma sesión. Los cambios
de RAM o espacio libre son variaciones observadas, no una demostración de mejora.

## Funciones disponibles

- Diagnóstico de Windows, CPU, RAM, discos y eventos.
- Evaluación orientativa de salud y cobertura de datos.
- Prueba opcional de estabilidad (opción 4). Guarda tu trabajo antes; genera
  carga durante unos 4–6 minutos. `Esc` o `Ctrl+C` solicita cancelación.
- PC Manager (opción 8): abrirlo si está instalado o abrir su ficha oficial de
  Store para instalar manualmente. Exige el token que muestra la consola.
- Reportes resumidos TXT y técnicos JSON, con seriales ocultos.
- Temporales (opción 5): inventario sin borrar, solo en Temp del usuario.
  Mínimo siete días desde creación y última escritura; excluye enlaces y
  elementos protegidos. El tamaño encontrado no garantiza espacio liberable.

## Límites actuales

- Mantenimiento e integridad solo muestran planes; limpieza solo inventaría.
  Esta candidata no borra temporales, vacía la papelera, repara Windows ni
  optimiza discos. No instala PC Manager automáticamente.
- RAM y almacenamiento están incluidos en el diagnóstico general. Las vistas
  separadas de la opción 3 y los programas de inicio de la opción 7 no están
  implementados.
- La puntuación no certifica el hardware. Los datos ausentes reducen la cobertura.
- Requiere Windows 10 22H2 o Windows 11 x64. La candidata actual todavía requiere
  aceptación final en las consolas y sistemas documentados en Fase 7.
- En desarrollo `rc.5-dev` se añade un aviso de soporte Windows 10 separado de la
  compatibilidad técnica. La inscripción ESU queda desconocida; ediciones LTSC
  requieren consultar su ciclo específico. Los binarios anteriores no incluyen
  este aviso.
- En ese desarrollo, el uso puntual de RAM no acredita integridad física ni
  presión sostenida. El resultado previo de Windows Memory Diagnostic es
  evidencia parcial e histórica; WinMaint no lo inicia ni pide reiniciar.
- El binario aún no tiene firma de editor. El SHA-256 permite identificar esta
  copia, no convierte al editor en una entidad verificada.

## Si se interrumpe o aparece un error

Vuelve a abrir WinMaint. Una prueba de estabilidad incompleta se anuncia y no se
reanuda automáticamente. Los reportes parciales conservan lo guardado hasta el
último punto de control; no afirman que la operación interrumpida terminó.

Si falla la escritura, revisa espacio y permisos o usa `--report-dir` con otra
carpeta. Conserva los archivos existentes. No borres el estado de una sesión
interrumpida para simular una prueba completada.

El desarrollo también detecta registros incompletos de limpieza al arrancar:
distingue eliminaciones verificadas, operaciones inciertas y no intentadas.
No reanuda borrados. Si el registro está corrupto, no supone que los archivos
restantes están intactos. La limpieza de producción sigue bloqueada.

## Comandos útiles

```text
WinMaint.Cli.exe --help
WinMaint.Cli.exe --report --no-ansi
WinMaint.Cli.exe --pc-manager-detect --no-ansi
WinMaint.Cli.exe --pc-manager-store
WinMaint.Cli.exe --report-summary "D:\Mis reportes\reporte.json"
```

El último comando crea `reporte.summary.txt` sin repetir diagnósticos; rechaza
un resumen ya existente en vez de sobrescribirlo. Los modos de ejecución se
usan de uno en uno. `--no-ansi` permite trabajar con consolas de texto simple.

Para identificar el conjunto local aceptado, utiliza el selector
`tools/release/SHOW-CURRENT-ACCEPTANCE.ps1`: coteja los hashes sin abrir la
candidata ni iniciar Sandbox. La selección actual `cli-rc5dev-review-a3` obtuvo
32/32: cuatro reportes funcionales, once rechazos esperados e inventarios vacío
y no vacío sin borrar. La selección histórica rc.4 conserva sus cuatro hashes
y resultado 13/13; no uses su comprobador original obsoleto. No hace falta
repetir ninguno de esos lotes sin cambios pertinentes.

La candidata posterior `rc.5` tiene paquete independiente y aceptación R0
Windows 11 propia; no sustituye aún la selección anterior ni habilita limpieza.
Para la validación pendiente en PC 2 se prepara una carpeta portátil con
`VALIDATE.cmd`: ejecutar como administrador y devolver completa, incluida
`RESULTS`. No requiere instalar .NET ni ejecutar carga. Los resultados son
privados; no deben subirse a GitHub. Su validación automática no sustituye
las puertas interactivas de UAC, presentación y teclas.

Un paquete nuevo necesita verificación y aceptación propias. En desarrollo,
`--diagnostic` y `--report` devuelven 4 si la exportación solicitada no termina;
0 acredita la exportación, no que toda la información ni todo el hardware
estén disponibles o saludables. JSON y TXT deben compartir identificador de
exportación; un TXT anterior no demuestra guardado completo.
