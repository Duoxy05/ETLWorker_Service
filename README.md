# ETLWorker_Service — Extracción (E de ETL) del Sistema de Opiniones

Worker Service en **.NET 8** con Clean Architecture que extrae opiniones de tres fuentes
(CSV, BD relacional MySQL y API REST) y las deja en tablas *staging* de la BD analítica,
registrando logs (ILogger + Serilog) y hallazgos de calidad.

## Estructura
| Carpeta | Contenido |
|---|---|
| `src/ETLWorker.Domain` | Entidades (`RawOpinion`, `QualityIssue`, `ExtractionSummary`) y enums |
| `src/ETLWorker.Application` | `IExtractor`, orquestador, pipeline, validador, opciones |
| `src/ETLWorker.Infrastructure` | `CsvExtractor`, `DatabaseExtractor`, `ApiExtractor`, repositorio MySQL |
| `src/ETLWorker.Worker` | Host (`Program.cs`, `EtlWorker`), `appsettings.json` |
| `src/ETLWorker.MockSocialApi` | API REST simulada que sirve `social_comments.csv` |
| `database/` | Scripts MySQL: BD origen y BD analítica (staging + bitácora) |
| `data/` | CSV de entrada |
| `docs/` | Diagramas de arquitectura y de flujo |

## Puesta en marcha
1. **MySQL 8**: ejecutar `database/01_origen_resenas_web.sql` y `database/02_analitica_staging.sql`
   (cambie `CAMBIAR_ESTA_CLAVE` por contraseñas reales).
2. **Secretos** (nunca van en el repositorio), desde `src/ETLWorker.Worker`:
   ```
   dotnet user-secrets set "ConnectionStrings:OrigenWeb" "Server=localhost;Port=3306;Database=OpinionesOrigenDB;User ID=etl_reader;Password=TU_CLAVE"
   dotnet user-secrets set "ConnectionStrings:Analitica" "Server=localhost;Port=3306;Database=SistemaOpinionesDW;User ID=etl_writer;Password=TU_CLAVE"
   dotnet user-secrets set "Secrets:SocialApiKey" "dev-local-key-123"
   ```
   En producción use variables de entorno: `ConnectionStrings__Analitica`, `Secrets__SocialApiKey`.
3. **API simulada** (terminal 1): `dotnet run --project src/ETLWorker.MockSocialApi`
4. **Worker** (terminal 2): `dotnet run --project src/ETLWorker.Worker`
5. Verificar: `SELECT * FROM etl_ejecucion;` y `SELECT Fuente, Problema, COUNT(*) FROM etl_calidad_datos GROUP BY 1,2;`

## Agregar una fuente nueva
Agregue un bloque en `Etl:Sources` de `appsettings.json` (tipo, tabla `stg_*` y mapeo de campos).
Si es de un tipo existente no se escribe código; si es un tipo nuevo, cree una clase que implemente
`IExtractor` y regístrela en `InfrastructureServiceRegistration`.

## Carga de dimensiones del almacén de datos (T/L) con procedimientos almacenados

Después de extraer a staging, el mismo Worker carga las 5 dimensiones del modelo estrella de `SistemaOpinionesDW`
**llamando a procedimientos almacenados de MySQL** (`CommandType.StoredProcedure`).

| Dimensión | Procedimiento | Origen | Reglas principales |
|---|---|---|---|
| `Dim_Cliente` | `usp_LoadCliente` | `data/clients.csv` | Valida ID, nombre y duplicados; agrega `-1 Anónimo` y `-2 Cliente no encontrado` |
| `Dim_Producto` | `usp_LoadProducto` | `data/products.csv` | Valida ID, nombre y duplicados; agrega `-1 Producto no encontrado` |
| `Dim_Fuente` | `usp_LoadFuente` | Configuración + valores distintos de `PlataformaRaw` en staging | Clave natural (Canal, Plataforma); el procedimiento asigna el siguiente `IdFuente` libre |
| `Dim_Fecha` | `usp_LoadFecha` | Generada por código (2024–2025) | `IdFecha = AAAAMMDD`; agrega `-1 Fecha desconocida` |
| `Dim_Sentimiento` | `usp_LoadSentimiento` | Valores fijos del modelo | 1 Positiva (+1), 2 Neutra (0), 3 Negativa (−1) |

**Cómo funciona cada procedimiento:**
- Recibe las filas como un arreglo JSON (`p_filas`). MySQL no tiene parámetros de tipo tabla (`SqlDbType.Structured`),
  así que el equivalente es enviar todo en un parámetro y expandirlo con `JSON_TABLE`: una sola llamada por dimensión.
- Revisa con `COUNT(*)` qué filas ya existen. **Si el registro ya existe no se inserta y el mensaje dice
  "... SE ENCUENTRA REGISTRADA"**; solo se insertan las que faltan.
- Devuelve por parámetros de salida: `p_insertadas`, `p_existentes`, `p_codigo` (0 insertó, 1 ya registrada, 2 sin filas,
  −1 error) y `p_result` (mensaje).
- Corre en una transacción con manejador de errores (`ROLLBACK` + código −1).
- El usuario `etl_loader` solo tiene `EXECUTE` sobre los procedimientos (`SQL SECURITY DEFINER`).

El Worker deja una fila por dimensión en la bitácora `etl_carga_dimensiones` (leídas, rechazadas, insertadas,
ya registradas, código de retorno, duración y el mensaje del procedimiento).

### Pasos
1. Con MySQL como administrador, ejecutar en orden (cambie `CAMBIAR_ESTA_CLAVE` en el 03 por la contraseña de `etl_loader`):
   `database/03_dw_dimensiones.sql` (dimensiones, bitácora, usuario) y `database/04_procedimientos_dimensiones.sql` (procedimientos y permisos).
2. Agregar el secreto del usuario (desde `src/ETLWorker.Worker`):
   ```
   dotnet user-secrets set "ConnectionStrings:AnaliticaCarga" "Server=localhost;Port=3306;Database=SistemaOpinionesDW;User ID=etl_loader;Password=TU_CLAVE"
   ```
3. Ejecutar (con la API simulada encendida si va a extraer): `dotnet run --project src/ETLWorker.Worker`
   El Worker extrae a staging y, si todo salió bien, carga las dimensiones.
   Para cargar **solo** dimensiones con el staging que ya existe, ponga `"RunExtraction": false` en `Etl` (appsettings.json).
4. Verificar con `database/05_verificacion_dimensiones.sql` (conteos esperados en el encabezado del script) y probar los
   procedimientos a mano con `database/06_prueba_procedimientos.sql`.

### Configuración (`Etl:Dimensions` en appsettings.json)
- `CalendarStartYear` / `CalendarEndYear`: años que cubre `Dim_Fecha`.
- `Fuentes`: cada bloque define un `Canal` y, o bien una `Plataforma` fija, o bien una `StagingTable` de la que se descubren las plataformas.
  Si su `Dim_Fuente` ya tiene valores distintos (por ejemplo otro nombre para la plataforma de encuestas), ajuste este bloque para que coincidan.
- Para agregar una dimensión nueva: un procedimiento `usp_Load...`, un método en `IDimensionRepository`, una clase que
  implemente `IDimensionLoader` y una línea en `AddApplication()`.
