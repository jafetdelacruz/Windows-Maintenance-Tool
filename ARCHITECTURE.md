# Arquitectura — WinMaint

## 1. Decisión general

WinMaint será un monolito modular de consola. No necesita servicios, base de
datos, servidor ni sistema de plugins en v1.

La solución separa presentación, reglas, casos de uso e integración con Windows
para poder probar el comportamiento sin ejecutar mantenimiento real.

## 2. Plataforma

- Runtime: .NET 10 LTS.
- Target: `net10.0-windows`.
- Runtime identifier: `win-x64`.
- Publicación: self-contained, single-file.
- Interfaz: Spectre.Console.
- Idioma: español.

.NET 10 reemplaza la propuesta inicial de .NET 8. La razón es su ciclo de
soporte: noviembre de 2028 frente a noviembre de 2026.

## 3. Estructura prevista

```text
src/
├── WinMaint.Cli/                  TUI y composición
├── WinMaint.Application/          Casos de uso y orquestación
├── WinMaint.Domain/               Hallazgos, puntuación y contratos
├── WinMaint.Infrastructure.Win/   APIs y comandos de Windows
└── WinMaint.Reporting/            TXT y JSON

tests/
├── WinMaint.Domain.Tests/
├── WinMaint.Application.Tests/
└── WinMaint.Windows.Tests/
```

Aunque haya varios proyectos, se publicará un solo ejecutable.

## 4. Flujo principal

```text
Usuario
  │
  ▼
TUI ──► Caso de uso ──► Proveedores de diagnóstico
              │                    │
              ▼                    ▼
       Motor de hallazgos     Windows/WMI/CIM/Eventos
              │
              ▼
       Prioridad + riesgo
              │
              ▼
       Plan y confirmación
              │
              ▼
       Ejecutor de acciones
              │
              ▼
      Verificación + reporte
```

## 5. Componentes

### 5.1 Presentación

- Tablero, paneles y tablas.
- Selección única y múltiple.
- Progreso, spinners y cancelación.
- Fallback sin color para consolas limitadas.
- Nunca contiene reglas de salud ni ejecuta comandos directamente.

Los prompts y los paneles de progreso se ejecutan en etapas distintas porque
Spectre.Console no garantiza que ambos operen simultáneamente.

### 5.2 Aplicación

- Coordina diagnósticos.
- Construye planes de mantenimiento.
- Aplica política de concurrencia.
- Solicita confirmaciones mediante un puerto de interacción.
- Produce snapshots antes y después.

### 5.3 Dominio

Tipos centrales previstos:

- `DiagnosticFinding`.
- `Priority` P0-P3.
- `RiskLevel` R0-R3.
- `Evidence` y `EvidenceCoverage`.
- `HealthDomainScore`.
- `HealthAssessment`.
- `MaintenanceActionDescriptor`.
- `ExecutionPlan`.
- `ActionResult`.
- `SystemSnapshot`.

El dominio no referencia PowerShell, WMI, Spectre.Console ni rutas de Windows.

### 5.4 Infraestructura Windows

Adaptadores previstos:

- Información de sistema y energía.
- WMI/CIM y Storage Management Provider.
- Performance Counters.
- Windows Event Log y WHEA.
- PowerShell controlado.
- Ejecución de DISM, SFC, CHKDSK y Optimize-Volume.
- Apertura de la ficha oficial de Store para instalación manual de PC Manager
  (ADR-004); WinGet queda fuera del flujo de v1.
- AppX/MSIX para detectar PC Manager.
- Sistema de archivos y papelera.

WinSAT será una fuente opcional. Su ausencia o estado inválido no falla el
diagnóstico.

### 5.5 Reportes

En Fase 8, la opción 5 recibe `ITemporaryInventoryProvider` (solo agregados).
`WindowsPreparedTemporaryInventoryProvider` usa la preparación nativa del
usuario actual sin exponer rutas, mapa de candidatos ni runner a la TUI.
La composición pública nunca habilita borrado. El reporte conserva directamente
el `TemporaryInventory` original: identificador, fecha y contadores long/null,
sin reconstruirlos desde double. El antiguo proveedor agregado se conserva
para compatibilidad y fixtures, pero ya no compone la opción 5.

