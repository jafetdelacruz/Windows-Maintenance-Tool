# Reglas de salud y estabilidad — versión 1.0.4 (desarrollo local)

La puntuación explica la evidencia disponible; no certifica hardware. El
rendimiento se muestra por separado y no se convierte directamente en salud.

## Dominios y confianza

| Dominio | Peso |
|---|---:|
| Fiabilidad del hardware | 30 % |
| Almacenamiento | 25 % |
| RAM | 20 % |
| Integridad de Windows | 15 % |
| Estabilidad bajo carga | 10 % |

Cobertura completa vale 100 %, parcial 50 % y ausente 0 %. Un dominio necesita
35 % de confianza para producir puntuación. El promedio general solo usa
dominios con evidencia suficiente, mientras que la confianza general conserva
el peso de todos los dominios. Por eso un dato ausente reduce confianza, pero no
reduce la salud como si fuera un fallo.

Cada dominio parte de 100 y descuenta 100/45/20/5 puntos por hallazgos
P0/P1/P2/P3. Después del promedio, cualquier P0 limita el resultado general a
39 y cualquier P1 sin P0 lo limita a 59.

## Reglas iniciales

| Identificador | Condición | Prioridad |
|---|---|---|
| `STORAGE-HEALTH-UNHEALTHY` | `HealthStatus=Unhealthy` | P0 |
| `STORAGE-OPERATIONAL-NOT-OK` | Estado operativo adverso explícito, aunque coexista con OK | P0 |
| `STORAGE-HEALTH-WARNING` | `HealthStatus=Warning` | P1 |
| `STORAGE-FREE-CRITICAL` | Menos de 5 % libre | P1 |
| `STORAGE-FREE-LOW` | Menos de 10 % libre | P2 |
| `STORAGE-FREE-WATCH` | Menos de 20 % libre | P3 |
| `WHEA-RECENT` | Uno o más errores/advertencias WHEA clasificados; no cuenta informativos | P2 |
| `WHEA-UNCLASSIFIED` | Hay registros WHEA sin severidad interpretable | P3 |
| `MEMORY-DIAGNOSTIC-FAILED` | Último resultado previo interpretable de Windows Memory Diagnostic indica errores | P1/R0 |
| `WINDOWS-REBOOT-PENDING` | Señal conocida de reinicio pendiente | P3/R3 |
| `STABILITY-*-FAILED` | Falla controlada de una etapa | P0 para RAM/disco; P1 para otras |
| `STABILITY-*-TIMEOUT` | Una etapa agota su timeout | P2 |
| `STABILITY-NEW-WHEA` | Aumenta WHEA entre antes y después | P1 |

P0-P3 expresa urgencia. R0-R3 expresa el riesgo de la acción sugerida y nunca
autoriza su ejecución.

## Prueba rápida

El plan estándar suma 245 segundos antes del pequeño costo de las consultas:

1. WHEA previo.
2. Línea base en reposo: 45 segundos.
3. CPU: 30 segundos, hasta cuatro trabajadores con ciclo aproximado de 50 %.
4. WHEA después de CPU; un evento nuevo omite carga intensiva posterior.
5. RAM: 50 segundos, máximo del menor valor entre 1 GiB y 20 % disponible.
6. WHEA después de RAM; un evento nuevo omite carga intensiva posterior.
7. Almacenamiento: 30 segundos de lectura secuencial del ejecutable.
8. WHEA después de almacenamiento; un evento nuevo omite carga intensiva.
9. Enfriamiento: 90 segundos sin carga generada por WinMaint.
10. WHEA posterior.

Las consultas comparan la marca temporal del evento WHEA más reciente, no un
conteo truncado. Añaden tiempo real pequeño al plan de 245 segundos, sin carga
artificial.

No se crea un archivo de benchmark. El único estado persistente es
`%LOCALAPPDATA%\WinMaint\stability-session.json`, que contiene fecha y última
etapa, se escribe mediante reemplazo atómico y se elimina al completar. Si la
ejecución se interrumpe, el siguiente inicio lo advierte y nunca reanuda carga
automáticamente.

## Estados operativos de discos (H02)

El proveedor conserva cada elemento de OperationalStatus, no una decisión por
subcadenas. Las reglas también aceptan listas separadas por coma de reportes o
fixtures previos. Un estado adverso no queda neutralizado por otro normal.

- Normales: coincidencia exacta, sin distinguir mayúsculas, con `OK` o el alias
  histórico `Healthy`.
- Adversos P0 de esta política: `Predictive Failure`, `Error`,
  `Non-Recoverable Error`, `Failed Media`, `IO Error`.
