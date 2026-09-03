# Especificación de producto — WinMaint v1

## 1. Propósito

Reducir el tiempo de diagnóstico y mantenimiento rutinario de computadoras
lentas mediante una herramienta visual de terminal, portátil y segura para uso
del técnico.

La herramienta debe producir evidencia comprensible para el propietario del
equipo sin presentar una prueba breve como certificación absoluta del hardware.

## 2. Alcance operativo

- Sistemas: Windows 10 y Windows 11.
- Arquitectura: x64 exclusivamente.
- Ejecución: CMD, Windows Terminal o consola compatible.
- Distribución: un ejecutable self-contained; no requiere instalar .NET.
- Usuario: técnico individual.
- Idioma inicial: español.
- Conectividad: opcional, salvo instalación de PC Manager o reparación que use
  Windows Update como fuente.
- Privilegios: solicitud UAC al iniciar.

Windows 10 permanece dentro del alcance técnico, pero WinMaint debe avisar que
su soporte estándar terminó el 14 de octubre de 2025 y no debe asumir que el
equipo está inscrito en ESU.

## 3. Fuera de alcance en v1

- Interfaz gráfica de escritorio.
- Plataformas ARM64 o x86.
- Campo de cliente, folio o sistema de tickets.
- Administración remota.
- Telemetría.
- Desinstalación masiva de software.
- Eliminación automática de aplicaciones de inicio.
- Limpieza agresiva de registro, WinSxS o Windows Update.
- Desactivación de seguridad, actualizaciones o servicios esenciales.
- Overclocking, undervolting o stress test extremo.
- Certificación de hardware.

## 4. Experiencia principal

### 4.1 Inicio

1. Validar Windows 10/11 x64.
2. Solicitar privilegios administrativos.
3. Crear identificador local de ejecución.
4. Capturar información básica del sistema.
5. Mostrar tablero y menú principal.

### 4.2 Menú

1. Diagnóstico completo.
2. Mantenimiento recomendado.
3. RAM y almacenamiento.
4. Prueba rápida de estabilidad.
5. Limpieza.
6. Integridad de Windows.
7. Inicio y procesos.
8. Microsoft PC Manager.
9. Reportes.
10. Salir.

### 4.3 Modos

- **Solo diagnosticar:** no modifica el sistema.
- **Mantenimiento seguro:** propone acciones rutinarias y limitadas.
- **Reparación profunda:** contiene acciones lentas, disruptivas o con reinicio.
- **Selección personalizada:** permite elegir varias acciones con casillas.

Antes de ejecutar se mostrará el plan completo, riesgo, requisitos y efectos.

## 5. Diagnóstico

### 5.1 Sistema operativo

- Edición, versión y build.
- Fecha del último arranque.
- Reinicio pendiente cuando sea detectable.
- Estado de soporte conocido.
- Integridad de componentes y archivos mediante comprobaciones explícitas.
- Errores críticos recientes del registro de eventos.

### 5.2 RAM

- Capacidad total, utilizada y disponible.
- Memoria comprometida y límite.
- Módulos, bancos, fabricante y velocidad cuando estén disponibles.
- Presión actual y muestra temporal.
- Eventos WHEA relacionados.
- Resultado previo de Windows Memory Diagnostic cuando exista.

WinMaint no mostrará un porcentaje físico de salud de RAM sin evidencia. El
estado será: sin señales de fallo, presión, posible fallo, fallo detectado o
diagnóstico insuficiente.

### 5.3 Almacenamiento

- Discos físicos, modelo y tipo de medio.
- Estado operativo y HealthStatus.
- Temperatura, desgaste, horas y errores cuando el dispositivo los reporte.
- Volúmenes, sistema de archivos y espacio libre.
- Estado de TRIM y fragmentación aplicable.
- Estado sucio o errores del volumen.
- Nivel de cobertura de sensores.

Un contador ausente se mostrará como `No disponible`.

### 5.4 Rendimiento y procesos

- Uso de CPU y memoria.
- Procesos con mayor consumo.
- Programas configurados al inicio y su impacto disponible.
- Métricas de almacenamiento y capacidad de respuesta.

Las métricas de rendimiento no forman parte directa de la salud. Un equipo
puede ser lento y estar sano.

## 6. Prioridad y riesgo

La prioridad expresa urgencia:

| Nivel | Significado | Ejemplo |
|---|---|---|
| P0 | Acción inmediata | Fallo predictivo o errores de disco no corregidos |
| P1 | Problema grave | Corrupción, espacio crítico o integridad dañada |
| P2 | Degradación importante | Presión sostenida de RAM o poco espacio |
| P3 | Prevención o información | Temporales, mantenimiento o herramienta ausente |

El riesgo de una acción se expresa por separado:

- R0: solo lectura.
- R1: cambio seguro y reversible.
- R2: cambio sensible o lento.
- R3: disruptivo, requiere respaldo o reinicio.

Una recomendación puede ser P0/R3. La urgencia nunca autoriza ejecución
automática.

## 7. Puntuación de salud

### 7.1 Dominios

| Dominio | Peso |
|---|---:|
| Fiabilidad del hardware | 30% |
| Almacenamiento | 25% |
| RAM | 20% |
| Integridad de Windows | 15% |
| Estabilidad bajo carga | 10% |