El componente mutable sigue restringido a fixtures propias de Sandbox. Su
aceptación no habilita producción ni acredita una nueva candidata de CLI.

- Modelo independiente de la TUI.
- Texto para lectura humana.
- JSON versionado para depuración y futuras migraciones.
- Redacción de identificadores sensibles antes de serializar.

Ruta predeterminada: `%ProgramData%\WinMaint\Reports`.

## 6. Ejecución segura de procesos

Todo comando externo debe usar un ejecutor común con:

- Ruta de ejecutable conocida.
- Lista de argumentos sin concatenación de shell.
- Captura separada de stdout y stderr.
- Codificación explícita y tolerancia a localización.
- Timeout por operación.
- Cancelación y terminación controlada.
- Códigos de salida documentados.
- Registro del comando redactado.
- Parser específico y fallback a resultado no estructurado.

Nunca se interpretará texto del hardware o del usuario como comando.

## 7. Elevación

La versión 1 solicita UAC al iniciar. Esto evita relanzamientos parciales y
pérdida del plan seleccionado. La elevación no equivale a autorización para
modificar: cada lote mutable exige confirmación dentro de WinMaint.

## 8. Concurrencia

- Los diagnósticos R0 independientes pueden ejecutarse en paralelo.
- Las acciones mutables se ejecutan secuencialmente por defecto.
- Cada acción declara recursos: CPU, RAM, disco, red, Store o reinicio.
- El planificador evita combinaciones conflictivas.
- La instalación manual de PC Manager queda bajo control del usuario en Store.
- No se mezcla un prompt interactivo con un renderizador de progreso activo.

## 9. Motor de puntuación

Las reglas tienen identificador y versión. La primera implementación puede
embutirlas como recursos JSON validados al compilar.

Proceso:

1. Los proveedores crean evidencia.
2. Las reglas producen hallazgos.
3. Los hallazgos calculan puntuaciones por dominio.
4. La cobertura calcula confianza.
5. Los límites P0/P1 se aplican al final.
6. El reporte conserva entradas, versión y resultado.

El rendimiento se reporta aparte. Las comparaciones antes/después solo muestran
métricas equivalentes capturadas en ambas sesiones.

## 10. Prueba de estabilidad

Los workloads son pequeños, acotados y reemplazables:

- `CpuStabilityProbe`.
- `MemoryPatternProbe`.
- `StorageReadProbe`.
- `CoolDownProbe`.
- `HardwareEventProbe` antes y después.

Cada probe implementa precondiciones, timeout, cancelación y resultado parcial.
El coordinador bloquea toda la prueba ante un hallazgo P0 incompatible.

## 11. Persistencia mínima

No habrá base de datos.

- Reportes TXT y JSON.
- Estado pequeño de sesión para detectar una ejecución interrumpida.
- Escritura atómica mediante archivo temporal y reemplazo.
- Limpieza de archivos temporales al cerrar y recuperación en el siguiente inicio.

## 12. Pruebas

### Fuera de Windows

- Reglas P0-P3.
- Cálculo de puntuación y confianza.
- Construcción y bloqueo de planes.
- Redacción de reportes.
- Parsers con fixtures capturados y anonimizados.

### En Windows

- Adaptadores WMI/CIM.
- Registro de eventos.
- Elevación UAC.
- Procesos nativos y cancelación.
- Detección, apertura de PC Manager y apertura de su ficha oficial de Store.
- Consola CMD y Windows Terminal.
- Empaquetado self-contained.

Las pruebas mutables deben ejecutarse en una VM o equipo de laboratorio con
snapshot o respaldo.

## 13. Seguridad y privacidad

- Sin telemetría.
- Sin descargas arbitrarias.
- Dependencias fijadas y revisadas.
- PC Manager solo desde fuente oficial.
- Números de serie ocultos por defecto.
- Reportes locales.
- No se almacenan cliente ni folio.
- Ninguna acción avanzada queda accesible en v1.

## 14. Extensión futura

El catálogo de acciones soportará los perfiles `Safe`, `Advanced` y
`Experimental`, pero v1 compilará y mostrará únicamente `Safe`.

Agregar un modo agresivo requerirá un ADR nuevo, amenazas revisadas, estrategia
de reversión y pruebas destructivas en VM. No se activará mediante una opción
oculta o un archivo editable.
