# ADR-005: registro previo durable y recuperación sin reanudar limpieza

## Estado

Adoptado para desarrollo. El laboratorio 0.9.1 pasó 100/100 controles y se
repitió tras recrear la VM, con cortes de procesos propios y lectura de
evidencia. No habilita producción, no acepta una nueva candidata CLI ni cierra
Fase 8.

## Decisión

Cada intento tiene GUID nuevo. Se exige registro de inicio (inventario e IDs
opacos) y de intención antes de invocar cada archivo; resultado tras cada
invocación y cierre al terminar. El coordinador no inicia sin registro ni
continúa tras perder persistencia. Confirmación o cancelación previa no mutan.

El almacén escribe un archivo nuevo por checkpoint, sin sobrescribir ni anexar
a un intento recuperado. Usa WriteThrough y `FileStream.Flush(true)` antes de
confirmar persistencia. Una cadena SHA-256 detecta alteración accidental del
contenido/orden disponible. No es autenticación ni protege frente a un usuario
privilegiado que altere o retire el registro.

Al arrancar se lee el registro sin crear la carpeta si no existe. Una intención
sin resultado queda Unverified: puede haberse eliminado el archivo. Se conservan
por separado resultados verificados y candidatos sin intención registrada.
Una cola corrupta vuelve inciertos los restantes, no los declara intactos.
La recuperación no tiene puerto para reanudar el runner ni repetir una
operación. Los reportes muestran el intento y no intentados, y rechazan
contadores derivados contradictorios o IDs duplicados/solapados.

## Límites y evidencia de aceptación

Las pruebas en memoria acreditan orden y bloqueo. Reabrir un archivo o simular
una cola rota no prueba terminación abrupta de un proceso real. Esa puerta
se acreditó con hijos propios en Sandbox/VM, con el mismo preparador, coordinador,
ejecutor y almacén, antes/después del borrado y alrededor del resultado durable
(laboratorio 0.9.1, dos lotes 100/100). No sustituye una prueba de corte eléctrico.
El sistema no promete resistencia a cortes eléctricos o hardware que incumpla
flush, ni protección exhaustiva frente a carreras de ancestros del journal.

La CLI sigue componiendo solo inventario. El coordinador mutable exige evidencia
R0 tipada y reciente ligada a sesión/inventario, plataforma y volumen C: válidos,
sin alertas observadas incompatibles; coteja inventario e IDs con el ejecutor
preparado. Revalida antes de cada archivo y después del flush de intención.
Inicio durable conserva sesión/fecha diagnósticas para enlazar la recuperación.
Un booleano por sí solo ya no autoriza invocación. El paquete CLI posterior
recibió aceptación R0 propia 32/32; no compone ejecución mutable. Cualquier
habilitación futura requiere revisión expresa y sus puertas de plataforma.

Los laboratorios históricos y sus binarios no se reconstruyen ni sobrescriben.
Un laboratorio nuevo debe pasar journal explícito; un coordinador habilitado
sin él queda bloqueado, no simula la aceptación anterior.

Fuente: [FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0),
que solicita vaciar buffers intermedios; no convierte una prueba de lógica en
garantía física de persistencia.
