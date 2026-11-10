#!/usr/bin/env bash
# Exporta o OpenAPI do servico com a ferramenta 'swagger tofile' (Swashbuckle.AspNetCore.Cli, fixada em
# dotnet-tools.json na mesma versao do Swashbuckle da API) e garante que docs/openapi.json versionado reflita a API.
#
# Uso:
#   bash scripts/exportar-swagger.sh               regera docs/openapi.json (rode e versione quando a API mudar)
#   bash scripts/exportar-swagger.sh --verificar   gera em artifacts/openapi.json e FALHA se docs/openapi.json difere
#   --sem-build                                    reaproveita o build Release existente (o CI ja compilou)
#
# Para clonar em outro servico basta trocar as tres variaveis abaixo (e, se o servico nao tiver banco na partida, o
# bloco do MongoDB em "subir_mongo").
set -euo pipefail

cd "$(dirname "$0")/.."

API_PROJETO="src/OficinaMecanica.Execucao.API/OficinaMecanica.Execucao.API.csproj"
API_DLL="src/OficinaMecanica.Execucao.API/bin/Release/net8.0/OficinaMecanica.Execucao.API.dll"
DOCUMENTO="v1"

VERSIONADO="docs/openapi.json"
GERADO="artifacts/openapi.json"

# O 'swagger tofile' executa o Program.Main e espera o host SUBIR (so nao abre a porta), e a partida da Execucao cria
# os indices no MongoDB (MongoInitializer): sem um banco acessivel o host nao sobe e nenhum documento sai. Um mongod
# comum basta (sem replica set, porque o outbox fica desligado abaixo). SWAGGER_MONGO_URI aponta para um Mongo que ja
# exista e dispensa o Docker.
MONGO_IMAGEM="mongo:7.0.45"
MONGO_CONTAINER="execucao-swagger-mongo-$$"
MONGO_URI="${SWAGGER_MONGO_URI:-}"

VERIFICAR=false
COMPILAR=true
for argumento in "$@"; do
  case "$argumento" in
    --verificar) VERIFICAR=true ;;
    --sem-build) COMPILAR=false ;;
    *) echo "Argumento desconhecido: $argumento (use --verificar e/ou --sem-build)" >&2; exit 2 ;;
  esac
done

subir_mongo() {
  [ -z "$MONGO_URI" ] || return 0

  if ! command -v docker > /dev/null || ! docker info > /dev/null 2>&1; then
    echo "ERRO: Docker indisponivel para subir o MongoDB da exportacao; defina SWAGGER_MONGO_URI com um MongoDB existente." >&2
    exit 1
  fi

  trap 'docker rm -f "$MONGO_CONTAINER" > /dev/null 2>&1 || true' EXIT
  docker run -d --rm --name "$MONGO_CONTAINER" -p 127.0.0.1::27017 "$MONGO_IMAGEM" > /dev/null

  local porta resposta
  porta="$(docker port "$MONGO_CONTAINER" 27017/tcp | head -n 1 | sed -E 's/.*:([0-9]+)$/\1/')"
  for _ in $(seq 1 60); do
    # O mongod leva alguns segundos para aceitar conexao; so o "ok: 1" do ping libera a partida da API.
    resposta="$(docker exec "$MONGO_CONTAINER" mongosh --quiet --eval 'db.adminCommand({ ping: 1 }).ok' 2> /dev/null | tr -d '\r\n' || true)"
    if [ "$resposta" = "1" ]; then
      MONGO_URI="mongodb://127.0.0.1:${porta}/?directConnection=true"
      return 0
    fi
    sleep 1
  done

  echo "ERRO: o MongoDB temporario ($MONGO_CONTAINER) nao respondeu em 60 s." >&2
  exit 1
}

dotnet tool restore > /dev/null
if [ "$COMPILAR" = true ]; then
  dotnet build "$API_PROJETO" -c Release
fi

gerar() {
  local destino="$1" temporario
  mkdir -p "$(dirname "$destino")"
  temporario="$(mktemp)"

  # Optei por Testing + transporte em memoria + sem outbox para a partida nao exigir RabbitMQ nem replica set (o
  # validador da mensageria so aceita esses atalhos em Development/Testing), e por JwksUri de fachada (nunca e
  # consultado: nenhuma requisicao chega a ser autenticada). O 'swagger tofile' roda com o diretorio atual na raiz do
  # repositorio, entao o appsettings.json da API nao e lido e o que a partida exige vai por variavel de ambiente.
  # Testing e Production geram o mesmo documento: a API nao tem controller nem rota por ambiente.
  env ASPNETCORE_ENVIRONMENT=Testing \
      Mensageria__Transporte=EmMemoria \
      Mensageria__Outbox=Nenhum \
      ConnectionStrings__MongoDb="$MONGO_URI" \
      JwtSettings__JwksUri=https://exemplo.invalid/.well-known/jwks.json \
      dotnet swagger tofile --output "$temporario" "$API_DLL" "$DOCUMENTO" > /dev/null

  # Final de linha sempre LF, em dois niveis: o gerador escreve CRLF no Windows (arquivo), e o compilador junta as linhas
  # dos comentarios XML multilinha com o separador da plataforma, o que deixa "\r\n" DENTRO das strings do JSON. Sem
  # normalizar os dois, o arquivo versionado dependeria de onde foi gerado e o CI (Linux) reprovaria o que saiu do Windows.
  tr -d '\r' < "$temporario" | sed 's/\\r\\n/\\n/g' > "$destino"
  [ -n "$(tail -c 1 "$destino")" ] && echo >> "$destino"
  rm -f "$temporario"
}

subir_mongo

if [ "$VERIFICAR" = false ]; then
  gerar "$VERSIONADO"
  echo "OpenAPI exportado para $VERSIONADO"
  exit 0
fi

gerar "$GERADO"
echo "OpenAPI gerado em $GERADO"

if [ ! -f "$VERSIONADO" ]; then
  echo "ERRO: $VERSIONADO nao existe. Rode 'bash scripts/exportar-swagger.sh' e versione o arquivo." >&2
  exit 1
fi

if ! diff -u --strip-trailing-cr "$VERSIONADO" "$GERADO" > artifacts/openapi.diff; then
  echo "ERRO: $VERSIONADO esta desatualizado em relacao a API. Diferencas (versionado -> gerado):" >&2
  head -n 60 artifacts/openapi.diff >&2
  echo "Rode 'bash scripts/exportar-swagger.sh' e versione o resultado." >&2
  exit 1
fi

echo "OK: $VERSIONADO esta em dia com a API."
