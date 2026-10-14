# WinMaint v1.0.0-rc.3 — guía breve

Esta guía describe una candidata utilizada en validaciones locales. Esta
actualización del repositorio público solo contiene documentación: no incluye
el ejecutable, el comprobador ni reportes de equipos reales. Las rutas de ejemplo
son ilustrativas. Consulta [STATUS.md](STATUS.md) para los pendientes actuales.

Esta es una candidata en validación, no una versión final. Funciona sin instalar
.NET. El paquete incluye un ejecutable x64, esta guía y un comprobador opcional.

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
- Windows 10 puede mostrar un aviso de fin de soporte estándar; no se presupone ESU.
- El binario aún no tiene firma de editor. El SHA-256 permite identificar esta
  copia, no convierte al editor en una entidad verificada.

## Si se interrumpe o aparece un error

Vuelve a abrir WinMaint. Una prueba de estabilidad incompleta se anuncia y no se
reanuda automáticamente. Los reportes parciales conservan lo guardado hasta el
último punto de control; no afirman que la operación interrumpida terminó.

Si falla la escritura, revisa espacio y permisos o usa `--report-dir` con otra
carpeta. Conserva los archivos existentes. No borres el estado de una sesión
interrumpida para simular una prueba completada.

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

Para verificar el paquete, haz clic derecho en `VALIDAR-CANDIDATA.cmd` y elige
**Ejecutar como administrador**. Ejecuta comprobaciones de diagnóstico y
reportes, sin prueba de carga ni mantenimiento. Guarda su `RESULTADO.json`.