- Vacíos, estados desconocidos, de proveedor, transitorios u otros no incluidos:
  no generan P0 por defecto; tampoco acreditan normalidad. Se conserva el texto,
  se excluye su cobertura operativa y se bloquea carga hasta revisarlos.
- Un HealthStatus no interpretable tampoco acredita cobertura de salud.

Fuente semántica consultada el 21 de octubre de 2026:
[MSFT_StorageFaultDomain, OperationalStatus](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagefaultdomain).
La clasificación P0 y el bloqueo son decisiones conservadoras de WinMaint, no
una escala de prioridad definida por Microsoft. No afirman qué pieza falló.

## Mínimos de preflight (H03)

| Dato indispensable | Válido | Bloqueo / incertidumbre |
|---|---|---|
| Energía | Una fuente conocida; AC o batería medida entre 0 y 100 % | Fuente ausente/contradictoria o batería sin lectura válida; batería <40 % bloquea |
| Discos | Estados operativos normales y HealthStatus Healthy en todos los observados | P0 bloquea; estado ausente, desconocido, adverso o Warning también impide carga |
| Espacio | Porcentaje finito 0–100 en cada volumen observado | <5 % bloquea; un volumen sin lectura válida impide iniciar |
| CPU inicial | Un porcentaje finito 0–100 | >=90 % bloquea; 70–<90 % advierte; ausente/duplicado/inválido impide iniciar |
| RAM disponible | Una cantidad finita, no negativa, representable en long | <256 MiB bloquea; <1 GiB advierte; ausente/duplicada/inválida impide iniciar |
| WHEA | Un conteo entero no negativo de errores clasificados | Consulta ausente/inválida impide iniciar; errores conocidos advierten; severidad sin clasificar impide iniciar |

Cobertura Partial es utilizable para estos datos si el valor tiene semántica
válida y están presentes todos los mínimos; no se convierte en Complete ni
demuestra cobertura de todos los dispositivos. Absent nunca autoriza carga,
aunque contenga un valor. NaN, infinito, rangos inválidos y conteos fraccionarios
no satisfacen un requisito. La ausencia conserva Unknown y añade un bloqueo
explícito por datos insuficientes: no se convierte en daño de hardware.

Sensores térmicos y contadores opcionales no son requisitos universales: su
ausencia no prueba temperatura segura. El usuario debe confirmar que guardó su
trabajo. Estos mínimos se vuelven a comprobar antes de cada etapa intensiva
en el desarrollo 1.0.3 descrito abajo. No se ejecuta carga para
probar estas decisiones: se comprueban cero invocaciones mediante probes falsos.

## Evaluación final de sesión (H01)

Los nuevos reportes usan esquema 1.2 con `finalHealth` explícito, compartido con
la vista de salud de la CLI y el encabezado TXT. Los snapshots conservan su
evaluación histórica. La cobertura parte de la última captura y de la última
prueba; los hallazgos previos se conservan, deduplicados por regla/evidencia con
la prioridad más alta. Un nuevo diagnóstico o prueba cancelada no resuelve un
fallo anterior. No existe todavía un protocolo de resolución automática.

Sin evidencia suficiente no se inventa puntuación; los hallazgos siguen
visibles. Los lectores conservan compatibilidad 1.0/1.1 sin reejecutar reglas
actuales sobre datos antiguos: al resumirlos solo unen alertas registradas y
aplican los límites P0/P1 al valor guardado, sin modificar el JSON original.
La corrección H01 por sí sola no resuelve H12. La interpretación RAM cambió
posteriormente en 1.0.4, según la sección siguiente; no se reevalúan snapshots
históricos ni candidatas ya aceptadas.

## RAM: uso, presión y evidencia física (H12, 1.0.4)

- `memory.load.percent` y memoria disponible describen uso puntual. Ya no
  contribuyen a la cobertura física del dominio RAM; tampoco lo hace un WHEA
  general, que no identifica por sí solo memoria defectuosa.
- Se etiqueta uso alto en esta muestra a partir de 90 %. Es un umbral de
  presentación de WinMaint, no una regla Microsoft, hallazgo P2, presión
  sostenida, fallo físico ni penalización de salud.
- No se ha implementado una serie temporal de presión suficiente: la cobertura
  de presión sostenida permanece explícitamente ausente, incluso con uso alto.
- Se consulta el último evento de `Microsoft-Windows-MemoryDiagnostics-Results`
  en System, solo lectura, máximo un evento y timeout de 15 s. No se ejecuta
  `mdsched`, no se programa una prueba ni un reinicio. No se almacena XML crudo,
  nombre de equipo ni SID del evento.
- Eventos 1101/1201 con nivel 4 indican prueba previa sin errores; 1102/1202 con
  nivel 2 indican errores previos. Cancelación 1103, incompleta 1104, otro ID,
  proveedor/nivel/fecha inválidos, fecha futura, payload contradictorio, salida
  ilegible, consulta fallida o resultado ausente quedan desconocidos.
