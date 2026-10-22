# Imagem do Worker para a demonstração (REQ-06, research R-10). Construída a partir da raiz do
# repositório: docker build -f src/PedidoFulfillment.Worker/Dockerfile .
# Versões fixas, sem "latest", iguais às da imagem da API (REQ-07).

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS compilacao
WORKDIR /fonte

# Restauração numa camada própria, reaproveitada enquanto os projetos não mudam.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PedidoFulfillment.Domain/PedidoFulfillment.Domain.csproj src/PedidoFulfillment.Domain/
COPY src/PedidoFulfillment.Application/PedidoFulfillment.Application.csproj src/PedidoFulfillment.Application/
COPY src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj src/PedidoFulfillment.Infrastructure/
COPY src/PedidoFulfillment.Worker/PedidoFulfillment.Worker.csproj src/PedidoFulfillment.Worker/
RUN dotnet restore src/PedidoFulfillment.Worker/PedidoFulfillment.Worker.csproj

COPY src/ src/
RUN dotnet publish src/PedidoFulfillment.Worker/PedidoFulfillment.Worker.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS execucao
WORKDIR /app
COPY --from=compilacao /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Usuário sem privilégios que a imagem já traz (app).
USER $APP_UID
ENTRYPOINT ["dotnet", "PedidoFulfillment.Worker.dll"]
