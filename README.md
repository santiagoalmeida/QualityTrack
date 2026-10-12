# QualityTrack

Historial de calidad para cualquier aplicación. Cada vez que corren los tests y el análisis de código,
el pipeline (o un script local) reporta la corrida; QualityTrack la guarda con su rama y commit, y muestra
en un dashboard qué cambió desde la corrida anterior y cuánto se avanzó hacia las metas.

- **Datos por organización:** proyectos, usuarios y claves API pertenecen a una organización y toda consulta se limita a ella.
- **Agnóstico de la herramienta:** recibe un JSON con métricas numéricas. Sirve con SonarQube, con un reporte
  de cobertura o con cualquier número que el equipo quiera seguir.
- **Avance medible:** cada métrica puede tener una meta; el avance es el tramo recorrido entre el valor
  inicial y la meta, y el del proyecto es el promedio de sus metas.

![Dashboard de un proyecto: avance hacia las metas, cambio desde la corrida anterior y tendencias](docs/dashboard.png)

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
2. **Un script reporta la corrida.** Al terminar, envía un JSON con el proyecto, la rama, el commit y las
   métricas de cada componente (backend, frontend, un servicio…). Hay [scripts de ejemplo](#scripts-de-ejemplo)
   para SonarQube, .NET, Jest y pytest.
3. **QualityTrack la guarda y la compara.** El proyecto y sus componentes se crean solos con la primera
   corrida. Cada corrida se compara con la anterior **de la misma rama**, así el trabajo de una rama no se
   mezcla con el de otra.
4. **El dashboard muestra el avance.** Por métrica: valor anterior, valor actual y si el cambio es bueno o
   malo (subir la cobertura es bueno; subir los bugs, no). Por proyecto: el avance hacia las metas y cuánto
   se movió desde la última corrida.

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

El **avance del proyecto** es el promedio de sus metas (56 % en el ejemplo). Si en la corrida anterior era
48 %, el dashboard muestra **+8 pt desde la corrida anterior**. Una métrica que empeora más allá de su valor
inicial cuenta como 0 %, no como negativa.

### Qué se guarda

| Concepto | Qué es |
|---|---|
| Organización | El dueño de los datos: sus proyectos, usuarios y claves API. |
| Proyecto | Una aplicación seguida, identificada por una clave estable (`tienda-online`). |
| Componente | Una parte medida por separado: `api`, `web`, `backend`… |
| Corrida | Una ejecución de tests y análisis: rama, commit, fecha y si había cambios sin commitear. |
| Medición | El valor de una métrica de un componente en una corrida. |
| Meta | El objetivo de una métrica de un componente, con su valor inicial. |

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
    "project": "mi-app",
    "projectName": "Mi aplicación",
    "branch": "main",
    "commit": "a1b2c3d",
    "dirty": false,
    "source": "ci",
    "components": [
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
| `project` | Clave estable del proyecto (minúsculas, dígitos, `.`, `_`, `-`). Se crea con la primera corrida. |
| `branch` | Las comparaciones se hacen siempre contra la corrida anterior de la misma rama. |
| `commit`, `dirty`, `source`, `executedAt` | Opcionales. `dirty` marca corridas con cambios sin commitear. |
| `components[]` | Partes medidas por separado (backend, frontend…). Cada una lleva `tests`, `metrics` o ambos. |
| `metrics` | Clave → número. Las del catálogo reciben nombre, unidad y meta; cualquier otra se guarda igual. |

La respuesta (`201`) trae el avance tras la corrida y el de la anterior:

```json
{ "runId": "…", "project": "mi-app", "branch": "main", "projectCreated": false, "progress": 0.47, "previousProgress": 0.41 }
```

### Métricas del catálogo

`quality_gate` (1 = pasa), `coverage`, `line_coverage`, `branch_coverage`, `bugs`, `vulnerabilities`,
`security_hotspots`, `code_smells`, `technical_debt_minutes`, `duplicated_lines_density`, `open_issues`,
`reliability_rating`, `security_rating`, `maintainability_rating` (1 = A … 5 = E), `lines_of_code`, y las de
tests (`tests_total`, `tests_passed`, `tests_failed`, `tests_skipped`, `tests_duration_seconds`), que salen
del objeto `tests`.

La primera vez que un componente reporta `coverage`, `bugs`, `vulnerabilities`, `tests_failed` o
`duplicated_lines_density` se le asigna una meta por defecto (80 %, 0, 0, 0 y 3 %). Se cambian o se quitan
en la página **Metas** del proyecto.

## Scripts de ejemplo

Están en [`examples/`](examples). Todos leen `QUALITYTRACK_URL` y `QUALITYTRACK_API_KEY`, toman la rama y el
commit de git, y al terminar imprimen el avance. Se copian al repositorio de la aplicación y se adaptan.

| Script | Para | Requiere |
|---|---|---|
| [`report-sonarqube.sh`](examples/report-sonarqube.sh) | Métricas de uno o más proyectos de SonarQube | `curl`, `jq` |
| [`report-dotnet.sh`](examples/report-dotnet.sh) | Tests y cobertura de .NET, sin Sonar | `curl`, `jq`, `coverlet.collector` |
| [`report-jest.sh`](examples/report-jest.sh) | Tests y cobertura de Jest | `curl`, `jq` |
| [`report_pytest.py`](examples/report_pytest.py) | Tests y cobertura de pytest | Python 3 (biblioteca estándar) |

### SonarQube

Después del análisis, cada proyecto de Sonar se reporta como un componente:

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

| SonarQube | QualityTrack |
|---|---|
| `alert_status` (`OK` / `ERROR`) | `quality_gate` (1 / 0) |
| `coverage`, `line_coverage`, `branch_coverage` | igual |
| `bugs`, `vulnerabilities`, `security_hotspots`, `code_smells` | igual |
| `reliability_rating`, `security_rating` | igual (1 = A … 5 = E) |
| `sqale_rating` | `maintainability_rating` |
| `sqale_index` | `technical_debt_minutes` |
| `duplicated_lines_density` | igual |
| `ncloc` | `lines_of_code` |
| `tests`, `test_failures`, `test_errors`, `skipped_tests` | objeto `tests` |

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
    \"project\": \"tienda-online\",
    \"branch\": \"$(git rev-parse --abbrev-ref HEAD)\",
    \"commit\": \"$(git rev-parse HEAD)\",
    \"components\": [{
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

- Las mediciones se guardan en formato "largo" (corrida, componente, métrica, valor): una métrica nueva no
  requiere migración.
- Toda consulta lleva el id de la organización del usuario o de la clave API.
- Las claves API se guardan como hash SHA-256 y solo se muestran al crearlas; las contraseñas, con PBKDF2.

```bash
dotnet test
dotnet ef migrations add <Nombre> -p src/QualityTrack.Infrastructure -s src/QualityTrack.Web -o Persistence/Migrations
```

## Hoja de ruta

- Invitar usuarios y roles por proyecto (hoy cada organización tiene solo el administrador que la creó).
- Alta de más organizaciones en una misma instalación (hoy la crea la configuración inicial).
- PostgreSQL o SQL Server como proveedor, para instalaciones con varias instancias.
- Alertas cuando una corrida empeora, e insignias de estado para el repositorio.

## Autor

© 2026 Santiago Almeida. Todos los derechos reservados. Consulta [LICENSE](LICENSE).
