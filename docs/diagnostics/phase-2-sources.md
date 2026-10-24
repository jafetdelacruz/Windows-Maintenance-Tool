# Fuentes de diagnóstico — Fase 2

Esta fase solo recopila evidencia R0. No repara, limpia, optimiza, instala ni
cambia configuración. Cada dato conserva su fuente y cobertura; si una API, un
contador o un permiso no está disponible, el valor visible es `Desconocido`.

## Fuentes nativas

- Versión real de Windows: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`,
  abierto explícitamente en modo de solo lectura. La familia se clasifica por
  build para evitar el valor heredado `ProductName`: 10240–21999 es Windows 10 y
  22000 o superior es Windows 11.
- Energía: `GetSystemPowerStatus`.
- Memoria global: `GlobalMemoryStatusEx`.
- Uso de CPU: dos muestras de `GetSystemTimes`, separadas por 350 ms.
- Modelo de CPU y señales de reinicio pendiente: claves del registro abiertas
  con `writable: false`.
- Procesos: propiedades expuestas por `System.Diagnostics.Process`, sin abrir ni
  alterar procesos ajenos.
- Volúmenes: `System.IO.DriveInfo`, únicamente consultas.

## Consultas externas permitidas

La ruta del ejecutable siempre es absoluta, no se usa shell y los argumentos se
añaden con `ProcessStartInfo.ArgumentList`. Los scripts son constantes del
programa; no incorporan texto del usuario.

- Windows PowerShell 5.1, no interactivo, para `Get-CimInstance
  Win32_PhysicalMemory`.
- Windows PowerShell 5.1, no interactivo, para `Get-PhysicalDisk` y
  `Get-StorageReliabilityCounter`.
- `wevtutil qe System` con una consulta XPath fija de los últimos siete días
  para niveles crítico/error y el proveedor `Microsoft-Windows-WHEA-Logger`.

La Fase 2 comprueba que `dism.exe` y `sfc.exe` existen, pero no los ejecuta.
Incluso los modos de comprobación pueden escribir logs del sistema, por lo que
la evaluación explícita de integridad pertenece a un flujo mutable controlado
de una fase posterior. El estado de integridad se muestra como `Desconocido`.

## Referencias oficiales

- [GetSystemPowerStatus](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getsystempowerstatus)
- [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
- [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)
- [Get-CimInstance](https://learn.microsoft.com/en-us/powershell/module/cimcmdlets/get-ciminstance)
- [Get-PhysicalDisk](https://learn.microsoft.com/en-us/powershell/module/storage/get-physicaldisk)
- [Get-StorageReliabilityCounter](https://learn.microsoft.com/en-us/powershell/module/storage/get-storagereliabilitycounter)
- [wevtutil](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wevtutil)
- [Repair a Windows image with DISM](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image)