Cada dominio produce una puntuación entre 0 y 100 basada en hallazgos
versionados. La puntuación general es el promedio ponderado de los dominios con
evidencia suficiente.

### 7.2 Confianza

Cada fuente declara cobertura completa, parcial o ausente. La confianza general
es la cobertura ponderada de las fuentes requeridas. La falta de datos reduce la
confianza, pero no penaliza la salud.

### 7.3 Límites

- Cualquier P0 limita la puntuación general a 39.
- Cualquier P1 no resuelto limita la puntuación general a 59.
- La puntuación siempre muestra versión de reglas y nivel de confianza.
- El texto permitido es `sin fallos detectados durante esta prueba`.
- El texto `100% saludable`, `certificado` o equivalentes está prohibido.

### 7.4 Bandas visuales

- 90–100: Excelente.
- 75–89: Buena.
- 60–74: Requiere atención.
- 40–59: Deficiente.
- 0–39: Crítica.

## 8. Prueba rápida de estabilidad

Duración objetivo: 4–6 minutos. Es opcional y nunca se inicia automáticamente.

### 8.1 Preflight

- Confirmar corriente o batería suficiente.
- Verificar espacio libre y estado del disco.
- Medir carga inicial.
- Consultar eventos WHEA recientes.
- Avisar sobre trabajo abierto.
- Bloquear si existe P0 de almacenamiento o una condición insegura.

### 8.2 Etapas

1. Línea base en reposo.
2. CPU controlada durante aproximadamente 30 segundos.
3. RAM durante 45–60 segundos, usando como máximo el menor valor entre 1 GB y
   20% de la memoria disponible.
4. Lectura secuencial limitada del almacenamiento.
5. Enfriamiento y segunda consulta de eventos.

La escritura temporal en disco estará desactivada por defecto en v1.

### 8.3 Cancelación y seguridad

- `Esc` y `Ctrl+C` solicitan cancelación.
- Cada etapa tiene timeout.
- Se detiene ante errores nuevos, falta de recursos o temperatura crítica cuando
  exista telemetría fiable.
- Si WinMaint se interrumpe, la siguiente ejecución detecta la sesión incompleta.
- La prueba no reemplaza MemTest86, diagnósticos del fabricante ni una prueba de
  taller prolongada.

## 9. Mantenimiento

### 9.1 Limpieza inicial

- Inventario y tamaño antes de borrar.
- Archivos temporales con antigüedad mínima configurable internamente.
- Papelera como opción separada.
- Exclusión de archivos bloqueados o en uso.
- Resumen exacto de archivos y bytes liberados.

### 9.2 Almacenamiento

- Análisis previo.
- Optimización por tipo de medio mediante APIs/comandos de Windows.
- Retrim para SSD cuando corresponda.
- Desfragmentación solo cuando Windows determine que aplica.
- `chkdsk /scan` antes de proponer reparación con reinicio.
- `chkdsk /r` excluido del mantenimiento rutinario.

### 9.3 Integridad de Windows

- Detección antes de reparación.
- DISM y SFC en el orden recomendado por Microsoft.
- Progreso real si la salida lo permite; spinner y tiempo si no.
- Reinicio nunca automático sin confirmación final.

### 9.4 Microsoft PC Manager

- Detectar paquete instalado.
- Verificar requisitos de sistema.
- Pedir autorización expresa.
- Instalar mediante el identificador de Microsoft Store `9PM860492SZD` usando
  WinGet y la fuente `msstore`.
- Abrir la ficha oficial si WinGet o Store no permiten instalar.
- Permitir abrir PC Manager después de instalar.
- No automatizar controles internos sin una API pública documentada.

La instalación puede coexistir con diagnósticos de solo lectura. No coexistirá
con limpieza o reparación que compita por recursos.

## 10. Reportes

Formatos: texto legible y JSON técnico.

Contenido:

- Fecha, equipo y versión de WinMaint.
- Versión/build de Windows.
- Puntuación, confianza y reglas utilizadas.
- Hallazgos P0-P3.
- Pruebas ejecutadas, omitidas y canceladas.
- Acciones y resultados.
- Métricas antes/después.
- Reinicios o acciones pendientes.

No se incluye cliente/folio. Los números de serie se ocultan por defecto.

## 11. Extensibilidad para modo agresivo

Toda acción debe declarar:

- Identificador estable y versión.
- Perfil mínimo: seguro, avanzado o experimental.
- Prioridad y riesgo.
- Requisitos de administrador, red, reinicio y exclusividad.
- Soporte de simulación.
- Estrategia de reversión.
- Precondiciones y bloqueos.
- Recursos que consume.

La versión 1 solo expone el perfil seguro. Los perfiles avanzado y experimental
no deben aparecer en la interfaz.

## 12. Criterios de aceptación de v1

- Se publica un `.exe` x64 self-contained.
- Funciona en Windows 10 22H2 x64 y una versión soportada de Windows 11 x64.
- La TUI funciona en CMD y Windows Terminal.
- El modo diagnóstico no modifica el sistema.
- Una alerta P0 impide pruebas intensivas.
- Todas las acciones mutables requieren revisión y confirmación.
- Cancelar no deja archivos temporales conocidos.
- Los reportes distinguen dato ausente de resultado sano.
- La prueba rápida termina dentro del rango objetivo en el equipo de referencia.
- Las pruebas unitarias y de integración definidas en el roadmap pasan.