- Un resultado previo interpretable aporta 50 % de cobertura física como
  evidencia parcial e histórica. No es una probabilidad ni cobertura exhaustiva
  de módulos: la puntuación orientativa no certifica la RAM actual. Sin esa
  evidencia, la puntuación RAM es desconocida aunque el uso se haya medido.
- Un fallo previo genera P1/R0 y se conserva entre snapshots hasta revisión;
  otro diagnóstico o prueba posterior no lo resuelve automáticamente.
  También bloquea el preflight de carga mientras figure sin resolver; no se
  convierte la ausencia del diagnóstico previo en un requisito universal.
- El patrón de memoria de proceso se conserva por separado como resultado de
  estabilidad, no aumenta la cobertura física de todos los DIMM. Sus fallos y
  límites P0 de estabilidad existentes no se eliminan.

Fuentes primarias: [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
describe uso actual volátil, no integridad física;
[wevtutil](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wevtutil)
documenta consulta de eventos y metadatos de publicadores. El mapeo de los seis
IDs anteriores se cotejó con el publicador Microsoft instalado en Windows 11,
mediante `wevtutil gp Microsoft-Windows-MemoryDiagnostics-Results /ge:true /gm:true /f:xml`,
el 21 de octubre de 2026. No se deduce éxito solo del nivel informativo ni del
texto localizado. Los eventos desconocidos en otras versiones quedan ausentes;
las fixtures no sustituyen la aceptación nativa pendiente de Windows 10.

## Revalidación y vigilancia durante estabilidad (H04, 1.0.3)

El coordinador no invoca CPU/RAM/lectura si no tiene IStabilitySafetySource.
La composición Windows sí conecta esa fuente. Antes de cada etapa intensiva
recopila un diagnóstico nuevo y aplica los mismos mínimos iniciales. Fallo o
ausencia de revalidación omite la carga, no afirma fallo físico.

El presupuesto RAM es el menor de 1 GiB y 20 % de la menor disponibilidad entre
esa captura y la lectura nativa inmediatamente anterior a la etapa. El probe
de RAM no usa la captura inicial de la sesión para dimensionar la muestra.

Durante la carga se consulta RAM/energía cada segundo (intervalo de programación,
no garantía de tiempo real). RAM <256 MiB, energía insuficiente o pérdida de esas
lecturas solicita cancelación, espera al workload y al observador, y omite las
cargas posteriores. La cancelación del usuario y el timeout también esperan
esas tareas. Un fallo de workload ya observado no se oculta como simple omisión.
El resultado conserva conteos de muestras, medición/presupuesto RAM y fecha de
revalidación bajo métricas safety.*. Un fallo de medición no crea un P0 físico.

El contrato admite una señal térmica crítica fiable, sin umbral Celsius
universal. Si existe, detiene ante alarma o pérdida de esa fuente. El adaptador
Windows actual declara esa señal no disponible: no detecta todos los sensores
OEM ni acredita temperatura segura. Esta limitación aparece en el resultado.

WHEA posterior sin marca previa, con severidad/fecha no interpretable o salida
no vacía sin eventos interpretables queda Skipped, nunca comparación Passed.
Una consulta WHEA omitida bloquea carga posterior. Para puntuar cobertura de
carga se requieren WHEA previo/final comparables; reposo y enfriamiento no
incrementan esa cobertura. Cada una de CPU/RAM/lectura aporta un tercio si
terminó Passed o Failed; una etapa omitida/cancelada/timeout no cuenta.

La CPU ahora describe únicamente el workload completado: no afirma comprobar
exactitud aritmética. Cero iteraciones no aporta un Passed.

Fuentes de APIs, consultadas el 21 de octubre de 2026:
[GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex),
[GetSystemPowerStatus](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getsystempowerstatus) y
[SYSTEM_POWER_STATUS](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status).
La memoria disponible es una lectura volátil. Los límites de 256 MiB, 40 % de
batería y el presupuesto 20 %/1 GiB son política de WinMaint, no certificación
ni umbrales prescritos por esas APIs.

La suma planificada sigue siendo 245 segundos. Las consultas nuevas añaden
tiempo y tienen cancelación/timeout. El 21 de octubre se midieron 253.272 s del
proceso CLI completo (250.096 s del coordinador) bajo carga nativa Windows 11,
dentro de 4–6 minutos en ese equipo. Cancelación cooperativa durante CPU también
verificada; no acepta una candidata final ni garantiza ese rango en otros equipos.
Ver `docs/verification/astra-h04-native-2026-10-21.md` para evidencia y límites.
