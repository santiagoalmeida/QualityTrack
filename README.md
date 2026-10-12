# QualityTrack

Historial de calidad para cualquier aplicación. Cada vez que corren los tests y el análisis de código,
el pipeline (o un script local) reporta la corrida; QualityTrack la guarda con su rama y commit, y muestra
en un dashboard qué cambió desde la corrida anterior y cuánto se avanzó hacia las metas.

- **Datos por organización:** soluciones, usuarios y claves API pertenecen a una organización y toda consulta se limita a ella.
- **Agnóstico de la herramienta:** recibe un JSON con métricas numéricas. Sirve con SonarQube, con un reporte
  de cobertura o con cualquier número que el equipo quiera seguir.
- **Avance medible:** cada métrica puede tener una meta; el avance es el tramo recorrido entre el valor
  inicial y la meta, y el de la solución es el promedio de sus metas.

![Dashboard de una solución: avance hacia las metas, cambio desde la corrida anterior y tendencias](docs/dashboard.png)

## Cómo funciona

```
 tests + análisis            POST /api/v1/runs             dashboard
┌──────────────────┐  JSON  ┌──────────────────────┐      ┌────────────────────────┐
│ CI o script local│ ─────▶ │ QualityTrack guarda  │ ───▶ │ compara con la corrida │
│ (Sonar, cobertura│        │ la corrida con rama, │      │ anterior de la rama y  │
│  reportes, etc.) │        │ commit y métricas    │      │ mide el avance         │
└──────────────────┘        └──────────────────────┘      └────────────────────────┘
```

1. **Tu pipeline mide.** Corre los tests y el análisis como siempre. QualityTrack no ejecuta nada en tu
   código: solo recibe números.
