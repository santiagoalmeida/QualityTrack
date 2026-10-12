#!/usr/bin/env bash
# Credenciales de QualityTrack para scripts: pide empresa, usuario y contraseña una sola vez, las cambia
# por una clave API y guarda esa clave como secreto en el llavero del sistema.
#
# Como comando:
#   qualitytrack-auth.sh login     pide los datos y guarda la clave (reemplaza la que hubiera)
#   qualitytrack-auth.sh status    dice con qué empresa y usuario hay sesión, sin mostrar la clave
#   qualitytrack-auth.sh logout    borra la clave guardada
#
# Desde otro script:
#   source qualitytrack-auth.sh
#   qualitytrack_load_credentials || exit 1     # deja QUALITYTRACK_URL y QUALITYTRACK_API_KEY listas
#
# La contraseña no se guarda: solo viaja al servidor para emitir la clave, que se puede revocar desde
# "Claves API". Si ya existen QUALITYTRACK_URL y QUALITYTRACK_API_KEY en el entorno (CI), se usan tal cual.
# En una instalación nueva, el primer inicio de sesión crea la empresa y su administrador: por eso pide la
# contraseña dos veces y muestra empresa y usuario para confirmarlos antes de crear nada.
#
# Sin terminal (CI, tareas programadas) no se puede preguntar: usa las variables QUALITYTRACK_ORGANIZATION,
# QUALITYTRACK_USER y QUALITYTRACK_PASSWORD, o directamente QUALITYTRACK_API_KEY.
# Requiere curl y jq. Guarda en el llavero con secret-tool (libsecret); si no está, en un archivo con permisos 600.

QUALITYTRACK_DEFAULT_URL="http://localhost:5210"
_qt_config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/qualitytrack"

_qt_has_tty() { { : </dev/tty; } 2>/dev/null; }

_qt_has_keyring() { command -v secret-tool >/dev/null 2>&1; }

# URL del servidor: variable de entorno, la del último inicio de sesión, o la local por defecto.
_qt_url() {
  if [ -n "${QUALITYTRACK_URL:-}" ]; then
    echo "${QUALITYTRACK_URL%/}"
  elif [ -f "$_qt_config_dir/url" ]; then
    cat "$_qt_config_dir/url"
  else
    echo "$QUALITYTRACK_DEFAULT_URL"
  fi
}

# Secreto guardado para una URL: JSON con apiKey, organization y user. Vacío si no hay.
_qt_secret_read() {
  if _qt_has_keyring; then
    secret-tool lookup service qualitytrack url "$1" 2>/dev/null || true
  elif [ -f "$_qt_config_dir/credentials.json" ]; then
    jq -c --arg url "$1" '.[$url] // empty' "$_qt_config_dir/credentials.json"
  fi
}

_qt_secret_write() {
  local url="$1" secret="$2"
  mkdir -p "$_qt_config_dir"
  echo "$url" > "$_qt_config_dir/url"
  if _qt_has_keyring; then
    printf '%s' "$secret" | secret-tool store --label="QualityTrack ($url)" service qualitytrack url "$url"
  else
    local file="$_qt_config_dir/credentials.json" current="{}"
    [ -f "$file" ] && current="$(cat "$file")"
    (umask 077; jq --arg url "$url" --argjson secret "$secret" '.[$url] = $secret' <<<"$current" > "$file.tmp")
    mv "$file.tmp" "$file"
  fi
}

_qt_secret_clear() {
  if _qt_has_keyring; then
    secret-tool clear service qualitytrack url "$1" 2>/dev/null || true
  elif [ -f "$_qt_config_dir/credentials.json" ]; then
    local file="$_qt_config_dir/credentials.json"
    (umask 077; jq --arg url "$1" 'del(.[$url])' "$file" > "$file.tmp") && mv "$file.tmp" "$file"
  fi
}

# Pregunta en la terminal aunque la salida del script esté redirigida. $1 = texto, $2 = valor por defecto.
_qt_ask() {
  local answer
  if [ -n "${2:-}" ]; then
    read -r -p "$1 [$2]: " answer </dev/tty
    echo "${answer:-$2}"
  else
    read -r -p "$1: " answer </dev/tty
    echo "$answer"
  fi
}

