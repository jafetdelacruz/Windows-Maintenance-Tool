# ADR-001: Plataforma y TUI

## Estado

Aceptado.

## Contexto

WinMaint debe ser portátil, ejecutarse en Windows 10/11 x64 y ofrecer una
experiencia visual sin interfaz gráfica de escritorio.

## Decisión

Usar C# con .NET 10 LTS, target `net10.0-windows`, publicación self-contained
single-file `win-x64` y Spectre.Console para la TUI.

## Razones

- Integración directa con APIs y herramientas de Windows.
- Un ejecutable no depende de un runtime preinstalado.
- Spectre.Console cubre tablas, paneles, selección múltiple y progreso.
- .NET 10 permanece soportado hasta noviembre de 2028.

## Alternativas

- PowerShell empaquetado: menor estructura, más falsos positivos y distribución
  menos predecible.
- Rust: binario pequeño, pero mayor costo de integración con Windows y TUI.
- .NET 8: soporte demasiado próximo a terminar para un proyecto nuevo.

## Consecuencias

- El binario será mayor que uno dependiente del runtime.
- La validación real requiere Windows.
- La dependencia TUI debe fijarse y revisarse.

## Revisar cuando

- Se requiera ARM64.
- El tamaño del binario sea un bloqueo real.
- Cambie el soporte de .NET 10.
