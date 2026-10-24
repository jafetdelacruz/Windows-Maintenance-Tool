# Fase 8 — contrato inicial de temporales

El usuario aprobó comenzar limpieza segura después de corregir WHEA.
Esta entrega solo inventaría metadata; no conecta ningún ejecutor mutable.

## Política interna

- Única raíz: LocalAppData/Temp del usuario actual; además debe coincidir con
  Profile/AppData/Local/Temp. Una carpeta redirigida fuera de ese alcance se
  bloquea, no se usa otra carpeta ni se aceptan rutas desde argumentos.
- Antigüedad mínima: siete días desde creación y última escritura. Es una
  política conservadora de WinMaint, no una garantía ni un umbral de Microsoft.
- Se rechazan enlaces/reparse points en la raíz y en sus ancestros hasta el
  perfil. El recorrido no sigue entradas con ese atributo.
- Se excluyen atributos System, Offline y archivos ReadOnly. Se protegen
  subcarpetas `.net` y nombres que empiezan por WinMaint (runtime y laboratorios).
- Máximo 20 000 entradas, profundidad 32 y presupuesto de 15 segundos comprobado
  entre lecturas de metadata. Límite o acceso fallido implica recorrido parcial.
- No se abren contenidos. El tamaño es lógico, no espacio físico liberable.
  La vista previa no determina si un archivo está bloqueado/en uso; esa
  comprobación y la identidad/ruta deben revalidarse en un futuro ejecutor.
- P3/R0 para inventario; eventual eliminación P3/R2 irreversible, no R1.
- No se elimina ninguna carpeta. Papelera, Windows Temp, Windows Update,
  Descargas, OneDrive, documentos, cachés fuera de Temp y otras áreas excluidas.

## Puertas restantes

Confirmación exacta, diagnóstico previo completo, inventario vigente, validación
de identidad/ruta con resistencia a sustitución por enlaces, omisión de
archivos en uso/cambiados, cancelación y reporte técnico de cada resultado
deben probarse en Sandbox recreable antes de habilitar producción. El viejo
adaptador de Fase 4 no se reutiliza como si ya validara estas garantías.

La opción 5 conserva el resumen del inventario en notas TXT/JSON de la sesión,
sin lista de rutas privadas. Es evidencia de inventario, no acción de limpieza.
El esquema para reportar mutaciones se definirá antes de conectar un ejecutor.

## Fuentes

- [Atributos de archivos .NET](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileattributes).
- [Storage Sense y separación de áreas](https://learn.microsoft.com/en-us/windows/configuration/storage/storage-sense).

## Preparación estricta de candidatos — 14 de octubre, pendiente de Sandbox

`WindowsTemporaryCleanupPreparation` obtiene perfil y LocalAppData desde Windows,
sin aceptar rutas públicas. La composición pública permanece incapaz de borrar,
incluso con un coordinador habilitado. La única composición mutable es interna y
exige la cuenta/montaje del Sandbox y una fixture propia con GUID. Todavía no se
conecta a la CLI. Esta implementación nativa admite únicamente C:; perfiles
redirigidos, UNC y otras unidades se bloquean, no se interpretan como vacíos.

Cada candidato tiene identificador opaco, identidad y vigencia monotónica. Antes
de ejecutar se vuelven a abrir y fijar todos los ancestros hasta la unidad,
comparando identidad y ruta, sin conservar bloqueos durante la espera humana.
Errores posteriores al inicio nativo no se convierten en un falso cero cambios.
La verificación posterior distingue FileNotFound de acceso denegado; no utiliza
File.Exists=false como prueba de eliminación. Referencia: [File.GetAttributes y
sus excepciones](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.getattributes?view=net-10.0).

163 tests no mutables aprobados. Laboratorio 0.8.0 preparado con los 20 controles
previos y siete nuevos sobre composición, identificadores y sustitución de un
directorio entre inventario y ejecución. Aceptación 27/27 pendiente, no heredada
de 0.7.0. Windows 10 queda aplazado a petición del usuario mientras PC 2 está
ocupada, sin eliminar esa puerta. Producción sigue bloqueada.

### Integración R0 en desarrollo

Opción 5 conectada mediante `ITemporaryInventoryProvider`, que solo devuelve
`TemporaryInventory`. El adaptador público no ofrece rutas configurables ni
operaciones mutables; descarta el mapa privado al devolver los agregados.
Se preservan ID, instante original y contadores enteros/null en reportes.
La ausencia de raíz admitida se muestra como no disponible; un recorrido parcial
no se promueve a completo. El menú exige un snapshot de diagnóstico previo,
no basta con que la sesión haya creado un archivo de reporte.

169 tests aprobados y flujo real de desarrollo 5 → 5 → 10 verificado en Windows
11 sin borrar. No se ha publicado una nueva candidata ni habilitado producción.
