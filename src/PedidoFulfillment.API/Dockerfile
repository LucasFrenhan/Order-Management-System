# Imagem da API para a demonstração (REQ-07, research R-07). Construída a partir da raiz do
# repositório: docker build -f src/PedidoFulfillment.API/Dockerfile .
# Versões fixas, sem "latest": o SDK acompanha o global.json e o runtime, os pacotes 10.0.13.

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS compilacao
WORKDIR /fonte

# Restauração numa camada própria, reaproveitada enquanto os projetos não mudam.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PedidoFulfillment.Domain/PedidoFulfillment.Domain.csproj src/PedidoFulfillment.Domain/
COPY src/PedidoFulfillment.Application/PedidoFulfillment.Application.csproj src/PedidoFulfillment.Application/
COPY src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj src/PedidoFulfillment.Infrastructure/
COPY src/PedidoFulfillment.API/PedidoFulfillment.API.csproj src/PedidoFulfillment.API/
RUN dotnet restore src/PedidoFulfillment.API/PedidoFulfillment.API.csproj

COPY src/ src/
# O projeto da API inclui o contrato aprovado, servido em /openapi/v1.yaml.
COPY specs/001-contratos-api/contracts/openapi-v1.yaml specs/001-contratos-api/contracts/
RUN dotnet publish src/PedidoFulfillment.API/PedidoFulfillment.API.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS execucao
WORKDIR /app
COPY --from=compilacao /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Usuário sem privilégios que a imagem já traz (app).
USER $APP_UID
ENTRYPOINT ["dotnet", "PedidoFulfillment.API.dll"]
