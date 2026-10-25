# Rankeo API — Plan de implementación

Backend .NET 10 + PostgreSQL + SignalR para Rankeo (tier lists en grupo). Sustituye a `LocalRoomService`
(salas en `localStorage`) por salas compartidas en vivo: todos los que abren el mismo código ven los
mismos cambios al momento.

> **Estado actual:** ver [Avance](#avance) al final. Trabajar siempre la siguiente fase sin marcar.

## Dónde está cada cosa

| Qué | Ruta |
|---|---|
| Backend (este repo) | `D:\YaxonFolder\Github\rankeo-backend` → remoto `origin` = https://github.com/YaxOFF/rankeo-backend |
| Frontend | `D:\YaxonFolder\Github\rankeo-frontend` (repo git; push al remoto `yaxoff` = https://github.com/YaxOFF/rankeo-frontend- ; `origin` = David54s, sin permiso de push) |
| Docs del frontend (leer primero) | `rankeo-frontend/docs/MAP.md`, `docs/architecture/state-management.md`, `docs/architecture/data-layer.md` |
| Lógica de negocio a portar | `src/room/roomReducer.ts` (todas las mutaciones), `src/room/roomService.ts` (bitácora, códigos, demo) |
| Modelo / contrato | `src/types.ts` (`Room`, `Tier`, `Item`, `RoomAction`, `ActivityEntry`, `POOL`) |
| Utilidades a portar | `src/lib/ids.ts` (`generateCode`, `normalizeCode`, `colorFor`), `src/lib/demo.ts` (`newRoom`, `MAX_TIERS`, `TIER_COLORS`) |
| Textos de bitácora (hoy en el cliente) | `src/pages/RoomPage.tsx` (`addImages`, `addTier`), `src/components/room/TierBoard.tsx` (`onDragEnd`, `onKeyDown`, `onClear`, `onDelete`, `placeName`) |
| Vistas que consumirán la API | `src/room/useRoom.ts`, `src/components/home/Hero.tsx`, `src/components/home/JoinModal.tsx`, `src/pages/RoomPage.tsx` |
| Pruebas existentes | `src/room/roomReducer.test.ts` (9 casos Vitest — se portan 1:1 a xUnit) |

El frontend va en **otro contenedor/origen** (lo despliega el usuario); habla con la API por HTTP
(`VITE_API_URL`) y por SignalR (`/hubs/sala`).

## Primeros pasos

Requisitos verificados: .NET SDK 10.0.401 (también 8.0.419), Docker 29.8.2, Compose v5.6.0.

```bash
cp .env.example .env
docker compose up -d db
dotnet run --project Rankeo.Api
dotnet test                       # Docker encendido (Testcontainers)
docker compose up --build         # stack completo
```

## Contexto

La demo ya está diseñada para esto: toda mutación es una `RoomAction` aplicada por un reducer puro, y
la UI solo habla con la interfaz `RoomService` (`exists/create/load/dispatch/subscribe`). El README
del frontend lo dice: "implementa `RoomService` (por ejemplo con WebSockets)". Hoy la portada promete
"salas en tiempo real" pero dos navegadores (ni dos pestañas) no comparten sala.

Decisiones ya tomadas con el usuario:
- **Grupos:** grupo = sala. La gente entra con el código y un apodo; **sin cuentas ni login** (igual que la demo: "sin cuentas ni instalaciones").
- **Tiempo real:** **SignalR** (viene con ASP.NET Core; usa WebSockets por debajo; grupos por sala nativos).
- **Imágenes:** archivos en **disco (volumen Docker)**, servidos por la API.
- **Frontend:** otro contenedor/origen → CORS con orígenes explícitos.

## 1. Análisis funcional (requisitos confirmados en la demo)

| Módulo | Funcionalidad | Reglas (fuente) |
|---|---|---|
| Salas | Crear con título | Título vacío → `'Mi Tier List'`; máx 80 (`Hero` `maxLength`); filas iniciales S/A/B/C/D con `TIER_COLORS[0..4]` (`newRoom`) |
| Salas | Código | 6 chars de `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, único, nunca `DEMO42` (`generateCode`, `LocalRoomService.create`) |
| Salas | Unirse / existe | Código normalizado: `trim`, mayúsculas, solo `A-Z0-9` (`normalizeCode`); `JoinModal` exige 6 chars; inexistente → "No existe ninguna sala con el código X." |
| Imágenes | Agregar | Llega al final del banco (`item:add`); nombre = archivo sin extensión, máx 30, o `'Imagen'`; `by` = apodo; cliente recorta a 256px webp (`shrink`) |
| Imágenes | Mover | `item:move`: quitar de su lista, insertar en `target` en `index` acotado a `[0, len]`, default final; target o item inexistente → sin cambio |
| Imágenes | Eliminar | `item:delete`: quitar de su lista y del índice `items` |
| Filas | Añadir | `tier:add`: etiqueta `'Nuevo'`, color dado; máx `MAX_TIERS = 15` → "Máximo 15 filas." |
| Filas | Editar | `tier:update`: `label` y/o `color` |
| Filas | Subir/bajar | `tier:move`: intercambio con vecina; en el borde → sin cambio |
| Filas | Vaciar | `tier:clear`: items al final del banco; vacía → sin cambio |
| Filas | Eliminar | `tier:delete`: items al final del banco; última fila → "La tier list necesita al menos una fila." |
| Sala | Título | `title:update` |
| Bitácora | Registro | Entrada nueva al inicio, se muestran 100 (`MAX_ACTIVITY`). Textos exactos: `subió X`, `movió X a la fila S` / `a el banco` (solo si cambia de contenedor), `eliminó X`, `añadió una fila`, `vació la fila S`, `eliminó la fila S`. **No** se registra: reordenar en la misma fila, renombrar/recolorear fila, subir/bajar fila, título |
| Bitácora | Autor | `{ name: apodo, color: colorFor(apodo) }` (`hsl(h 60% 70%)`) |
| Común | "Sin cambio" | Si la acción no aplica, el reducer devuelve la misma sala: no se guarda, no se emite, no se registra |

Fuera de alcance porque la demo no lo tiene (no se agrega): cuentas/login, roles o dueño de sala,
borrar salas, lista de salas, presencia ("quién está conectado"), chat, votos, paginación de bitácora,
edición de nombre de imagen, moderación. La sala `DEMO42` **se queda en el frontend** (ver Decisiones).

## 2. Modelo de datos

**Decisión clave: el estado de la sala se guarda como un documento `jsonb`.** El estado ya es un
documento pequeño (`Room`) que se transmite completo en cada evento, y todas las reglas son
operaciones sobre listas ordenadas (insertar en índice, intercambiar). Guardarlo como documento
permite portar `roomReducer` tal cual (función pura en C#) y portar sus 9 pruebas 1:1. Tablas
relacionales Filas/Items obligarían a reescribir el orden con columnas `Posicion` sin ganar nada
que la demo use. `ponytail:` documento por sala; si algún día hay reportes cruzados entre salas,
normalizar.

| Tabla | Columnas | Restricciones |
|---|---|---|
| `Salas` | `Codigo char(6) PK`, `Estado jsonb NOT NULL` (`{title, tiers, pool, items}`), `Version bigint NOT NULL DEFAULT 0`, `CreadaEn timestamptz` | CHECK `Codigo ~ '^[A-HJ-NP-Z2-9]{6}$'` |
| `Bitacora` | `Id bigint identity PK`, `SalaCodigo char(6)`, `Quien varchar(30)`, `Color varchar(24)`, `Texto varchar(120)`, `En timestamptz` | FK → `Salas` **CASCADE**; índice `(SalaCodigo, Id DESC)` |
| `Imagenes` | `Id uuid PK`, `SalaCodigo char(6)`, `Archivo varchar(80)`, `Bytes int`, `SubidaEn timestamptz` | FK → `Salas` **CASCADE**; CHECK `Bytes > 0` |

- `Version` sube en cada acción aplicada; viaja en el DTO para que el cliente descarte estados viejos.
- Serialización de escrituras por sala: `SELECT … FOR UPDATE` sobre la fila de `Salas` dentro de la
  transacción (dos personas arrastrando a la vez → se aplican en orden, ninguna se pierde).
- `Imagenes` registra cada archivo del volumen (para borrarlo con `item:delete` y detectar huérfanos).
- Ids: filas = string del cliente (validado, ver API); imágenes = `Guid` en formato `N` generado por el servidor.

## 3. Diseño de API

JSON camelCase. **Los nombres son los de `src/types.ts`** (`code, title, tiers[{id,label,color,items}], pool, items{id:{id,name,url,by}}`, `ActivityEntry{id,who{name,color},text,at}` con `at` en ms epoch) + `version`.
Errores HTTP como ProblemDetails con `detail` = mensaje de la demo; errores del hub como `HubException` con el mismo mensaje.

### HTTP

| Método y ruta | Responsabilidad | Entrada → Salida |
|---|---|---|
| `POST /api/salas` | Crear sala (`RoomService.create`) | `{ title }` → `201 Room` |
| `GET /api/salas/{codigo}` | Existe (`RoomService.exists`) | → `204` / `404` "No existe ninguna sala con el código X." |
| `POST /api/salas/{codigo}/imagenes` | Subir imagen + `item:add` + bitácora "subió X" + broadcast | multipart `archivo`, `nombre`, `apodo` → `201 Item` |
| `GET /imagenes/{codigo}/{archivo}` | Archivo estático del volumen | `Cache-Control: immutable`, con CORS (necesario para exportar PNG) |

Rate limit nativo por IP: crear 10/min, existe 30/min, subir 60/min → 429.

### Hub SignalR `/hubs/sala`

| Método (cliente → servidor) | Responsabilidad | Entrada → Salida |
|---|---|---|
| `Unirse(codigo, apodo)` | Valida sala, guarda apodo en la conexión, `Groups.AddToGroupAsync`, lee estado | → `{ room, activity }` (o `HubException` si no existe) |
| `Despachar(accion)` | Aplica acción con `FOR UPDATE`; si cambió: guarda, `Version++`, genera texto de bitácora, emite | `RoomAction` (sin `item:add`) → `void` |

| Evento (servidor → grupo) | Payload |
|---|---|
| `state` | `Room` (con `version`) |
| `activity` | `ActivityEntry[]` (últimas 100, igual que hoy) |

Acciones aceptadas: `item:move`, `item:delete`, `tier:update`, `tier:add`, `tier:move`, `tier:clear`,
`tier:delete`, `title:update` — mismo JSON que `RoomAction`, deserializado con
`[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]`.

**El cliente nunca manda lo que el servidor calcula:** el texto de bitácora y el color del autor los
genera el servidor desde la acción y el estado previo (el `text` que hoy pasa `act()` se ignora); el
`by` del item sale del apodo; el `url` lo arma el servidor. Validaciones en servidor: `MAX_TIERS`,
mínimo una fila, título ≤ 80, etiqueta ≤ 20, color `^#[0-9a-fA-F]{6}$`, id de fila nueva
`^[a-z0-9]{1,16}$` y no repetido, apodo 1–30, nombre ≤ 30, máx 200 imágenes por sala.

## 4. Arquitectura

```
rankeo-backend/
  Rankeo.Api/
    Program.cs              .env, DI, CORS, rate limit, ProblemDetails, SignalR, estáticos, migración al arrancar
    Data/RankeoDb.cs        DbContext (jsonb, CHECK, FK)
    Data/Migrations/
    Modelos/Entidades.cs    Sala, EntradaBitacora, Imagen
    Dominio/Room.cs         records Room/Tier/Item + RoomAction polimórfico (= src/types.ts)
    Dominio/RoomReducer.cs  port puro de roomReducer.ts
    Services/Calculos.cs    GenerarCodigo, NormalizarCodigo, ColorPara, TextoBitacora, NuevaSala
    Services/SalaService.cs crear, existe, cargar, aplicar(acción, apodo) en transacción
    Services/ImagenService.cs validar, guardar en disco, registrar, borrar
    Services/Errores.cs     ReglaException(400) / NoEncontradoException(404) + IExceptionHandler
    Hubs/SalaHub.cs         Unirse / Despachar → SalaService; emite state/activity
    Controllers/SalasController.cs
    Dockerfile .dockerignore
  Rankeo.Api.Tests/
    ApiFactory.cs           WebApplicationFactory + Testcontainers + HubConnection contra TestServer
  Rankeo.slnx  docker-compose.yml  .env.example  .gitignore  PLAN.md  CLAUDE.md
```

- **Controllers/Hub**: transporte. **Services**: reglas + EF Core directo. `RoomReducer` y `Calculos` puros.
- El hub emite **después** del commit (nunca estados no confirmados).
- Hora: `DateTimeOffset.UtcNow` (solo timestamps de bitácora; el cliente formatea). No hace falta `Reloj` de negocio: nada depende de "hoy".
- Sin auth: el código de sala es la llave (como hoy en la demo).
- Paquetes: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (misma versión de EF que trae Npgsql). Pruebas: `xunit`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.SignalR.Client`.
- **No** se usa: JWT/Identity, OpenAPI, repositorios, AutoMapper, MediatR, FluentValidation, ImageSharp (no se re-codifica la imagen; se valida por firma de bytes), Redis backplane (`ponytail:` una sola instancia de la API; si se escala a varias, `AddStackExchangeRedis`).

## 5. Infraestructura

`docker-compose.yml`:
- `db`: `postgres:17-alpine`, volumen `pgdata`, healthcheck `pg_isready`, publicado en `127.0.0.1:${DB_PORT:-5432}`.
- `api`: build `Rankeo.Api`, `depends_on: db (service_healthy)`, `${API_PORT:-8080}:8080`, volumen `imagenes:/data/imagenes`, `restart: unless-stopped`.

`.env.example`: `POSTGRES_PASSWORD` (obligatoria, `${VAR:?}`), `DB_PORT`, `API_PORT`,
`ConnectionStrings__Rankeo` (localhost para `dotnet run`; compose la arma con `Host=db`),
`CORS_ORIGINS` (lista separada por comas, p. ej. `http://localhost:5173`), `IMAGENES_DIR`
(default `/data/imagenes`), `IMAGEN_MAX_KB` (default 512).

Al arrancar: `Database.Migrate()` y crear `IMAGENES_DIR` si falta. Sin semilla (la demo vive en el frontend).

## 6. Testing

- **Unitarias `RoomReducer`**: los 9 casos de `roomReducer.test.ts` portados con los mismos nombres y valores, + "no aplica → misma referencia" por cada acción.
- **Unitarias `Calculos`**: `ColorPara` con vectores calculados por `colorFor` de JS (incluye emoji: JS itera code points y toma `charCodeAt(0)` = surrogate alto); `NormalizarCodigo("  ab-c 12 3 ")` = `"ABC123"`; `GenerarCodigo` nunca contiene `0 O 1 I` ni es `DEMO42`; `TextoBitacora` para cada acción (incluye "a el banco" y "sin texto al reordenar en la misma fila").
- **Integración HTTP**: crear (título vacío → "Mi Tier List", 5 filas), existe 204/404, normalización del código en la ruta, subida: firma inválida → 400, demasiado grande → 400, ok → 201 + archivo en disco + `GET /imagenes/...` 200 con CORS.
- **Integración hub** (dos `HubConnection` en la misma sala): A mueve → B recibe `state` y `activity`; acción que no aplica → nadie recibe nada; `tier:delete` de la última → `HubException` con el mensaje; sala inexistente → `HubException`; **concurrencia**: A y B mandan 20 movimientos cada uno a la vez → estado final consistente y `version` = movimientos aplicados.
- `item:delete` borra el archivo del volumen y la fila de `Imagenes`.

## 7. Fases de implementación

| Fase | Tareas | Depende de | Criterio de aceptación |
|---|---|---|---|
| 0 Base | `Rankeo.slnx`, `Rankeo.Api`, `Rankeo.Api.Tests`, compose solo `db`, `.env.example`, lector de `.env`, `ApiFactory` con Testcontainers | — | `dotnet test` verde con 1 prueba que arranca la API contra Postgres real |
| 1 Dominio | `Dominio/Room.cs`, `RoomReducer.cs`, `Calculos.cs` + unitarias | 0 | 9 casos portados + Calculos en verde |
| 2 Datos | Entidades, `RankeoDb` (jsonb, CHECK, FK), migración inicial, `Migrate()` al arrancar | 1 | Migración aplica en base vacía; prueba de CHECK de código |
| 3 Salas HTTP | `SalasController` (crear, existe), errores ProblemDetails, CORS, rate limit | 2 | Pruebas HTTP de §6 en verde |
| 4 Hub | `SalaHub` + `SalaService.Aplicar` (`FOR UPDATE`, `Version`, bitácora, emitir tras commit) | 3 | Pruebas de hub de §6 incluida concurrencia |
| 5 Imágenes | `ImagenService`, endpoint multipart, estáticos con CORS + caché, borrado en `item:delete` | 4 | Pruebas de subida/borrado en verde |
| 6 Docker | `Dockerfile` multi-stage, compose completo con volumen `imagenes` | 5 | `docker compose -p rankeo-prueba up --build`: crear sala, subir imagen, `down`+`up` y sala e imagen siguen; `down -v` |
| 7 Frontend | Migración de §8 en rama del frontend | 6 | Dos navegadores en la misma sala ven cada cambio; demo `DEMO42` sigue igual; `npm test` y `npm run build` verdes |
| 8 Cierre | Prueba manual de punta a punta, README del backend, actualizar `/docs` del frontend (`sh docs/docs.sh stale`) | 7 | Verificación final completa |

## 8. Migración del frontend

Principio: las vistas no cambian; solo se cambia la implementación de `RoomService`.

1. `npm i @microsoft/signalr`; `VITE_API_URL` en `.env` (+ `.env.example`).
2. `src/room/signalrRoomService.ts`: implementa `RoomService`.
   - `exists` → `GET`; `create` → `POST`; `load` → arranca conexión (`withAutomaticReconnect`) + `invoke('Unirse', code, getName())`.
   - `dispatch` → **aplica `roomReducer` local de forma optimista** y emite `state` al instante; luego `invoke('Despachar', action)`. El `state` del servidor reemplaza al optimista. Evita el "rebote" del arrastre (`TierBoard` suelta su vista previa al soltar).
   - `subscribe` → handlers `state`/`activity`; descarta `state` con `version` menor a la actual (cubre la carrera subscribe/load de `useRoom`).
   - `onreconnected` → vuelve a `Unirse` (los grupos de SignalR no sobreviven a la reconexión). Errores del hub → evento `warning` (toast que ya existe).
   - URLs de imagen relativas (`/imagenes/...`) → prefijo `VITE_API_URL` en un solo punto.
3. `roomService` en `roomService.ts` pasa a ser compuesto: `DEMO42` → `LocalRoomService` (sin cambios), resto → SignalR.
4. Subida: agregar `addImage(code, dataUrl, name, by)` a `RoomService`. Local: `item:add` como hoy. SignalR: `fetch(dataUrl).blob()` → `POST` multipart. `addImages` en `RoomPage` llama a `roomService.addImage` en vez de `act`.
5. `tier:add`: se sigue mandando el id de `newId()` (8 chars base36, pasa la validación del servidor).
6. Exportar PNG con imágenes de otro origen: `crossOrigin="anonymous"` en el `<img>` de `ItemCard` y `useCORS: true` en `exportPng`.
7. Actualizar `rankeo-frontend/docs` (data-layer, state-management, deployment) y quitar el aviso de "sin backend".

## Decisiones propuestas (no vienen de la demo; confirmar cuando toque)

1. **`DEMO42` se queda 100% en el frontend** (`LocalRoomService`): cada visitante tiene su propia copia que no se guarda, igual que hoy. Una demo compartida en el servidor la podría vandalizar cualquiera.
2. Etiqueta de fila máx 20 chars (la demo no tiene límite).
3. Máx 200 imágenes por sala e imagen máx 512 KB (el cliente ya manda ~10–30 KB).
4. Formatos aceptados: webp, png, jpeg, gif (por firma de bytes). Se guarda tal cual, sin re-codificar.
5. Bitácora: se guardan todas las entradas, se leen las últimas 100. `ponytail:` sin purga; agregar si crece.
6. Salas abandonadas no se borran. `ponytail:` agregar limpieza por antigüedad si el volumen crece.
7. `title:update` sigue mandándose por tecla (como hoy). `ponytail:` debounce en el cliente si la carga molesta.
8. Imágenes huérfanas (subida OK pero falló el `item:add`): se borran en la misma operación con try/finally; sin job de limpieza.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Latencia: el arrastre "rebota" hasta que responde el servidor | Reducer optimista en el cliente (§8.2) |
| Estado viejo pisa uno nuevo (carrera subscribe/load, reconexión) | Campo `version`, el cliente ignora versiones menores |
| Escrituras simultáneas en la misma sala | `SELECT … FOR UPDATE` por sala; prueba de concurrencia |
| Exportar PNG falla con imágenes de otro origen (canvas "tainted") | CORS en `/imagenes` + `crossOrigin` + `useCORS` (§8.6) |
| CORS + SignalR: credenciales | Orígenes explícitos desde `CORS_ORIGINS` + `AllowCredentials()`; nunca `*` |
| Proxy inverso sin soporte de WebSocket | Documentar `Upgrade`/`Connection` en el README; SignalR cae a long polling solo |
| Rate limit detrás de proxy (todos con la misma IP) | `UseForwardedHeaders` cuando haya proxy (`ponytail:`) |
| Enumerar códigos por el hub (sin rate limit) | Espacio de 32⁶ ≈ 10⁹ códigos; `ponytail:` limitar `Unirse` por conexión si aparece abuso |
| `colorFor` distinto entre JS y C# con emoji | Vectores de prueba generados con el JS real |
| Discriminador `type` no va primero en el JSON | `AllowOutOfOrderMetadataProperties = true` en las opciones JSON del hub |
| Version de `EntityFrameworkCore.Design` ≠ la de Npgsql (CS1705) | Fijarlas juntas en Fase 0 |
| Puertos 5432/8080 ocupados | Todo por variable; revisar con `netstat` en Fase 0 |
| Push al frontend | Resuelto: remoto `yaxoff` (YaxOFF/rankeo-frontend-); trabajar en rama nueva |

## Verificación final

1. `dotnet test` verde; `docker compose up --build` limpio.
2. Frontend con `VITE_API_URL` apuntando a la API: crear sala en navegador A, unirse con el código en navegador B (otro perfil).
3. En A: subir 3 imágenes, moverlas a S y A, añadir fila, renombrarla, vaciarla, borrar una imagen a la papelera y otra con Supr → B ve cada cambio y la misma bitácora.
4. Mover en A y B a la vez → ambos terminan con el mismo estado.
5. Matar la API y levantarla → los clientes se reconectan y ven el estado guardado.
6. Descargar PNG en B con imágenes subidas desde A.
7. `/sala/DEMO42` funciona sin tocar la API.
8. `docker compose down` + `up` → salas e imágenes siguen.

## Avance

- [x] **Plan** — plan aprobado, `PLAN.md` y `CLAUDE.md` creados, repo git local creado (2026-10-24).
- [x] **Fase 0 — Base** (2026-10-24)
- [ ] **Fase 1 — Dominio**
- [ ] **Fase 2 — Datos**
- [ ] **Fase 3 — Salas HTTP**
- [ ] **Fase 4 — Hub SignalR**
- [ ] **Fase 5 — Imágenes**
- [ ] **Fase 6 — Docker**
- [ ] **Fase 7 — Frontend**
- [ ] **Fase 8 — Cierre**

### Bitácora

<!-- Una entrada por fase terminada: fecha, qué se hizo, archivos clave, decisiones tomadas, pendientes para la siguiente fase. -->

#### Fase 0 — Base (2026-10-24)

- Hecho: `Rankeo.slnx` con `Rankeo.Api` (web vacío, net10.0) y `Rankeo.Api.Tests` (xUnit). Compose solo con
  `db` (`postgres:17-alpine`, healthcheck, `127.0.0.1:${DB_PORT}`). `.env.example` con todas las variables del plan.
  Remoto `origin` = https://github.com/YaxOFF/rankeo-backend.
- Archivos clave: `Rankeo.Api/Program.cs` (función `CargarEnv`: lee el primer `.env` subiendo desde la carpeta
  actual, sin pisar variables ya definidas; falla al arrancar si falta `ConnectionStrings__Rankeo`),
  `Rankeo.Api.Tests/ApiFactory.cs` (`WebApplicationFactory<Program>` + `PostgreSqlContainer`, fixture por clase),
  `Rankeo.Api.Tests/ArranqueTests.cs` (1 prueba: arranca la API y abre conexión con su cadena).
- Versiones fijadas: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (trae EF Core 10.0.4) +
  `Microsoft.EntityFrameworkCore.Design` 10.0.4. Tests: `Microsoft.AspNetCore.Mvc.Testing` 10.0.13,
  `Testcontainers.PostgreSql` 4.16.0.
- Decisiones: la `ApiFactory` pasa la cadena del contenedor por variable de entorno (gana sobre el `.env`
  local porque `CargarEnv` no pisa). `Program` es público sin `partial class` (.NET 10 lo genera).
  Se borró `appsettings.Development.json` (vacío de valor).
- Pendientes: en esta máquina los puertos 5432 y 8080 ya están ocupados → usar `DB_PORT=5433` (y ajustar
  `ConnectionStrings__Rankeo`) y otro `API_PORT` en el `.env` local. `Microsoft.AspNetCore.SignalR.Client`
  se agrega a las pruebas en Fase 4.