# Pide empresa, usuario y contraseña, obtiene una clave API y la guarda. Deja las variables exportadas.
qualitytrack_login() {
  local url organization user password response status body
  url="$(_qt_url)"
  organization="${QUALITYTRACK_ORGANIZATION:-}"
  user="${QUALITYTRACK_USER:-}"
  password="${QUALITYTRACK_PASSWORD:-}"

  if [ -z "$organization" ] || [ -z "$user" ] || [ -z "$password" ]; then
    if ! _qt_has_tty; then
      echo "QualityTrack: no hay sesión y no hay terminal para pedir los datos." >&2
      echo "  Inicia sesión una vez con: ${BASH_SOURCE[0]} login" >&2
      return 1
    fi
    echo "Iniciar sesión en QualityTrack" >/dev/tty
    [ -n "${QUALITYTRACK_URL:-}" ] || url="$(_qt_ask "Servidor" "$url")"
    url="${url%/}"

    # En una instalación nueva este inicio de sesión crea la cuenta: un error de tecleo dejaría una
    # contraseña o un correo que nadie conoce, así que se pide la contraseña dos veces y se confirma.
    local fresh=false repeated answer
    if [ "$(curl -fsS "$url/api/v1/cli/status" 2>/dev/null | jq -r '.setupRequired' 2>/dev/null)" = true ]; then
      fresh=true
      echo "Instalación nueva: se creará la empresa y su cuenta de administración." >/dev/tty
    fi

    [ -n "$organization" ] || organization="$(_qt_ask "Empresa")"
    [ -n "$user" ] || user="$(_qt_ask "Usuario (correo)")"
    while [ -z "$password" ]; do
      read -r -s -p "Contraseña: " password </dev/tty
      echo >/dev/tty
      if [ "$fresh" = true ]; then
        read -r -s -p "Repite la contraseña: " repeated </dev/tty
        echo >/dev/tty
        if [ "$password" != "$repeated" ]; then
          echo "Las contraseñas no coinciden; inténtalo de nuevo." >/dev/tty
          password=""
        fi
        repeated=""
      fi
    done

    if [ "$fresh" = true ]; then
      read -r -p "Crear la empresa «$organization» con el usuario $user. ¿Continuar? [s/N]: " answer </dev/tty
      case "$answer" in
        s|S|si|sí|Si|Sí|SI|SÍ) ;;
        *) echo "QualityTrack: cancelado; no se creó nada." >&2; return 1 ;;
      esac
    fi
  fi

  # La contraseña va en el cuerpo, leído de la entrada estándar, para que no aparezca en la lista de procesos.
  response="$(jq -n --arg o "$organization" --arg e "$user" --arg p "$password" --arg k "$(hostname 2>/dev/null || echo cli)" \
      '{organization: $o, email: $e, password: $p, keyName: $k}' |
    curl -sS -w '\n%{http_code}' -X POST "$url/api/v1/cli/login" -H "Content-Type: application/json" --data-binary @-)" || {
    echo "QualityTrack: no se pudo conectar con $url." >&2
    return 1
  }
  password=""
  status="${response##*$'\n'}"
  body="${response%$'\n'*}"

  if [ "$status" != 200 ]; then
    echo "QualityTrack: $(jq -r '(.errors.request // [.title // "error"]) | join(" ")' <<<"$body" 2>/dev/null || echo "error $status")" >&2
    return 1
  fi

  _qt_secret_write "$url" "$(jq -c '{apiKey, organization, user: .email}' <<<"$body")" || {
    echo "QualityTrack: no se pudo guardar la clave." >&2
    return 1
  }
  if [ "$(jq -r '.organizationCreated' <<<"$body")" = true ]; then
    echo "QualityTrack: instalación nueva; se creó la empresa «$(jq -r '.organization' <<<"$body")» con $user como administrador." >&2
  fi
  echo "QualityTrack: sesión guardada para $user en $url." >&2

  export QUALITYTRACK_URL="$url"
  QUALITYTRACK_API_KEY="$(jq -r '.apiKey' <<<"$body")"
  export QUALITYTRACK_API_KEY
}

# Deja QUALITYTRACK_URL y QUALITYTRACK_API_KEY en el entorno: las que ya haya, las guardadas o, si hay
# terminal, las de un inicio de sesión nuevo.
qualitytrack_load_credentials() {
  if [ -n "${QUALITYTRACK_URL:-}" ] && [ -n "${QUALITYTRACK_API_KEY:-}" ]; then
    return 0
  fi

  local url secret
  url="$(_qt_url)"
  secret="$(_qt_secret_read "$url")"
  if [ -n "$secret" ]; then
    export QUALITYTRACK_URL="$url"
    QUALITYTRACK_API_KEY="$(jq -r '.apiKey' <<<"$secret")"
    export QUALITYTRACK_API_KEY
    return 0
  fi

  qualitytrack_login
}

qualitytrack_status() {
  local url secret
  url="$(_qt_url)"
  secret="$(_qt_secret_read "$url")"
  if [ -z "$secret" ]; then
    echo "Sin sesión en $url."
    return 1
  fi
  jq -r --arg url "$url" --arg where "$(_qt_has_keyring && echo "llavero del sistema" || echo "$_qt_config_dir/credentials.json")" \
    '"Sesión en \($url): \(.user) · \(.organization) (clave \(.apiKey[0:10])…, guardada en \($where))"' <<<"$secret"
}

qualitytrack_logout() {
  local url
  url="$(_qt_url)"
  _qt_secret_clear "$url"
  echo "Clave de $url borrada de este equipo. Para invalidarla en el servidor, revócala en «Claves API»."
}

# Ejecutado directamente (no con source): actúa como comando.
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
  set -euo pipefail
  case "${1:-}" in
    login) qualitytrack_login ;;
    status) qualitytrack_status ;;
    logout) qualitytrack_logout ;;
    *) sed -n '2,21p' "$0"; exit 2 ;;
  esac
fi
