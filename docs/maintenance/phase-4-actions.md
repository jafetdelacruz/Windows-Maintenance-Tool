# Catálogo de mantenimiento seguro — Fase 4

Versión del catálogo: `1.0.0`. La versión 1 expone únicamente el perfil
`Safe`. La TUI genera planes verificables y la ejecución mutable de producción
permanece bloqueada. Existe un ejecutor acotado de temporales que solo se
habilita dentro del perfil recuperable de Windows Sandbox para cerrar las
pruebas de aceptación.

## Reglas globales

- El diagnóstico precede a cualquier modificación.
- Prioridad P0–P3 y riesgo R0–R3 se presentan como datos independientes.
- Toda mutación necesita precondiciones comprobadas, vista previa y
  confirmación exacta de la acción.
- Las mutaciones se ejecutarán en serie; nunca competirán entre sí.
- No se construyen comandos mediante shell ni interpolación. El ejecutable es
  una ruta absoluta y cada argumento ocupa un elemento separado.
- Nunca se reinicia automáticamente.
- Una cancelación debe reportarse y no puede ocultarse como éxito.

## Acciones

| Identificador | Prioridad | Riesgo | Modifica | Condición principal |
|---|---:|---:|---:|---|
| `temp.inventory` | P3 | R0 | No | Carpeta temporal validada |
| `temp.cleanup` | P3 | R2 | Sí | Inventario vigente y antigüedad mínima |
| `recycle.inventory` | P3 | R0 | No | Papelera accesible |
| `recycle.empty` | P3 | R2 | Sí | Inventario separado y confirmación |
| `volume.analyze` | P3 | R0 | No | Unidad local compatible y no dañada |
| `volume.optimize` | P3 | R2 | Sí | Análisis previo de Windows |
| `chkdsk.scan` | P2 | R1 | No | NTFS y administrador |
| `dism.scan-health` | P2 | R1 | No | Imagen activa y administrador |
| `dism.restore-health` | P1 | R2 | Sí | Corrupción reparable y fuente válida |
| `sfc.scan-now` | P1 | R2 | Sí | Almacén de componentes preparado |

## Límites por área

### Temporales y papelera

El inventario debe existir antes de borrar y registrar cantidad y bytes. La
antigüedad mínima es una política interna y los archivos bloqueados o en uso se
omiten. La papelera constituye un plan independiente porque vaciarla es una
eliminación permanente. La carpeta temporal se resuelve con
`Path.GetTempPath`; después se valida su ruta y accesibilidad, pues la propia API
no garantiza que exista ni que sea accesible.

### Unidades

`defrag.exe /A` analiza. `defrag.exe /O` permite que Windows elija la operación
correcta para el tipo de medio; WinMaint no fuerza `/D`. Las unidades marcadas
como dañadas se bloquean y se propone primero un examen del sistema de archivos.
`chkdsk /scan` es el único modo rutinario del catálogo; `/f`, `/r`, `/x` y `/b`
no forman parte de esta fase.

### Integridad de Windows

`DISM /Online /Cleanup-Image /ScanHealth` detecta. Solo si el resultado indica
corrupción reparable se puede proponer `RestoreHealth`. `sfc /scannow` depende
de que DISM confirme o repare el almacén de componentes. La disponibilidad de
red o de una fuente local se comprueba antes de reparar.

## Reporte obligatorio

Cada resultado conserva identificador y versión, prioridad, riesgo, inicio y
fin, estado, cantidad de archivos, bytes, código de salida, reinicio requerido,
temporales conocidos, detalle y verificación posterior. Un campo desconocido
no se convierte en cero ni en éxito.

## Fuentes oficiales

- [Defrag](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/defrag)
- [Chkdsk](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/chkdsk)
- [Reparar una imagen de Windows con DISM](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image?view=windows-11)
- [Usar DISM y el Comprobador de archivos de sistema](https://support.microsoft.com/es-es/windows/experience/backup-recovery/using-system-file-checker-in-windows)
- [Clear-RecycleBin](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/clear-recyclebin)
- [Path.GetTempPath](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.gettemppath?view=net-10.0)
- [Configurar Sensor de almacenamiento](https://learn.microsoft.com/en-us/windows/configuration/storage/storage-sense)