2. **Un script reporta la corrida.** Al terminar, envía un JSON con la solución, la rama, el commit y las
   métricas de cada proyecto (backend, frontend, un servicio…). Hay [scripts de ejemplo](#scripts-de-ejemplo)
   para SonarQube, .NET, Jest y pytest.
3. **QualityTrack la guarda y la compara.** La solución y sus proyectos se crean solos con la primera
   corrida. Cada corrida se compara con la anterior **de la misma rama**, así el trabajo de una rama no se
   mezcla con el de otra.
4. **El dashboard muestra el avance.** Por métrica: valor anterior, valor actual y si el cambio es bueno o
   malo (subir la cobertura es bueno; subir los bugs, no). Por solución: el avance hacia las metas y cuánto
   se movió desde la última corrida.

### Pantallas

Todas las pantallas leen la corrida en tres categorías: **calidad**, **seguridad** y **mantenibilidad y
deuda técnica**.

- **Solución** (la pantalla de inicio cuando solo hay una): avance hacia las metas, cambio desde la corrida
  anterior, una tarjeta por proyecto con las cifras principales de cada categoría, las tendencias y el
  historial de corridas.
- **Proyecto** (desde su tarjeta): una sección por categoría con todas sus métricas (valor anterior,
  actual, cambio y meta), el avance de la categoría y sus tendencias.
- **Tipos de incidencia** (al hacer clic en una métrica, por ejemplo *Code smells*): las reglas que la
  componen, con cuántas incidencias y archivos afecta cada una.
- **Incidencias de un tipo** (al hacer clic en una regla): cada ubicación con su archivo y línea, el bloque
  de código con la parte a corregir marcada, la sugerencia de la regla y un prompt listo para copiar en
  Claude, GitHub Copilot o Codex, tanto para una incidencia como para todas las de la regla.
- **Metas** y **Claves API**.

### Cómo se calcula el avance

Cada métrica puede tener una **meta**. Al fijarla se guarda el valor que la métrica tenía en ese momento
(el **valor inicial**), y el avance es el tramo recorrido entre ambos:

```
avance = (valor inicial − valor actual) / (valor inicial − meta)      limitado entre 0 % y 100 %
```

| Métrica | Valor inicial | Meta | Actual | Avance |
|---|---|---|---|---|
| Cobertura | 60 % | 80 % | 70 % | 50 % |
| Bugs | 4 | 0 | 1 | 75 % |
| Vulnerabilidades | 3 | 0 | 3 | 0 % |
| Tests fallidos | 0 | 0 | 0 | 100 % (ya cumplida) |

El **avance de la solución** es el promedio de sus metas (56 % en el ejemplo). Si en la corrida anterior era
48 %, el dashboard muestra **+8 pt desde la corrida anterior**. Una métrica que empeora más allá de su valor
inicial cuenta como 0 %, no como negativa.

### Qué se guarda

| Concepto | Qué es |
|---|---|
| Organización | El dueño de los datos: sus soluciones, usuarios y claves API. |
| Solución | Una aplicación seguida, identificada por una clave estable (`tienda-online`). |
| Proyecto | Una parte de la solución medida por separado: `api`, `web`, `backend`… |
| Corrida | Una ejecución de tests y análisis: rama, commit, fecha y si había cambios sin commitear. |
| Medición | El valor de una métrica de un proyecto en una corrida. |
| Meta | El objetivo de una métrica de un proyecto, con su valor inicial. |

## Licencia en breve

QualityTrack es **código disponible, no código abierto**. Es propiedad intelectual de
[Santiago Almeida](https://github.com/santiagoalmeida).

| Quién | Uso |
|---|---|
| Desarrolladores independientes con ingresos de hasta USD 10.000 al año | Gratis, también en proyectos comerciales propios |
| Cualquiera, para evaluarlo | Gratis durante 30 días, fuera de producción |
| Empresas y organizaciones, o personas por encima de ese umbral | Licencia comercial, negociada con el autor |

Los términos completos están en [LICENSE](LICENSE). Para una licencia comercial, escribe a Santiago Almeida
desde su [perfil de GitHub](https://github.com/santiagoalmeida).

## Arrancar

Requiere .NET 10.

```bash
dotnet run --project src/QualityTrack.Web    # http://localhost:5210
```

La primera visita lleva a **Configuración inicial**, donde se crea la organización y su administrador.
Después, en **Claves API**, se crea la clave con la que se reportan las corridas.

La base de datos es SQLite (`qualitytrack.db`, junto al ejecutable) y las migraciones se aplican al arrancar.
Para cambiar su ubicación: `ConnectionStrings__QualityTrack="Data Source=/ruta/qualitytrack.db"`.

Con Docker:

```bash
docker build -t qualitytrack .
docker run -p 8080:8080 -v qualitytrack-data:/data qualitytrack
```

La aplicación sirve HTTP; en producción va detrás de un proxy que termine TLS.

## Reportar una corrida

```bash
curl -X POST http://localhost:5210/api/v1/runs \
  -H "X-Api-Key: $QUALITYTRACK_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "solution": "mi-app",
    "solutionName": "Mi aplicación",
    "branch": "main",
    "commit": "a1b2c3d",
    "dirty": false,
    "source": "ci",
    "projects": [
      {
        "key": "backend",
        "name": "Backend",
        "tests": { "total": 120, "passed": 118, "failed": 2, "skipped": 0 },
        "metrics": { "coverage": 61.5, "bugs": 3, "vulnerabilities": 1 }
      }
    ]
  }'
```

| Campo | Notas |
|---|---|
| `solution` | Clave estable de la solución (minúsculas, dígitos, `.`, `_`, `-`). Se crea con la primera corrida. |
| `branch` | Las comparaciones se hacen siempre contra la corrida anterior de la misma rama. |
| `commit`, `dirty`, `source`, `executedAt` | Opcionales. `dirty` marca corridas con cambios sin commitear. |
| `projects[]` | Proyectos de la solución, medidos por separado (backend, frontend…). Cada uno lleva `tests`, `metrics` o ambos. |
| `metrics` | Clave → número. Las del catálogo reciben nombre, unidad y meta; cualquier otra se guarda igual. |

La respuesta (`201`) trae el avance tras la corrida y el de la anterior:

```json
{
  "runId": "…", "solution": "mi-app", "branch": "main", "solutionCreated": false,
  "progress": 0.47, "previousProgress": 0.41,
  "projects": [
    {
      "key": "backend", "name": "Backend",
      "metrics": [
        {
          "key": "coverage", "name": "Cobertura", "category": "quality", "unit": "percent",
          "current": 61.5, "previous": 60.2, "delta": 1.3, "trend": "improved",
          "goal": { "baseline": 55, "target": 80, "progress": 0.26, "met": false }
        }
      ]
    }
  ]
}
```

`projects` trae cada métrica con su categoría (`quality`, `security`, `maintainability` u `other`) y
comparada con la corrida anterior de la rama (`trend`: `improved`, `worsened` o `unchanged`), para que el script que reporta pueda mostrar el resultado sin abrir el dashboard.

### Detalle de incidencias (opcional)

Para navegar desde un conteo hasta la línea a corregir, la corrida puede llevar el detalle: `findings` en
cada proyecto y `rules` en la raíz.

```json
{
  "solution": "mi-app", "branch": "main",
  "rules": [
    { "key": "csharpsquid:S1656", "name": "Variables should not be self-assigned",
      "description": "Texto plano: por qué es un problema.", "howToFix": "Texto plano: cómo corregirlo." }
  ],
  "projects": [
    {
      "key": "backend",
      "metrics": { "code_smells": 12 },
      "findings": [
        {
          "metric": "code_smells", "rule": "csharpsquid:S1656", "severity": "major",
          "message": "Remove or correct this useless self-assignment.",
          "file": "src/Tienda/Pedidos.cs", "line": 42, "endLine": 42, "startColumn": 8, "endColumn": 35,
          "snippet": { "startLine": 39, "lines": ["…", "…", "…", "        pedido.Total = pedido.Total;", "…"] }
        }
      ]
    }
  ]
}
```

- `metric` enlaza la incidencia con la métrica que la cuenta; `file` es relativo a la raíz del repositorio.
- `startColumn` y `endColumn` (desde 0) marcan el tramo exacto dentro de la línea; son opcionales.
- Las descripciones son texto plano: el HTML no se interpreta.
- **Solo se conserva el detalle de la última corrida de cada rama**, porque incluye código fuente. Una
  corrida que no envía `findings` deja intacto el detalle guardado.
- El detalle contiene fragmentos de tu código: quedan almacenados en la instancia de QualityTrack.

### Métricas del catálogo

Todo informe se lee en tres categorías, y cada métrica del catálogo pertenece a una:

| Categoría | Métricas |
|---|---|
| **Calidad** | `quality_gate` (1 = pasa), `reliability_rating`, `bugs`, `coverage`, `line_coverage`, `branch_coverage` y las de tests (`tests_total`, `tests_passed`, `tests_failed`, `tests_skipped`, `tests_duration_seconds`), que salen del objeto `tests` |
| **Seguridad** | `security_rating`, `vulnerabilities`, `security_review_rating`, `security_hotspots`, `security_hotspots_reviewed` |
| **Mantenibilidad y deuda técnica** | `maintainability_rating`, `code_smells`, `technical_debt_minutes`, `technical_debt_ratio`, `duplicated_lines_density`, `duplicated_lines`, `duplicated_blocks`, `open_issues`, `blocker_issues`, `critical_issues`, `complexity`, `cognitive_complexity`, `lines_of_code` |

Las notas (`*_rating`) van de 1 = A a 5 = E. Una clave fuera del catálogo se guarda igual y aparece en
"Otras métricas", solo en el detalle del proyecto.

La primera vez que un proyecto reporta `coverage`, `bugs`, `vulnerabilities`, `tests_failed` o
`duplicated_lines_density` se le asigna una meta por defecto (80 %, 0, 0, 0 y 3 %). Se cambian o se quitan
en la página **Metas** de la solución.

## Scripts de ejemplo

Están en [`examples/`](examples). Los que reportan leen `QUALITYTRACK_URL` y `QUALITYTRACK_API_KEY`, toman la
rama y el commit de git, y al terminar imprimen el avance. Se copian al repositorio de la aplicación y se adaptan.

| Script | Para | Requiere |
|---|---|---|
| [`qualitytrack-auth.sh`](examples/qualitytrack-auth.sh) | Pedir las credenciales una vez y guardar la clave en el llavero | `curl`, `jq`, `secret-tool` (opcional) |
| [`report-sonarqube.sh`](examples/report-sonarqube.sh) | Métricas de uno o más proyectos de SonarQube y, con Python, el detalle de sus incidencias | `curl`, `jq`, `python3` (opcional) |
| [`sonar_findings.py`](examples/sonar_findings.py) | Exportar de SonarQube cada incidencia con su código y la explicación de su regla | Python 3 (biblioteca estándar) |
| [`report-dotnet.sh`](examples/report-dotnet.sh) | Tests y cobertura de .NET, sin Sonar | `curl`, `jq`, `coverlet.collector` |
| [`report-jest.sh`](examples/report-jest.sh) | Tests y cobertura de Jest | `curl`, `jq` |
| [`report_pytest.py`](examples/report_pytest.py) | Tests y cobertura de pytest | Python 3 (biblioteca estándar) |

### Iniciar sesión desde un script

[`qualitytrack-auth.sh`](examples/qualitytrack-auth.sh) evita tener claves en archivos del repositorio. La
primera vez pide **empresa, usuario y contraseña**, los cambia por una clave API y guarda esa clave como
secreto en el llavero del sistema (libsecret, con `secret-tool`); las siguientes corridas no preguntan nada.

```bash
source examples/qualitytrack-auth.sh
qualitytrack_load_credentials || exit 1     # deja QUALITYTRACK_URL y QUALITYTRACK_API_KEY listas
```

```text
Iniciar sesión en QualityTrack
Servidor [http://localhost:5210]:
Empresa: Mi Empresa
Usuario (correo): ana@miempresa.com
Contraseña:
QualityTrack: sesión guardada para ana@miempresa.com en http://localhost:5210.
```

- **La contraseña no se guarda.** Solo viaja al servidor (`POST /api/v1/cli/login`) para emitir la clave,
  que queda en "Claves API" con el nombre del equipo y se puede revocar.
- **En una instalación nueva**, ese primer inicio de sesión crea la empresa y su administrador, igual que
  la pantalla de configuración inicial. Como ahí un error de tecleo dejaría una cuenta inaccesible, pide la
  contraseña dos veces y muestra la empresa y el usuario para confirmarlos antes de crear nada.
- **Sin terminal** (CI) no se puede preguntar: define `QUALITYTRACK_URL` y `QUALITYTRACK_API_KEY`, o bien
  `QUALITYTRACK_ORGANIZATION`, `QUALITYTRACK_USER` y `QUALITYTRACK_PASSWORD`.
- `qualitytrack-auth.sh status` muestra con qué cuenta hay sesión y `qualitytrack-auth.sh logout` la borra.
- Sin `secret-tool`, la clave se guarda en `~/.config/qualitytrack/credentials.json` con permisos 600.

### SonarQube

Después del análisis, cada proyecto de Sonar se reporta como un proyecto de la solución:

```bash
export QUALITYTRACK_URL=https://qualitytrack.miempresa.com
export QUALITYTRACK_API_KEY=qt_...
export SONAR_HOST_URL=http://localhost:9000
export SONAR_TOKEN=squ_...

sonar-scanner                                   # o dotnet-sonarscanner, o el scanner de Maven/Gradle
examples/report-sonarqube.sh tienda-online api=tienda-api web=tienda-web
# Avance: 47 % (antes 41 %)
```

El script consulta `api/measures/component` y traduce las métricas de Sonar a las del catálogo:

| Categoría | SonarQube | QualityTrack |
|---|---|---|
| Calidad | `alert_status` (`OK` / `ERROR`) | `quality_gate` (1 / 0) |
| Calidad | `reliability_rating`, `bugs` | igual |
| Calidad | `coverage`, `line_coverage`, `branch_coverage` | igual |
| Calidad | `tests`, `test_failures`, `test_errors`, `skipped_tests` | objeto `tests` |
| Seguridad | `security_rating`, `vulnerabilities` | igual |
| Seguridad | `security_review_rating`, `security_hotspots`, `security_hotspots_reviewed` | igual |
| Mantenibilidad | `sqale_rating` | `maintainability_rating` |
| Mantenibilidad | `code_smells` | igual |
| Mantenibilidad | `sqale_index`, `sqale_debt_ratio` | `technical_debt_minutes`, `technical_debt_ratio` |
| Mantenibilidad | `duplicated_lines_density`, `duplicated_lines`, `duplicated_blocks` | igual |
| Mantenibilidad | `violations`, `blocker_violations`, `critical_violations` | `open_issues`, `blocker_issues`, `critical_issues` |
| Mantenibilidad | `complexity`, `cognitive_complexity` | igual |
| Mantenibilidad | `ncloc` | `lines_of_code` |

Sonar omite las medidas que no aplican (por ejemplo, hotspots revisados cuando no hay hotspots); esas no
se envían.

Si `python3` está disponible, el script llama a `sonar_findings.py` y envía también el detalle de cada
incidencia. El código se lee del árbol de trabajo (el token de análisis de Sonar no suele poder leer
fuentes), así que cada proyecto puede indicar su carpeta dentro del repositorio: `web=tienda-web:web`.

El servidor de Sonar procesa el análisis unos segundos después de que termina el scanner; si el script
corre de inmediato puede leer las métricas del análisis anterior. En CI, espera al quality gate
(`-Dsonar.qualitygate.wait=true`) antes de reportar.

### .NET sin Sonar

Corre `dotnet test` con cobertura y lee los reportes TRX y Cobertura:

```bash
examples/report-dotnet.sh tienda-online api tests/Tienda.Tests
```

### Jest

Desde la carpeta del paquete; usa `jest --json` y el reporte `json-summary` de cobertura:

```bash
cd web && ../examples/report-jest.sh tienda-online web
```

### pytest

```bash
pytest --junitxml=junit.xml --cov --cov-report=xml
examples/report_pytest.py tienda-online api
```

### GitHub Actions

La clave va en un secreto del repositorio y la URL en una variable. `BRANCH` evita que en un pull request
se reporte la referencia `HEAD` en lugar del nombre de la rama.

```yaml
jobs:
  quality:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Tests y reporte a QualityTrack
        env:
          QUALITYTRACK_URL: ${{ vars.QUALITYTRACK_URL }}
          QUALITYTRACK_API_KEY: ${{ secrets.QUALITYTRACK_API_KEY }}
          BRANCH: ${{ github.head_ref || github.ref_name }}
        run: examples/report-dotnet.sh tienda-online api tests/Tienda.Tests
```

### Cualquier otra herramienta

Si tu herramienta produce un número, se puede seguir: mándalo en `metrics` con la clave que quieras.

```bash
curl -X POST "$QUALITYTRACK_URL/api/v1/runs" \
  -H "X-Api-Key: $QUALITYTRACK_API_KEY" -H "Content-Type: application/json" \
  -d "{
    \"solution\": \"tienda-online\",
    \"branch\": \"$(git rev-parse --abbrev-ref HEAD)\",
    \"commit\": \"$(git rev-parse HEAD)\",
    \"projects\": [{
      \"key\": \"web\",
      \"metrics\": { \"bundle_size_kb\": 512, \"lighthouse_performance\": 91, \"eslint_warnings\": 14 }
    }]
  }"
```

Las claves fuera del catálogo se muestran con su propio nombre y sin juicio de "mejor o peor".

## Arquitectura

```
src/
  QualityTrack.Domain          Entidades, catálogo de métricas y cálculo de avance. Sin dependencias.
  QualityTrack.Application     Casos de uso: ingesta, dashboard, metas, claves API, cuentas.
  QualityTrack.Infrastructure  EF Core (SQLite), migraciones, hash de contraseñas.
  QualityTrack.Web             Blazor (render estático en servidor), API de ingesta, autenticación.
tests/
  QualityTrack.Tests           xUnit sobre SQLite en memoria.
```

Las dependencias apuntan hacia adentro: Web → Infrastructure → Application → Domain.

- Las mediciones se guardan en formato "largo" (corrida, proyecto, métrica, valor): una métrica nueva no
  requiere migración.
- Toda consulta lleva el id de la organización del usuario o de la clave API.
- Las claves API se guardan como hash SHA-256 y solo se muestran al crearlas; las contraseñas, con PBKDF2.

```bash
dotnet test
dotnet ef migrations add <Nombre> -p src/QualityTrack.Infrastructure -s src/QualityTrack.Web -o Persistence/Migrations
```

## Hoja de ruta

- Invitar usuarios y roles por solución (hoy cada organización tiene solo el administrador que la creó).
- Alta de más organizaciones en una misma instalación (hoy la crea la configuración inicial).
- PostgreSQL o SQL Server como proveedor, para instalaciones con varias instancias.
- Alertas cuando una corrida empeora, e insignias de estado para el repositorio.

## Autor

© 2026 Santiago Almeida. Todos los derechos reservados. Consulta [LICENSE](LICENSE).
