# VidDown Pro

Descargador de videos de escritorio para Windows, construido con .NET 8 y yt-dlp.  
Soporta más de 1000 sitios: YouTube, Twitch, Twitter/X, Reddit, Vimeo y muchos más.

---

## Requisitos del sistema

| Requisito | Versión mínima | Obligatorio |
|-----------|---------------|-------------|
| Windows   | 10 / 11       | ✓           |
| .NET Runtime 8 | 8.0      | ✓           |
| yt-dlp    | cualquiera    | ✓           |
| ffmpeg    | cualquiera    | Recomendado |

> **ffmpeg** es necesario para fusionar video+audio en MP4, extraer audio MP3,
> y embeber miniaturas. Sin él solo funcionan formatos que ya vienen combinados.

---

## Instalación paso a paso

### 1 — .NET Runtime 8

1. Abre **https://dotnet.microsoft.com/download/dotnet/8.0**
2. En la sección **"Run apps"**, descarga **.NET Desktop Runtime 8** (x64)
3. Ejecuta el instalador y sigue los pasos

Verifica que quedó instalado abriendo una terminal (`Win + R` → `cmd`) y escribiendo:
```
dotnet --version
```
Debe mostrar algo como `8.0.x`.

---

### 2 — yt-dlp

**Opción A — winget (recomendado, 1 comando):**
```
winget install yt-dlp
```
Cierra y vuelve a abrir la terminal después de instalarlo.

**Opción B — manual:**
1. Ve a **https://github.com/yt-dlp/yt-dlp/releases/latest**
2. Descarga **yt-dlp.exe**
3. Copia el archivo a la carpeta del programa:
   ```
   VideoDownloader\bin\Debug\net8.0\yt-dlp.exe
   ```

Verifica:
```
yt-dlp --version
```

---

### 3 — ffmpeg

**Opción A — winget:**
```
winget install ffmpeg
```

**Opción B — manual:**
1. Ve a **https://www.gyan.dev/ffmpeg/builds/**
2. Descarga **ffmpeg-release-essentials.zip**
3. Descomprime y copia estos 3 archivos a la carpeta del programa:
   ```
   VideoDownloader\bin\Debug\net8.0\ffmpeg.exe
   VideoDownloader\bin\Debug\net8.0\ffprobe.exe
   VideoDownloader\bin\Debug\net8.0\ffplay.exe
   ```

Verifica:
```
ffmpeg -version
```

---

### 4 — Compilar y ejecutar el proyecto

Si solo tienes los archivos fuente (`.cs`, `.csproj`), necesitas también el **SDK de .NET 8**
(distinto al Runtime — incluye el compilador):

1. Descarga desde **https://dotnet.microsoft.com/download/dotnet/8.0** → sección **"Build apps"** → **SDK 8**
2. Instala y abre una terminal en la carpeta del proyecto (donde está `VideoDownloader.csproj`)
3. Restaura dependencias y compila:
   ```
   dotnet restore
   dotnet build
   ```
4. Ejecuta:
   ```
   dotnet run
   ```
   O navega a `bin\Debug\net8.0\` y abre `VideoDownloader.exe` directamente.

---

## Dependencias NuGet

El proyecto usa una sola librería externa, que se descarga automáticamente con `dotnet restore`:

| Paquete | Versión | Para qué sirve |
|---------|---------|----------------|
| [Spectre.Console](https://spectreconsole.net/) | ≥ 0.49 | Toda la interfaz visual: menús, colores, barras de progreso, tablas, paneles |

No se necesita instalar nada de NuGet manualmente.

---

## Estructura del proyecto

```
VideoDownloader/
├── src/
│   ├── Program.cs           — menú principal y lógica de UI
│   ├── DownloadEngine.cs    — llamadas a yt-dlp y ffmpeg
│   ├── HistoryManager.cs    — lectura/escritura del historial JSON
│   └── Models.cs            — clases de datos (VideoInfo, DownloadRecord, etc.)
├── bin/Debug/net8.0/
│   ├── VideoDownloader.exe
│   ├── yt-dlp.exe           — colocar aquí si no está en el PATH
│   ├── ffmpeg.exe           — colocar aquí si no está en el PATH
│   ├── ffprobe.exe
│   └── download_history.json  — se crea automáticamente
└── VideoDownloader.csproj
```

---

## Solución de problemas frecuentes

| Problema | Causa probable | Solución |
|----------|---------------|----------|
| `yt-dlp no encontrado` | No está instalado o no está en PATH | Ver paso 2 |
| Error al fusionar video | ffmpeg no encontrado | Ver paso 3 |
| `Requested format is not available` | El sitio usa HLS/m3u8 | Usar opción **"Mejor calidad"** en vez de una resolución fija |
| La barra de progreso no avanza | ffmpeg está post-procesando | Esperar, es normal al final de la descarga |
| Caracteres raros en la terminal | Encoding incorrecto | Ejecutar `chcp 65001` en la terminal antes de abrir el programa |
| `dotnet` no reconocido | .NET no instalado o no en PATH | Ver paso 1, reiniciar la terminal después |

---

## Actualizar yt-dlp

Desde dentro del programa, en el menú principal → **🔄 Actualizar yt-dlp**.

O manualmente desde terminal:
```
yt-dlp -U
```

---

## Carpeta de descargas por defecto

```
C:\Users\<tu usuario>\Videos\VideoDownloader\
```

Se puede cambiar desde el menú → **📁 Cambiar carpeta de descarga**.